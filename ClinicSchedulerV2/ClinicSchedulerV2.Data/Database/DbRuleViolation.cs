using Npgsql;

namespace ClinicSchedulerV2.Data.Database;

public enum DbRuleKind
{
    /// <summary>A trigger or procedure raised the error (a cross-row scheduling rule).</summary>
    BusinessRule,
    Check,
    Exclusion,
    Unique,
    ForeignKey,
    NotNull,
}

/// <summary>
/// A write the database rejected. The app does not re-implement the rules; it reports
/// which rule the database enforced, using the constraint name or the trigger's message.
/// </summary>
public sealed record DbRuleViolation(DbRuleKind Kind, string Message, string? Constraint, string? Table)
{
    /// <summary>
    /// Returns the violation if <paramref name="exception"/> (or an inner exception) is a
    /// PostgreSQL integrity or raised error; otherwise null, so real faults still surface.
    /// </summary>
    public static DbRuleViolation? From(Exception? exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is not PostgresException pg) continue;

            DbRuleKind? kind = pg.SqlState switch
            {
                PostgresErrorCodes.RaiseException => DbRuleKind.BusinessRule,
                PostgresErrorCodes.CheckViolation => DbRuleKind.Check,
                PostgresErrorCodes.ExclusionViolation => DbRuleKind.Exclusion,
                PostgresErrorCodes.UniqueViolation => DbRuleKind.Unique,
                PostgresErrorCodes.ForeignKeyViolation => DbRuleKind.ForeignKey,
                PostgresErrorCodes.NotNullViolation => DbRuleKind.NotNull,
                _ => null,
            };
            return kind is null
                ? null
                : new DbRuleViolation(kind.Value, pg.MessageText, pg.ConstraintName, pg.TableName);
        }
        return null;
    }
}
