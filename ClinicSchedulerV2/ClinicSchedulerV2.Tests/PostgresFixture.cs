using ClinicSchedulerV2.Data.Database;
using Npgsql;

namespace ClinicSchedulerV2.Tests;

/// <summary>
/// Creates a throwaway database on a real PostgreSQL server, applies the db/postgres scripts,
/// and drops it afterwards. The rules under test are constraints and triggers, so they can
/// only be tested against the real engine.
/// Override the server with the CLINIC_V2_TEST_SERVER environment variable.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DefaultServer = "Host=localhost;Username=postgres;Password=postgres";

    private readonly string _server =
        Environment.GetEnvironmentVariable("CLINIC_V2_TEST_SERVER") ?? DefaultServer;

    public string DatabaseName { get; } = $"clinic_v2_test_{Guid.NewGuid():N}";

    public string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_server) { Database = DatabaseName, Pooling = false }.ConnectionString;

    public IReadOnlyList<string> AppliedScripts { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await ExecuteOnServerAsync($"CREATE DATABASE \"{DatabaseName}\"");
        AppliedScripts = await SqlScriptRunner.ApplyAllAsync(ConnectionString);
    }

    public Task DisposeAsync() =>
        ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)");

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        var admin = new NpgsqlConnectionStringBuilder(_server) { Database = "postgres", Pooling = false };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
