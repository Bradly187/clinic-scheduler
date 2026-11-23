using ClinicSchedulerV2.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicScheduler.Mcp;

/// <summary>Fails fast, before any agent connects, when the database or the acting employee is wrong.</summary>
public static class StartupCheck
{
    public static async Task RunAsync(IServiceProvider services, int actingEmployeeId)
    {
        await using var scope = services.CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<ClinicDb>().OpenAsync();

        var ready = await db.Database.SqlQueryRaw<bool>(
            "SELECT to_regproc('public.sp_book_appointment') IS NOT NULL AS \"Value\"").SingleAsync();
        if (!ready)
            throw new InvalidOperationException(
                "The database is not a ClinicSchedulerV2 database, or is missing scripts up to 06_care.sql. " +
                "Run the ClinicSchedulerV2 web app once with Database:AutoCreate=true to bring it up to date.");

        var staff = await db.Staff.Include(s => s.Employee).SingleOrDefaultAsync(s => s.EmployeeId == actingEmployeeId);
        if (staff is null || !staff.Employee.IsActive)
            throw new InvalidOperationException(
                $"Mcp:ActingEmployeeId {actingEmployeeId} is not an active staff member; bookings must be made by staff.");
    }
}
