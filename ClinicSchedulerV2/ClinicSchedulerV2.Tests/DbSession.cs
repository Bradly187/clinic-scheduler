using ClinicSchedulerV2.Data.Database;
using Npgsql;

namespace ClinicSchedulerV2.Tests;

/// <summary>
/// One connection inside a transaction that is rolled back on dispose, so every test starts
/// from the untouched sample data (the MySQL test script did the same with one big ROLLBACK).
/// </summary>
public sealed class DbSession : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private int _savepoint;

    private DbSession(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    /// <param name="actingEmployeeId">Who the audit triggers should record; null leaves it unset.</param>
    public static async Task<DbSession> BeginAsync(PostgresFixture db, int? actingEmployeeId = 13)
    {
        var connection = await db.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        var session = new DbSession(connection, transaction);
        if (actingEmployeeId is { } id)
            await session.ExecAsync($"SELECT set_config('app.current_employee_id', '{id}', false)");
        return session;
    }

    public async Task<int> ExecAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection, _transaction);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection, _transaction);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default! : (T)value;
    }

    /// <summary>Returns each row as its columns rendered to text ("" for NULL).</summary>
    public async Task<List<string[]>> RowsAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, _connection, _transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
        {
            var row = new string[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
                row[i] = reader.IsDBNull(i) ? "" : ReadText(reader, i);
            rows.Add(row);
        }
        return rows;
    }

    private static string ReadText(NpgsqlDataReader reader, int i)
    {
        try
        {
            return Convert.ToString(reader.GetValue(i)) ?? "";
        }
        catch (InvalidCastException)
        {
            // The database's enum types are not mapped to CLR types here; their labels read as text
            return reader.GetString(i);
        }
    }

    /// <summary>
    /// Runs a statement the database is expected to reject and returns the violation.
    /// PostgreSQL aborts a transaction on error, so the statement runs inside a savepoint
    /// and the session stays usable afterwards.
    /// </summary>
    public async Task<DbRuleViolation> RejectedAsync(string sql)
    {
        var savepoint = $"sp_{++_savepoint}";
        await _transaction.SaveAsync(savepoint);
        try
        {
            await ExecAsync(sql);
        }
        catch (PostgresException ex)
        {
            await _transaction.RollbackAsync(savepoint);
            return DbRuleViolation.From(ex)
                   ?? throw new Xunit.Sdk.XunitException($"Failed with a non-rule error {ex.SqlState}: {ex.MessageText}");
        }
        throw new Xunit.Sdk.XunitException("Expected the database to reject the statement, but it succeeded: " + sql);
    }

    public async ValueTask DisposeAsync()
    {
        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
