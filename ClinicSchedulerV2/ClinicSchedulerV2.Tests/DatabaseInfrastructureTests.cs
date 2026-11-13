using ClinicSchedulerV2.Data.Database;
using Npgsql;

namespace ClinicSchedulerV2.Tests;

public class DatabaseInfrastructureTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Scripts_apply_in_file_name_order_and_stop_at_the_first_failure()
    {
        var dir = Directory.CreateTempSubdirectory("clinic_v2_scripts").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "02_rows.sql"), "INSERT INTO runner_probe VALUES (1);");
            await File.WriteAllTextAsync(Path.Combine(dir, "01_table.sql"),
                "CREATE TABLE runner_probe (n int CONSTRAINT ck_probe_positive CHECK (n > 0));");
            await File.WriteAllTextAsync(Path.Combine(dir, "03_bad.sql"),
                "INSERT INTO runner_probe VALUES (2); SELECT nope;");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SqlScriptRunner.ApplyAllAsync(db.ConnectionString, dir));
            Assert.Contains("03_bad.sql", ex.Message);

            await using var connection = await db.OpenAsync();
            await using var count = new NpgsqlCommand("SELECT count(*) FROM runner_probe", connection);
            Assert.Equal(1L, await count.ExecuteScalarAsync()); // 01 and 02 committed; 03 rolled back whole
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Rejected_writes_are_reported_by_rule_kind_and_constraint_name()
    {
        await using var connection = await db.OpenAsync();
        await using (var setup = new NpgsqlCommand(
            "CREATE TEMP TABLE violation_probe (n int CONSTRAINT ck_violation_probe CHECK (n > 0))", connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        await using var insert = new NpgsqlCommand("INSERT INTO violation_probe VALUES (0)", connection);
        var check = DbRuleViolation.From(
            await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync()));
        Assert.Equal(DbRuleKind.Check, check?.Kind);
        Assert.Equal("ck_violation_probe", check?.Constraint);

        await using var raise = new NpgsqlCommand(
            "DO $$ BEGIN RAISE EXCEPTION 'Provider 4 is already booked at that time'; END $$", connection);
        var rule = DbRuleViolation.From(
            await Assert.ThrowsAsync<PostgresException>(() => raise.ExecuteNonQueryAsync()));
        Assert.Equal(DbRuleKind.BusinessRule, rule?.Kind);
        Assert.Equal("Provider 4 is already booked at that time", rule?.Message);

        Assert.Null(DbRuleViolation.From(new TimeoutException()));
    }
}
