using Npgsql;

namespace ClinicSchedulerV2.Data.Database;

/// <summary>
/// Applies the hand-written PostgreSQL scripts in <c>db/postgres</c> in file-name order.
/// The schema and its rules live in those scripts, not in EF migrations, so the database
/// enforces the same rules for every client that writes to it.
/// </summary>
public static class SqlScriptRunner
{
    /// <summary>Folder the scripts are copied to next to the built assembly.</summary>
    public static string DefaultScriptDirectory =>
        Path.Combine(AppContext.BaseDirectory, "db", "postgres");

    public static IReadOnlyList<string> FindScripts(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    /// <summary>
    /// Runs every script in order, each in its own transaction, and returns the file names applied.
    /// A failing script rolls back and stops the run, naming the script in the exception.
    /// </summary>
    /// <param name="include">Optional filter on the file name, e.g. to leave out the sample data.</param>
    public static async Task<IReadOnlyList<string>> ApplyAllAsync(
        string connectionString, string? directory = null, Func<string, bool>? include = null,
        CancellationToken ct = default)
    {
        var applied = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        foreach (var path in FindScripts(directory ?? DefaultScriptDirectory))
        {
            var name = Path.GetFileName(path);
            if (include is not null && !include(name)) continue;

            var sql = await File.ReadAllTextAsync(path, ct);
            if (string.IsNullOrWhiteSpace(sql)) continue;

            await using var transaction = await connection.BeginTransactionAsync(ct);
            try
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                await command.ExecuteNonQueryAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (PostgresException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new InvalidOperationException(
                    $"Script '{name}' failed at position {ex.Position}: {ex.MessageText}", ex);
            }
            applied.Add(name);
        }

        // The scripts create enum types. Npgsql cached this database's type list when the
        // connection opened, so refresh it or later connections cannot read those columns.
        if (applied.Count > 0) await connection.ReloadTypesAsync(ct);
        return applied;
    }
}
