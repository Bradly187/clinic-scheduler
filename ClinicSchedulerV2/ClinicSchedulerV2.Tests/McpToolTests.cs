using ClinicScheduler.Mcp;
using ClinicSchedulerV2.Data;
using ClinicSchedulerV2.Data.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ClinicSchedulerV2.Tests;

/// <summary>
/// The MCP server's tools, called directly with the same services the server registers.
/// They commit real rows, so the class has its own database.
/// </summary>
public class McpToolTests : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly PostgresFixture _db;
    private readonly ServiceProvider _services;
    private readonly ClinicDb _clinic;
    private readonly SchedulingService _scheduling;
    private readonly DatabaseCatalog _catalog;

    public McpToolTests(PostgresFixture db)
    {
        _db = db;
        var services = new ServiceCollection();
        services.AddDbContextFactory<ClinicDbContext>(o => ClinicDbContext.Configure(o, db.ConnectionString));
        services.AddSingleton<IActorSource>(new FixedActorSource(Actor.Employee(10, "MCP server"))); // Kevin Brooks, scheduler
        services.AddScoped<ClinicDb>();
        services.AddScoped<SchedulingService>();
        services.AddScoped<DatabaseCatalog>();
        _services = services.BuildServiceProvider();
        _clinic = _services.GetRequiredService<ClinicDb>();
        _scheduling = _services.GetRequiredService<SchedulingService>();
        _catalog = _services.GetRequiredService<DatabaseCatalog>();
    }

    public void Dispose() => _services.Dispose();

    /// <summary>A weekday at least two weeks out, clear of the sample appointments' times used below.</summary>
    private async Task<string> FutureWeekdayAsync(int isoDay)
    {
        await using var connection = await _db.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT to_char(d, 'YYYY-MM-DD') FROM generate_series(clinic_now()::date + 14, clinic_now()::date + 20, INTERVAL '1 day') d WHERE EXTRACT(ISODOW FROM d) = {isoDay}",
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Startup_check_accepts_a_staff_member_and_refuses_anyone_else()
    {
        await StartupCheck.RunAsync(_services, 10);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => StartupCheck.RunAsync(_services, 1)); // a provider
        Assert.Contains("is not an active staff member", ex.Message);
    }

    [Fact]
    public async Task Listing_tools_describe_providers_services_and_the_request_queue()
    {
        var providers = await ClinicTools.ListProviders(_clinic, default);
        Assert.Contains("- Maria Gonzalez, MD — works at Medical Center, Memorial City — qualified for New-patient evaluation, Follow-up visit, Joint injection", providers);

        var services = await ClinicTools.ListServices(_clinic, default);
        Assert.Contains("- IV biologic infusion: 120 minutes, infusion room, $650.00", services);
        Assert.Contains("Locations: Medical Center, Memorial City, Clear Lake", services);

        var queue = await ClinicTools.ListPendingRequests(_catalog, default);
        Assert.StartsWith("Waiting for review (oldest first):\n- Appointment request: David Ramirez, Follow-up visit", queue);
    }

    [Fact]
    public async Task Scheduling_books_through_the_database_and_returns_its_message_when_it_refuses()
    {
        var monday = await FutureWeekdayAsync(1);

        // Infusion at Clear Lake: the database picks the infusion suite, the only room of that type there
        var booked = await ClinicTools.ScheduleAppointment(_clinic, _scheduling, _catalog,
            "Susan Brooks", "Luis Herrera", "infusion", "Clear Lake", monday, "15:00", null, default);
        Assert.Matches(@"^Booked\. ID \d+: Susan Brooks, IV biologic infusion, .* 3:00 PM \(clinic time\) to 5:00 PM, with Luis Herrera, RN, Infusion Suite at Clear Lake\.$", booked);

        // The same provider again at the same time, elsewhere: a trigger refuses it
        var clash = await ClinicTools.ScheduleAppointment(_clinic, _scheduling, _catalog,
            "Karen Wright", "Herrera", "infusion", "Medical Center", monday, "15:30", 60, default);
        Assert.StartsWith("Could not book: Provider 6 is already booked at that time", clash);

        // Out of hours: a CHECK constraint refuses it, explained in the words stored on the constraint
        var late = await ClinicTools.ScheduleAppointment(_clinic, _scheduling, _catalog,
            "Karen Wright", "Herrera", "infusion", "Clear Lake", monday, "16:00", null, default);
        Assert.Equal("Could not book: Appointments must fall between 8:00 AM and 5:00 PM.", late);

        // Ambiguous and unknown names are resolved before anything is written
        // "ma" is in Maria Santos and Thomas Green
        Assert.StartsWith("Several patients match 'ma'", await ClinicTools.ScheduleAppointment(_clinic, _scheduling, _catalog,
            "ma", "Gonzalez", "Follow-up", "Medical Center", monday, "10:00", null, default));
        Assert.Equal("Error: No provider found matching 'Dr. Who'.", await ClinicTools.ScheduleAppointment(_clinic, _scheduling, _catalog,
            "Linda Carter", "Dr. Who", "Follow-up", "Medical Center", monday, "10:00", null, default));
    }

    [Fact]
    public async Task Cancelling_previews_first_and_cancels_only_when_confirmed()
    {
        var tuesday = await FutureWeekdayAsync(2);
        var booked = await ClinicTools.ScheduleAppointment(_clinic, _scheduling, _catalog,
            "Patricia Nguyen", "Okafor", "Follow-up", "Memorial City", tuesday, "14:00", null, default);
        var id = int.Parse(booked.Split("ID ")[1].Split(':')[0]);

        var upcoming = await ClinicTools.GetAppointments(_clinic, "patricia", default);
        Assert.Contains($"- ID {id}: Patricia Nguyen, Follow-up visit", upcoming);

        var preview = await ClinicTools.CancelAppointment(_clinic, _scheduling, _catalog, id, confirmed: false, reason: null, default);
        Assert.StartsWith($"CONFIRMATION REQUIRED: You are about to cancel appointment {id}, Patricia Nguyen's follow-up visit", preview);
        await using (var db = await _clinic.OpenAsync())
            Assert.Equal(Data.Model.AppointmentStatus.Scheduled, (await db.Appointments.SingleAsync(a => a.AppointmentId == id)).Status);

        var done = await ClinicTools.CancelAppointment(_clinic, _scheduling, _catalog, id, confirmed: true, reason: "Provider unavailable", default);
        Assert.StartsWith($"Canceled appointment {id}", done);
        Assert.Equal($"Appointment {id} is Canceled; only a scheduled appointment can be canceled.",
            await ClinicTools.CancelAppointment(_clinic, _scheduling, _catalog, id, confirmed: true, reason: null, default));

        // The audit log names the staff member the server acts as
        await using var check = await _clinic.OpenAsync();
        Assert.True(await check.AuditLog.AnyAsync(a => a.TableName == "Appointment" && a.RecordId == id && a.ChangedBy == 10));
    }
}
