using Microsoft.EntityFrameworkCore;

namespace ClinicSchedulerV2.Data;

/// <summary>
/// The employee the current user is working as. The design leaves logins out of scope, so the
/// app asks who is acting and passes that to the database for the audit triggers.
/// </summary>
public sealed class ActingUser
{
    public int? EmployeeId { get; private set; }
    public string? Name { get; private set; }
    public event Action? Changed;

    public void Set(int? employeeId, string? name)
    {
        EmployeeId = employeeId;
        Name = name;
        Changed?.Invoke();
    }
}

/// <summary>
/// Opens database sessions that identify the acting employee, the PostgreSQL equivalent of
/// MySQL's <c>SET @current_employee_id</c>. The audit triggers read the setting.
/// </summary>
public sealed class ClinicDb(IDbContextFactory<ClinicDbContext> factory, ActingUser actingUser)
{
    public int? ActingEmployeeId => actingUser.EmployeeId;

    /// <summary>
    /// Returns a context whose connection stays open, with app.current_employee_id set for the
    /// session. Dispose it when the unit of work is done; the pooled connection is reset then.
    /// </summary>
    public async Task<ClinicDbContext> OpenAsync(CancellationToken ct = default)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            await db.Database.OpenConnectionAsync(ct);
            var who = actingUser.EmployeeId?.ToString() ?? "";
            await db.Database.ExecuteSqlAsync($"SELECT set_config('app.current_employee_id', {who}, false)", ct);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }
}
