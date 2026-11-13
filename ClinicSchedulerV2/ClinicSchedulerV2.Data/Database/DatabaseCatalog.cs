using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicSchedulerV2.Data.Database;

/// <summary>A query result as text, for screens that show whatever the database returns.</summary>
public sealed record QueryResult(IReadOnlyList<string> Columns, IReadOnlyList<string[]> Rows);

public sealed record ReportDefinition(string Key, string Title, string Question, string Sql);

/// <summary>
/// Reads things the database already knows about itself: its rules (constraints, triggers,
/// routines and their descriptions) and the report queries shipped beside the schema.
/// </summary>
public sealed class DatabaseCatalog(ClinicDb clinic)
{
    private static readonly Lazy<IReadOnlyList<ReportDefinition>> ReportFiles = new(LoadReports);
    private Dictionary<string, string>? _descriptions;

    public IReadOnlyList<ReportDefinition> Reports => ReportFiles.Value;

    /// <summary>
    /// The plain-language description stored with a constraint (COMMENT ON CONSTRAINT / INDEX),
    /// or null when the database has none.
    /// </summary>
    public async Task<string?> DescribeAsync(string? constraintName)
    {
        if (string.IsNullOrEmpty(constraintName)) return null;
        if (_descriptions is null)
        {
            var result = await QueryAsync(
                """
                SELECT c.conname, obj_description(c.oid, 'pg_constraint')
                FROM pg_constraint c
                WHERE c.connamespace = 'public'::regnamespace AND obj_description(c.oid, 'pg_constraint') IS NOT NULL
                UNION ALL
                SELECT i.relname, obj_description(i.oid, 'pg_class')
                FROM pg_class i
                WHERE i.relnamespace = 'public'::regnamespace AND i.relkind = 'i' AND obj_description(i.oid, 'pg_class') IS NOT NULL
                """);
            _descriptions = result.Rows.GroupBy(r => r[0]).ToDictionary(g => g.Key, g => g.First()[1]);
        }
        return _descriptions.GetValueOrDefault(constraintName);
    }

    public Task<QueryResult> ConstraintsAsync() => QueryAsync(
        """
        SELECT t.relname AS "Table",
               c.conname AS "Rule",
               CASE c.contype WHEN 'c' THEN 'CHECK' WHEN 'u' THEN 'UNIQUE' WHEN 'x' THEN 'EXCLUDE'
                              WHEN 'f' THEN 'FOREIGN KEY' WHEN 'p' THEN 'PRIMARY KEY' END AS "Kind",
               COALESCE(obj_description(c.oid, 'pg_constraint'), '') AS "What it means",
               pg_get_constraintdef(c.oid) AS "Definition"
        FROM pg_constraint c
        JOIN pg_class t ON t.oid = c.conrelid
        WHERE c.connamespace = 'public'::regnamespace AND c.contype IN ('c', 'u', 'x', 'f', 'p')
        UNION ALL
        SELECT t.relname, i.relname, 'UNIQUE (partial index)',
               COALESCE(obj_description(i.oid, 'pg_class'), ''), pg_get_indexdef(i.oid)
        FROM pg_index x
        JOIN pg_class i ON i.oid = x.indexrelid
        JOIN pg_class t ON t.oid = x.indrelid
        WHERE t.relnamespace = 'public'::regnamespace AND x.indisunique AND x.indpred IS NOT NULL
        ORDER BY 1, 3, 2
        """);

    public Task<QueryResult> TriggersAsync() => QueryAsync(
        """
        SELECT t.relname AS "Table",
               g.tgname  AS "Trigger",
               CASE WHEN g.tgtype & 2 = 2 THEN 'BEFORE' ELSE 'AFTER' END || ' ' ||
               concat_ws(' OR ',
                 CASE WHEN g.tgtype & 4  = 4  THEN 'INSERT' END,
                 CASE WHEN g.tgtype & 16 = 16 THEN 'UPDATE' END,
                 CASE WHEN g.tgtype & 8  = 8  THEN 'DELETE' END) AS "Fires",
               p.proname AS "Runs"
        FROM pg_trigger g
        JOIN pg_class t ON t.oid = g.tgrelid
        JOIN pg_proc p  ON p.oid = g.tgfoid
        WHERE NOT g.tgisinternal AND t.relnamespace = 'public'::regnamespace
        ORDER BY 1, 2
        """);

    public Task<QueryResult> RoutinesAsync() => QueryAsync(
        """
        SELECT p.proname AS "Routine",
               pg_get_function_identity_arguments(p.oid) AS "Arguments",
               pg_get_function_result(p.oid) AS "Returns"
        FROM pg_proc p
        WHERE p.pronamespace = 'public'::regnamespace AND p.proname LIKE 'sp\_%'
        ORDER BY 1
        """);

    public Task<QueryResult> RunReportAsync(ReportDefinition report, DateOnly today) =>
        QueryAsync(report.Sql, command =>
        {
            if (!report.Sql.Contains("@today", StringComparison.Ordinal)) return;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "today";
            parameter.Value = today;
            command.Parameters.Add(parameter);
        });

    /// <summary>The staff work queue, straight from the vw_pending_requests view, oldest first.</summary>
    public Task<QueryResult> PendingQueueAsync() => QueryAsync(
        """
        SELECT request_type AS "Kind", patient AS "Patient", service AS "Service",
               requested_time AS "For", COALESCE(details, '') AS "Details",
               to_char(submitted_at, 'Mon DD, HH24:MI') AS "Submitted"
        FROM vw_pending_requests
        ORDER BY submitted_at
        """);

    public async Task<DateOnly> ClinicTodayAsync()
    {
        var result = await QueryAsync("SELECT to_char(clinic_now(), 'YYYY-MM-DD')");
        return DateOnly.ParseExact(result.Rows[0][0], "yyyy-MM-dd");
    }

    private async Task<QueryResult> QueryAsync(string sql, Action<DbCommand>? configure = null)
    {
        await using var db = await clinic.OpenAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        configure?.Invoke(command);

        await using var reader = await command.ExecuteReaderAsync();
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
        {
            var row = new string[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
                row[i] = reader.IsDBNull(i) ? "" : Format(reader.GetValue(i));
            rows.Add(row);
        }
        return new QueryResult(columns, rows);
    }

    private static string Format(object value) => value switch
    {
        DateTime d => d.ToString("yyyy-MM-dd HH:mm"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static IReadOnlyList<ReportDefinition> LoadReports()
    {
        var directory = Path.Combine(SqlScriptRunner.DefaultScriptDirectory, "reports");
        var reports = new List<ReportDefinition>();
        foreach (var path in SqlScriptRunner.FindScripts(directory))
        {
            var lines = File.ReadAllLines(path);
            string Header(string key) =>
                lines.FirstOrDefault(l => l.StartsWith($"-- {key}:", StringComparison.OrdinalIgnoreCase))
                    ?[(key.Length + 4)..].Trim() ?? "";
            var sql = string.Join('\n', lines.Where(l => !l.StartsWith("-- ", StringComparison.Ordinal))).Trim().TrimEnd(';');
            reports.Add(new ReportDefinition(Path.GetFileNameWithoutExtension(path), Header("title"), Header("question"), sql));
        }
        return reports;
    }
}
