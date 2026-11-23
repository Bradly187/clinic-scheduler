using ClinicSchedulerV2.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace ClinicSchedulerV2.Tests;

/// <summary>
/// The schema is hand-written SQL, so nothing generates the EF model from it or the other way
/// round. This compares the two directly: every mapped property must hit a real column with a
/// compatible type and nullability, and every column the app cannot fill must have a default.
/// </summary>
public class SchemaMappingTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private sealed record Column(string Table, string Name, string DataType, string UdtName, bool Nullable, bool HasDefault);

    [Fact]
    public async Task Every_mapped_property_matches_a_column_in_the_schema()
    {
        var columns = await ReadColumnsAsync();
        var problems = new List<string>();

        await using var context = CreateContext();
        foreach (var entity in context.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null) continue;
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());

            if (!columns.Any(c => c.Table == table))
            {
                problems.Add($"{entity.ClrType.Name}: table '{table}' does not exist");
                continue;
            }

            foreach (var property in entity.GetProperties())
            {
                var name = property.GetColumnName(store);
                if (name is null) continue;
                var column = columns.SingleOrDefault(c => c.Table == table && c.Name == name);
                if (column is null)
                {
                    problems.Add($"{entity.ClrType.Name}.{property.Name}: column {table}.{name} does not exist");
                    continue;
                }

                // A non-nullable C# property over a nullable column throws on read when the value is NULL
                if (!property.IsNullable && column.Nullable)
                    problems.Add($"{entity.ClrType.Name}.{property.Name}: required in C# but {table}.{name} allows NULL");

                var expected = ExpectedUdt(property.ClrType);
                if (expected is not null && expected != column.UdtName)
                    problems.Add($"{entity.ClrType.Name}.{property.Name}: C# {property.ClrType.Name} expects {expected}, column is {column.UdtName}");
            }
        }

        Assert.True(problems.Count == 0, "EF model and schema disagree:\n" + string.Join("\n", problems));
    }

    [Fact]
    public async Task Every_column_is_mapped_or_filled_by_the_database()
    {
        var columns = await ReadColumnsAsync();
        await using var context = CreateContext();

        var mapped = new HashSet<(string, string)>();
        foreach (var entity in context.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is not { } table) continue;
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            foreach (var property in entity.GetProperties())
                if (property.GetColumnName(store) is { } name) mapped.Add((table, name));
        }

        // A NOT NULL column without a default that EF never writes makes every insert fail
        var unfillable = columns
            .Where(c => !mapped.Contains((c.Table, c.Name)) && !c.Nullable && !c.HasDefault)
            .Select(c => $"{c.Table}.{c.Name}")
            .ToList();
        Assert.True(unfillable.Count == 0, "Required columns the app cannot write: " + string.Join(", ", unfillable));

        // Every table in the schema is reachable from the model
        var tables = columns.Select(c => c.Table).Distinct().Order().ToList();
        var mappedTables = mapped.Select(m => m.Item1).Distinct().ToHashSet();
        Assert.Equal([], tables.Where(t => !mappedTables.Contains(t)));
        Assert.Equal(17, tables.Count);
    }

    [Fact]
    public async Task Every_routine_the_app_calls_exists_with_the_arguments_it_passes()
    {
        await using var connection = await db.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ') -> ' || pg_get_function_result(p.oid)
            FROM pg_proc p
            WHERE p.pronamespace = 'public'::regnamespace
              AND p.proname IN ('sp_approve_request', 'sp_deny_request', 'sp_reschedule_missed', 'clinic_now', 'current_employee_id')
            ORDER BY 1
            """, connection);
        var signatures = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) signatures.Add(reader.GetString(0));

        Assert.Equal(
            [
                "clinic_now() -> timestamp without time zone",
                "current_employee_id() -> integer",
                "sp_approve_request(p_request_id integer, p_staff_id integer, p_provider integer, p_room integer, p_start timestamp without time zone) -> integer",
                "sp_deny_request(p_request_id integer, p_staff_id integer, p_note character varying) -> void",
                "sp_reschedule_missed(p_appointment_id integer, p_staff_id integer) -> integer",
            ],
            signatures);
    }

    [Fact]
    public async Task Queries_the_pages_run_on_load_match_the_views_and_functions()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions
            .AddDbContextFactory<ClinicDbContext>(services, o => ClinicDbContext.Configure(o, db.ConnectionString));
        await using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
            .BuildServiceProvider(services);
        var catalog = new ClinicSchedulerV2.Data.Database.DatabaseCatalog(new ClinicDb(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<IDbContextFactory<ClinicDbContext>>(provider),
            new ActingUser()));

        // Every page reads the clinic's date from clinic_now()
        var today = await catalog.ClinicTodayAsync();
        Assert.InRange(today, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));

        // The overview page's queue reads vw_pending_requests by column name
        var queue = await catalog.PendingQueueAsync();
        Assert.Equal(["Kind", "Patient", "Service", "For", "Details", "Submitted"], queue.Columns);
        Assert.Equal(5, queue.Rows.Count(r => r[0] == "Appointment request"));
        Assert.Equal(3, queue.Rows.Count(r => r[0] == "Cancellation request"));
    }

    private ClinicDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ClinicDbContext>();
        ClinicDbContext.Configure(options, db.ConnectionString);
        return new ClinicDbContext(options.Options);
    }

    private async Task<List<Column>> ReadColumnsAsync()
    {
        await using var connection = await db.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT c.table_name, c.column_name, c.data_type, c.udt_name, c.is_nullable = 'YES',
                   c.column_default IS NOT NULL OR c.is_identity = 'YES'
            FROM information_schema.columns c
            JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE'
            """, connection);
        var columns = new List<Column>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(new Column(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetBoolean(4), reader.GetBoolean(5)));
        return columns;
    }

    /// <summary>The PostgreSQL type a CLR type must land on; null for enums, checked by name instead.</summary>
    private static string? ExpectedUdt(Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (type.IsEnum)
            return type.Name switch
            {
                "RoomType" => "room_type",
                "ProviderCredential" => "provider_credential",
                "DrugClass" => "drug_class",
                "PlanStatus" => "plan_status",
                "AppointmentStatus" => "appointment_status",
                "DoseUnit" => "dose_unit",
                "AdminRoute" => "admin_route",
                "AdminReason" => "admin_reason",
                "RequestStatus" => "request_status",
                "CancelSource" => "cancel_source",
                "AuditAction" => "audit_action",
                _ => "unknown enum " + type.Name,
            };
        return type switch
        {
            _ when type == typeof(int) => "int4",
            _ when type == typeof(long) => "int8",
            _ when type == typeof(short) => "int2",
            _ when type == typeof(bool) => "bool",
            _ when type == typeof(decimal) => "numeric",
            _ when type == typeof(DateOnly) => "date",
            _ when type == typeof(DateTime) => "timestamp",
            _ => null, // strings map to varchar, bpchar, text or jsonb; checked by existence and nullability only
        };
    }
}
