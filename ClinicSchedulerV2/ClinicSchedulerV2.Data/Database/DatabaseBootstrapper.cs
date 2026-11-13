using Npgsql;

namespace ClinicSchedulerV2.Data.Database;

/// <summary>
/// Creates the database on first run by applying the SQL scripts. It only ever acts on a
/// database that does not exist yet or has no tables from this schema; it never alters or
/// drops an existing one.
/// </summary>
public static class DatabaseBootstrapper
{
    /// <returns>The scripts applied, or an empty list when the schema was already there.</returns>
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

        await using (var database = new NpgsqlConnection(connectionString))
        {
            await database.OpenAsync(ct);
            await using var hasSchema = new NpgsqlCommand("SELECT to_regclass('public.appointment') IS NOT NULL", database);
            if (await hasSchema.ExecuteScalarAsync(ct) is true) return [];
        }

        return await SqlScriptRunner.ApplyAllAsync(
            connectionString,
            include: name => includeSampleData || !name.Contains("sample_data", StringComparison.OrdinalIgnoreCase),
            ct: ct);
    }
}
