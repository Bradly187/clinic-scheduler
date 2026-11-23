using Microsoft.EntityFrameworkCore;

namespace ClinicSchedulerV2.Data;

/// <summary>Who is acting: a login (staff, provider or patient), or a service acting as an employee.</summary>
public sealed record Actor(int? UserId, int? EmployeeId, int? PatientId, string Name)
{
    public static readonly Actor Nobody = new(null, null, null, "Not identified");

    public bool IsPatient => PatientId is not null;

    /// <summary>A service without a login (the MCP server, tests) acting as a staff member.</summary>
    public static Actor Employee(int employeeId, string name) => new(null, employeeId, null, name);
}

/// <summary>Supplies the current actor: the signed-in user in the web app, configuration elsewhere.</summary>
public interface IActorSource
{
    ValueTask<Actor> GetActorAsync();
}

public sealed class FixedActorSource(Actor actor) : IActorSource
{
    public Actor Actor { get; set; } = actor;
    public ValueTask<Actor> GetActorAsync() => ValueTask.FromResult(Actor);
}

/// <summary>
/// Opens database sessions that tell PostgreSQL who is acting, the equivalent of MySQL's
/// <c>SET @current_employee_id</c>. With a login the database derives the employee or patient
/// from <c>app.current_user_id</c>; a service without a login names its employee directly.
/// The audit and permission triggers read these settings.
/// </summary>
public sealed class ClinicDb(IDbContextFactory<ClinicDbContext> factory, IActorSource actors)
{
    public ValueTask<Actor> ActorAsync() => actors.GetActorAsync();

    /// <summary>
    /// Returns a context whose connection stays open, with the actor set for the session.
    /// Dispose it when the unit of work is done; the pooled connection is reset then.
    /// </summary>
    public async Task<ClinicDbContext> OpenAsync(CancellationToken ct = default)
    {
        var actor = await actors.GetActorAsync();
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            await db.Database.OpenConnectionAsync(ct);
            var user = actor.UserId?.ToString() ?? "";
            var employee = actor.UserId is null ? actor.EmployeeId?.ToString() ?? "" : "";
            await db.Database.ExecuteSqlAsync(
                $"SELECT set_config('app.current_user_id', {user}, false), set_config('app.current_employee_id', {employee}, false)", ct);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }
}
