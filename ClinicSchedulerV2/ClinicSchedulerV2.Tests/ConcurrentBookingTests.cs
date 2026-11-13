using ClinicSchedulerV2.Data.Database;
using Npgsql;

namespace ClinicSchedulerV2.Tests;

/// <summary>
/// A trigger that reads other rows cannot see another session's uncommitted insert, so two
/// bookings made at the same moment could each pass the check. The port closes that gap with
/// advisory locks; these tests commit real rows from two connections to prove it.
/// </summary>
public class ConcurrentBookingTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private const string InsertAppointment =
        "INSERT INTO appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time) VALUES ";

    [Fact]
    public async Task Second_simultaneous_booking_for_a_provider_waits_and_is_then_rejected()
    {
        await using var first = await db.OpenAsync();
        await using var second = await db.OpenAsync();

        await using var firstTx = await first.BeginTransactionAsync();
        await ExecAsync(first, firstTx, InsertAppointment + "(3, 1, 2, 2, 9, '2026-12-09 09:00', '2026-12-09 09:30')");

        // Same provider, same time, different patient and room: only the provider rule can stop it
        var secondInsert = ExecAsync(second, null, InsertAppointment + "(12, 1, 1, 2, 9, '2026-12-09 09:00', '2026-12-09 09:30')");
        await Task.Delay(500);
        Assert.False(secondInsert.IsCompleted, "the second booking should wait for the first to commit");

        await firstTx.CommitAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => secondInsert);
        Assert.Equal("rule_provider_double_booked", DbRuleViolation.From(ex)?.Constraint);
    }

    [Fact]
    public async Task Simultaneous_bookings_cannot_overfill_a_room()
    {
        // The Clear Lake infusion suite (room 15, 3 chairs) is empty on Dec 10. Six nurses-and-patients
        // race for it at once; exactly three may win.
        var attempts = new[]
        {
            (Patient: 1, Provider: 6), (Patient: 2, Provider: 7), (Patient: 3, Provider: 8),
            (Patient: 4, Provider: 6), (Patient: 5, Provider: 7), (Patient: 6, Provider: 8),
        };

        var results = await Task.WhenAll(attempts.Select(async a =>
        {
            await using var connection = await db.OpenAsync();
            try
            {
                await ExecAsync(connection, null,
                    InsertAppointment + $"({a.Patient}, {a.Provider}, 15, 3, 12, '2026-12-10 09:00', '2026-12-10 11:00')");
                return "booked";
            }
            catch (PostgresException ex)
            {
                return DbRuleViolation.From(ex)?.Constraint ?? ex.SqlState;
            }
        }));

        Assert.Equal(3, results.Count(r => r == "booked"));
        Assert.All(results.Where(r => r != "booked"), r => Assert.Equal("rule_room_full", r));

        await using var check = await db.OpenAsync();
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM appointment WHERE room_id = 15 AND start_time = '2026-12-10 09:00'", check);
        Assert.Equal(3L, await count.ExecuteScalarAsync());
    }

    private static async Task ExecAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
