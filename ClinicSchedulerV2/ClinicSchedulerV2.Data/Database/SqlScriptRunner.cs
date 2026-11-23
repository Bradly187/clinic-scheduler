using Npgsql;

namespace ClinicSchedulerV2.Data.Database;

/// <summary>
/// Applies the hand-written PostgreSQL scripts in <c>db/postgres</c> in file-name order.
/// The schema and its rules live in those scripts, not in EF migrations, so the database
/// enforces the same rules for every client that writes to it.
/// <para>
/// Each applied script is recorded in the <c>schema_script</c> table, in the same transaction
/// as the script itself, so a script runs exactly once per database and a later run applies
/// only the scripts added since.
/// </para>
/// </summary>
public static class SqlScriptRunner
{
    /// <summary>The first script; a database without its tables has no schema yet.</summary>
    public const string SchemaScript = "01_schema.sql";

    /// <summary>The scripts a database created before the journal existed already had.</summary>
    private static readonly string[] PreJournalScripts = ["01_schema.sql", "02_rules.sql"];
    private const string PreJournalSampleData = "03_sample_data.sql";

    /// <summary>Folder the scripts are copied to next to the built assembly.</summary>
    public static string DefaultScriptDirectory =>
        Path.Combine(AppContext.BaseDirectory, "db", "postgres");

    public static IReadOnlyList<string> FindScripts(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    public static bool IsSampleData(string scriptName) =>
        scriptName.Contains("sample_data", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs every script not yet recorded in the journal, in order, each in its own transaction,
    /// and returns the file names applied. A failing script rolls back and stops the run, naming
    /// the script in the exception.
    /// </summary>
    /// <param name="include">
    /// Optional filter, given the script name and the names already applied (including those
    /// applied earlier in this run).
    /// </param>
    public static async Task<IReadOnlyList<string>> ApplyAllAsync(
        string connectionString, string? directory = null,
        Func<string, IReadOnlySet<string>, bool>? include = null, CancellationToken ct = default)
    {
        var applied = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var done = await EnsureJournalAsync(connection, ct);

        foreach (var path in FindScripts(directory ?? DefaultScriptDirectory))
        {
            var name = Path.GetFileName(path);
            if (done.Contains(name)) continue;
            if (include is not null && !include(name, done)) continue;

            var sql = await File.ReadAllTextAsync(path, ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            try
            {
                if (!string.IsNullOrWhiteSpace(sql))
                {
                    await using var command = new NpgsqlCommand(sql, connection, transaction);
                    await command.ExecuteNonQueryAsync(ct);
                }
                await RecordAsync(connection, transaction, name, ct);
                await transaction.CommitAsync(ct);
            }
            catch (PostgresException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new InvalidOperationException(
                    $"Script '{name}' failed at position {ex.Position}: {ex.MessageText}", ex);
            }
            done.Add(name);
            applied.Add(name);
        }

        // The scripts create enum types. Npgsql cached this database's type list when the
        // connection opened, so refresh it or later connections cannot read those columns.
        if (applied.Count > 0) await connection.ReloadTypesAsync(ct);
        return applied;
    }

    /// <summary>The scripts recorded as applied, or an empty set when the journal does not exist.</summary>
    public static async Task<IReadOnlySet<string>> GetAppliedAsync(NpgsqlConnection connection, CancellationToken ct = default)
    {
        await using var exists = new NpgsqlCommand("SELECT to_regclass('public.schema_script') IS NOT NULL", connection);
        if (await exists.ExecuteScalarAsync(ct) is not true) return new HashSet<string>();
        return await ReadJournalAsync(connection, ct);
    }

    private static async Task<HashSet<string>> EnsureJournalAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using (var check = new NpgsqlCommand(
            "SELECT to_regclass('public.schema_script') IS NOT NULL, to_regclass('public.appointment') IS NOT NULL", connection))
        await using (var reader = await check.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            var hasJournal = reader.GetBoolean(0);
            var hasSchema = reader.GetBoolean(1);
            await reader.CloseAsync();
            if (hasJournal) return await ReadJournalAsync(connection, ct);

            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var create = new NpgsqlCommand(
                """
                CREATE TABLE schema_script (
                  name       varchar(200) NOT NULL,
                  applied_at timestamptz  NOT NULL DEFAULT now(),
                  CONSTRAINT pk_schema_script PRIMARY KEY (name)
                );
                COMMENT ON TABLE schema_script IS 'Scripts from db/postgres already applied to this database.';
                """, connection, transaction))
            {
                await create.ExecuteNonQueryAsync(ct);
            }

            // A database created before the journal: record what it already has, so only
            // scripts added since are applied to it.
            if (hasSchema)
            {
                foreach (var name in PreJournalScripts)
                    await RecordAsync(connection, transaction, name, ct);
                await using var seeded = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM location)", connection, transaction);
                if (await seeded.ExecuteScalarAsync(ct) is true)
                    await RecordAsync(connection, transaction, PreJournalSampleData, ct);
            }
            await transaction.CommitAsync(ct);
        }
        return await ReadJournalAsync(connection, ct);
    }

    private static async Task<HashSet<string>> ReadJournalAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("SELECT name FROM schema_script", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
        return names;
    }

    private static async Task RecordAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string name, CancellationToken ct)
    {
        await using var record = new NpgsqlCommand("INSERT INTO schema_script (name) VALUES (@name)", connection, transaction);
        record.Parameters.AddWithValue("name", name);
        await record.ExecuteNonQueryAsync(ct);
    }
}
