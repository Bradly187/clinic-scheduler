using ClinicSchedulerV2.Data.Database;
using Microsoft.EntityFrameworkCore;

namespace ClinicSchedulerV2.Data;

/// <summary>The outcome of a write: either it was stored, or the database rejected it.</summary>
public readonly record struct WriteResult(DbRuleViolation? Violation, int? Id = null)
{
    public bool Succeeded => Violation is null;
}

/// <summary>
/// Base for the services that write. None of them validates anything itself: each sends the
/// change to PostgreSQL as the current actor and reports what the constraints, triggers and
/// routines decided.
/// </summary>
public abstract class DbService(ClinicDb clinic)
{
    protected ClinicDb Clinic { get; } = clinic;

    /// <summary>Saves tracked changes on a context from <see cref="ClinicDb.OpenAsync"/> and reports a rejection.</summary>
    public async Task<WriteResult> SaveAsync(ClinicDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return new WriteResult(null);
        }
        catch (Exception ex) when (DbRuleViolation.From(ex) is { } violation)
        {
            return new WriteResult(violation);
        }
    }

    protected async Task<WriteResult> WriteAsync(Func<ClinicDbContext, Actor, Task<int>> write)
    {
        try
        {
            var actor = await Clinic.ActorAsync();
            await using var db = await Clinic.OpenAsync();
            return new WriteResult(null, await write(db, actor));
        }
        catch (Exception ex) when (DbRuleViolation.From(ex) is { } violation)
        {
            return new WriteResult(violation);
        }
    }

    /// <summary>Runs one SQL statement with named parameters and returns its single integer result.</summary>
    protected static async Task<int> ScalarAsync(
        ClinicDbContext db, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value switch
            {
                null => DBNull.Value,
                DateTime d => DateTime.SpecifyKind(d, DateTimeKind.Unspecified), // clinic wall-clock time
                _ => value,
            };
            command.Parameters.Add(parameter);
        }
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }
}
