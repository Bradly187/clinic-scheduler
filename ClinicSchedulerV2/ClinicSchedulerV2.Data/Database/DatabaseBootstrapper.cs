using Npgsql;

namespace ClinicSchedulerV2.Data.Database;

/// <summary>
/// Brings a database up to date with the SQL scripts: creates it if it does not exist, then
/// applies every script the journal has not recorded yet. It never re-runs a script and never
/// drops anything.
/// </summary>
public static class DatabaseBootstrapper
{
    /// <param name="includeSampleData">
    /// Whether sample-data scripts may run. They only run on a database that received the base
    /// sample data, so they never reference rows that are not there.
    /// </param>
    /// <returns>The scripts applied in this run; empty when the database was already up to date.</returns>
    public static async Task<IReadOnlyList<string>> EnsureCreatedAsync(
        string connectionString, bool includeSampleData, CancellationToken ct = default)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = target.Database
            ?? throw new InvalidOperationException("The connection string must name a database.");

        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false };
        await using (var server = new NpgsqlConnection(admin.ConnectionString))
        {
            await server.OpenAsync(ct);
            await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", server);
            exists.Parameters.AddWithValue("name", databaseName);
            if (await exists.ExecuteScalarAsync(ct) is null)
            {
                // Identifiers cannot be parameters; quote the name
                var quoted = "\"" + databaseName.Replace("\"", "\"\"") + "\"";
                await using var create = new NpgsqlCommand($"CREATE DATABASE {quoted}", server);
                await create.ExecuteNonQueryAsync(ct);
            }
        }

        bool freshSchema;
        await using (var database = new NpgsqlConnection(connectionString))
        {
            await database.OpenAsync(ct);
            await using var hasSchema = new NpgsqlCommand("SELECT to_regclass('public.appointment') IS NULL", database);
            freshSchema = await hasSchema.ExecuteScalarAsync(ct) is true;
        }

        return await SqlScriptRunner.ApplyAllAsync(
            connectionString,
            include: (name, applied) => !SqlScriptRunner.IsSampleData(name) || (includeSampleData && (
                // The base sample data only goes into a brand-new database ...
                freshSchema || applied.Any(SqlScriptRunner.IsSampleData))),
            // ... and later sample-data scripts only where earlier sample data exists
            ct: ct);
    }
}
