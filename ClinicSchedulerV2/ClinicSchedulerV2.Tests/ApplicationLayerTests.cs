using ClinicSchedulerV2.Data;
using ClinicSchedulerV2.Data.Database;
using ClinicSchedulerV2.Data.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ClinicSchedulerV2.Tests;

/// <summary>
/// The C# layer the pages use: the EF mapping onto the hand-written schema, and the service
/// that sends writes to the database and reports its verdict. These tests commit real rows,
/// so the class has its own database.
/// </summary>
public class ApplicationLayerTests : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly ServiceProvider _services;
    private readonly ClinicDb _clinic;
    private readonly SchedulingService _scheduling;
    private readonly DatabaseCatalog _catalog;

    public ApplicationLayerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        var services = new ServiceCollection();
        services.AddDbContextFactory<ClinicDbContext>(o => ClinicDbContext.Configure(o, fixture.ConnectionString));
        services.AddScoped<ActingUser>();
        services.AddScoped<ClinicDb>();
        services.AddScoped<SchedulingService>();
        services.AddScoped<DatabaseCatalog>();
        _services = services.BuildServiceProvider();

        _services.GetRequiredService<ActingUser>().Set(13, "Monica Patel");
        _clinic = _services.GetRequiredService<ClinicDb>();
        _scheduling = _services.GetRequiredService<SchedulingService>();
        _catalog = _services.GetRequiredService<DatabaseCatalog>();
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Every_table_maps_and_reads_back_the_sample_data()
    {
        await using var db = await _clinic.OpenAsync();

        Assert.Equal(3, await db.Locations.CountAsync());
        Assert.Equal(15, await db.Rooms.CountAsync());
        Assert.Equal(13, await db.Employees.CountAsync());
        Assert.Equal(12, await db.Patients.CountAsync());
        Assert.Equal(5, await db.ServiceTypes.CountAsync());
        Assert.Equal(10, await db.Medications.CountAsync());
        Assert.Equal(10, await db.TreatmentPlans.CountAsync());
        Assert.Equal(18, await db.TreatmentPlanItems.CountAsync());
        Assert.Equal(20, await db.MedicationAdministrations.CountAsync());
        Assert.True(await db.Appointments.CountAsync() >= 40);
        Assert.True(await db.AppointmentRequests.CountAsync() >= 11);
        Assert.True(await db.CancellationRequests.CountAsync() >= 10);

        // Subtypes, both many-to-many junctions, and enum columns
        var gonzalez = await db.Providers
            .Include(p => p.Employee).ThenInclude(e => e.Locations)
            .Include(p => p.Services)
            .SingleAsync(p => p.EmployeeId == 1);
        Assert.Equal("Maria Gonzalez, MD", gonzalez.DisplayName);
        Assert.Equal(ProviderCredential.MD, gonzalez.Credential);
        Assert.Equal(2, gonzalez.Employee.Locations.Count);
        Assert.Equal(3, gonzalez.Services.Count);
        Assert.Equal("Scheduler", (await db.Staff.SingleAsync(s => s.EmployeeId == 13)).JobTitle);

        var suite = await db.Rooms.Include(r => r.Location).SingleAsync(r => r.RoomId == 5);
        Assert.Equal((RoomType.Infusion, (short)3, "Medical Center"), (suite.RoomType, suite.Capacity, suite.Location.ShortName));

        // Enum labels that are not C# identifiers
        var injection = await db.MedicationAdministrations.SingleAsync(m => m.AdministrationId == 15);
        Assert.Equal((AdminRoute.IntraArticular, DoseUnit.Mg, "Right knee"), (injection.Route, injection.DoseUnit, injection.Site));
        Assert.Equal(2, await db.MedicationAdministrations.CountAsync(m => m.Reason == AdminReason.AsNeeded));
        Assert.Equal(5, await db.MedicationAdministrations.CountAsync(m => m.DoseUnit == DoseUnit.MgPerKg) - 2);

        var plan = await db.TreatmentPlans
            .Include(p => p.Patient).Include(p => p.OrderingProvider).ThenInclude(p => p.Employee)
            .Include(p => p.Items).ThenInclude(i => i.Medication)
            .SingleAsync(p => p.PlanId == 1);
        Assert.Equal(("Linda Carter", 3, PlanStatus.Active), (plan.Patient.FullName, plan.Items.Count, plan.Status));

        var visit = await db.Appointments
            .Include(a => a.Patient).Include(a => a.Provider).ThenInclude(p => p.Employee)
            .Include(a => a.Room).ThenInclude(r => r.Location).Include(a => a.ServiceType)
            .SingleAsync(a => a.AppointmentId == 2);
        Assert.Equal(new DateTime(2026, 10, 5, 9, 0, 0), visit.StartTime);
        Assert.Equal(DateTimeKind.Unspecified, visit.StartTime.Kind); // clinic wall-clock time, not UTC
        Assert.Equal(AppointmentStatus.Completed, visit.Status);
    }

    [Fact]
    public async Task Booking_goes_through_the_database_rules_and_is_audited_as_the_acting_employee()
    {
        // Dr. Gonzalez is already booked on Nov 23 at 11:00 (appointment 29)
        var rejected = await _scheduling.BookAsync(new BookingRequest(
            3, 1, 2, 2, null, new DateTime(2026, 11, 23, 11, 0, 0), new DateTime(2026, 11, 23, 11, 30, 0), null));
        Assert.False(rejected.Succeeded);
        Assert.Equal("rule_provider_double_booked", rejected.Violation!.Constraint);

        var saturday = await _scheduling.BookAsync(new BookingRequest(
            3, 1, 2, 2, null, new DateTime(2026, 12, 5, 9, 0, 0), new DateTime(2026, 12, 5, 9, 30, 0), null));
        Assert.Equal(DbRuleKind.Check, saturday.Violation!.Kind);
        Assert.Equal("The clinic is open Monday to Friday only.", await _catalog.DescribeAsync(saturday.Violation.Constraint));

        var booked = await _scheduling.BookAsync(new BookingRequest(
            3, 1, 2, 2, null, new DateTime(2026, 12, 15, 9, 0, 0), new DateTime(2026, 12, 15, 9, 30, 0), "  Follow-up  "));
        Assert.True(booked.Succeeded);

        await using var db = await _clinic.OpenAsync();
        var saved = await db.Appointments.SingleAsync(a => a.AppointmentId == booked.Id);
        Assert.Equal((13, AppointmentStatus.Scheduled, "Follow-up"), (saved.CreatedBy, saved.Status, saved.Notes));
        Assert.True(saved.CreatedAt > new DateTime(2026, 1, 1)); // stamped by the database

        var audit = await db.AuditLog.Include(a => a.ChangedByEmployee)
            .SingleAsync(a => a.TableName == "Appointment" && a.RecordId == booked.Id);
        Assert.Equal((AuditAction.INSERT, "Monica Patel"), (audit.Action, audit.ChangedByEmployee!.FullName));
        Assert.Contains("\"status\": \"Scheduled\"", audit.NewValues);

        // Moving it onto a taken slot is rejected; a status change is accepted
        var moved = await _scheduling.MoveAsync(booked.Id!.Value, 2,
            new DateTime(2026, 11, 23, 11, 0, 0), new DateTime(2026, 11, 23, 11, 30, 0));
        Assert.Equal("rule_provider_double_booked", moved.Violation!.Constraint);
        Assert.True((await _scheduling.SetStatusAsync(booked.Id.Value, AppointmentStatus.Completed)).Succeeded);
    }

    [Fact]
    public async Task Requests_and_cancellations_run_through_the_database_routines_and_triggers()
    {
        // Approve request 9 (Karen Wright, follow-up) with Dr. Nair in Clear Lake Exam 2
        var approved = await _scheduling.ApproveRequestAsync(9, 3, 12, new DateTime(2026, 12, 10, 14, 0, 0));
        Assert.True(approved.Succeeded, approved.Violation?.Message);
        var again = await _scheduling.ApproveRequestAsync(9, 3, 12, new DateTime(2026, 12, 10, 15, 0, 0));
        Assert.Equal("Only a Pending request can be approved", again.Violation!.Message);

        Assert.True((await _scheduling.DenyRequestAsync(10, "No openings that week")).Succeeded);

        var submitted = await _scheduling.SubmitAppointmentRequestAsync(8, 2, 3, new DateTime(2026, 12, 16, 10, 0, 0), null);
        Assert.True(submitted.Succeeded);

        // A patient asks to cancel the new visit; a second pending request is refused; staff approve
        var cancel = await _scheduling.PatientCancelRequestAsync(approved.Id!.Value, "Feeling better");
        Assert.True(cancel.Succeeded);
        var duplicate = await _scheduling.PatientCancelRequestAsync(approved.Id.Value, "Asking again");
        Assert.Equal("rule_cancel_one_pending", duplicate.Violation!.Constraint);
        Assert.True((await _scheduling.ReviewCancellationAsync(cancel.Id!.Value, approve: true)).Succeeded);

        // The clinic cancels appointment 25 outright
        Assert.True((await _scheduling.ClinicCancelAsync(25, "Provider out")).Succeeded);

        // Appointment 13 was missed and already rescheduled once in the sample data
        var twice = await _scheduling.RescheduleMissedAsync(13);
        Assert.Equal("rule_already_rescheduled", twice.Violation!.Constraint);
        Assert.True((await _scheduling.SetStatusAsync(40, AppointmentStatus.Missed)).Succeeded);
        var rescheduled = await _scheduling.RescheduleMissedAsync(40);
        Assert.True(rescheduled.Succeeded, rescheduled.Violation?.Message);

        await using var db = await _clinic.OpenAsync();
        var request = await db.AppointmentRequests.SingleAsync(r => r.RequestId == 9);
        Assert.Equal((RequestStatus.Approved, 13, approved.Id), (request.Status, request.ReviewedBy, request.AppointmentId));
        Assert.Equal(RequestStatus.Denied, (await db.AppointmentRequests.SingleAsync(r => r.RequestId == 10)).Status);
        Assert.Equal(RequestStatus.Pending, (await db.AppointmentRequests.SingleAsync(r => r.RequestId == submitted.Id)).Status);
        Assert.Equal(AppointmentStatus.Canceled, (await db.Appointments.SingleAsync(a => a.AppointmentId == approved.Id)).Status);
        Assert.Equal(AppointmentStatus.Canceled, (await db.Appointments.SingleAsync(a => a.AppointmentId == 25)).Status);
        var reviewed = await db.CancellationRequests.SingleAsync(c => c.CancelRequestId == cancel.Id);
        Assert.Equal((RequestStatus.Approved, 13), (reviewed.Status, reviewed.ReviewedBy));
        Assert.NotNull(reviewed.ReviewedAt);
        Assert.Equal(40, (await db.Appointments.SingleAsync(a => a.AppointmentId == rescheduled.Id)).RescheduledFromId);
    }

    [Fact]
    public async Task Edits_to_reference_data_are_checked_by_the_database_too()
    {
        await using var db = await _clinic.OpenAsync();
        var room = await db.Rooms.SingleAsync(r => r.RoomId == 1);
        room.Capacity = 2; // standard rooms hold exactly one
        var result = await _scheduling.SaveAsync(db);
        Assert.Equal("ck_room_type_capacity", result.Violation!.Constraint);
        Assert.StartsWith("Standard rooms hold exactly one patient", await _catalog.DescribeAsync(result.Violation.Constraint));

        db.ChangeTracker.Clear();
        db.Patients.Add(new Patient
        {
            HomeLocationId = 1, FirstName = "Dup", LastName = "Email", DateOfBirth = new DateOnly(1990, 1, 1),
            Email = "linda.carter@mail.example", Phone = "713-555-0000",
        });
        result = await _scheduling.SaveAsync(db);
        Assert.Equal((DbRuleKind.Unique, "uq_patient_email"), (result.Violation!.Kind, result.Violation.Constraint));
    }

    [Fact]
    public async Task The_database_describes_its_own_rules_and_runs_every_report()
    {
        var constraints = await _catalog.ConstraintsAsync();
        Assert.Contains(constraints.Rows, r => r[1] == "ex_appt_patient_no_overlap" && r[2] == "EXCLUDE");
        Assert.Contains(constraints.Rows, r => r[1] == "uq_cancel_one_pending" && r[2] == "UNIQUE (partial index)");
        Assert.Equal(24, constraints.Rows.Count(r => r[2] == "CHECK"));
        Assert.All(constraints.Rows.Where(r => r[2] == "CHECK"), r => Assert.NotEqual("", r[3])); // each CHECK is described

        Assert.Equal(17, (await _catalog.TriggersAsync()).Rows.Count);
        Assert.Equal(
            ["sp_approve_request", "sp_deny_request", "sp_reschedule_missed", "sp_validate_appointment"],
            (await _catalog.RoutinesAsync()).Rows.Select(r => r[0]));

        Assert.Equal(8, _catalog.Reports.Count);
        var today = new DateOnly(2026, 11, 13);
        foreach (var report in _catalog.Reports)
        {
            Assert.NotEqual("", report.Title);
            var result = await _catalog.RunReportAsync(report, today);
            Assert.NotEmpty(result.Columns);
            Assert.NotEmpty(result.Rows);
        }

        // Q1 from the report: the infusion nurses carried the most hours in October 2026
        var workload = await _catalog.RunReportAsync(_catalog.Reports[0], today);
        Assert.Equal(["Luis Herrera", "RN"], workload.Rows[0][..2]);
        Assert.Equal("5.0", workload.Rows[0][4]);
    }

    [Fact]
    public async Task Bootstrapper_creates_a_missing_database_once_and_leaves_an_existing_one_alone()
    {
        var name = $"clinic_v2_test_boot_{Guid.NewGuid():N}";
        var connectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = name }.ConnectionString;
        try
        {
            var applied = await DatabaseBootstrapper.EnsureCreatedAsync(connectionString, includeSampleData: false);
            Assert.Equal(["01_schema.sql", "02_rules.sql"], applied);

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var insert = new NpgsqlCommand(
                    "INSERT INTO location (name, street, zip, phone) VALUES ('Test Site', '1 Main St', '77001', '713-555-0000')", connection);
                await insert.ExecuteNonQueryAsync();
            }

            Assert.Empty(await DatabaseBootstrapper.EnsureCreatedAsync(connectionString, includeSampleData: true));

            await using var check = new NpgsqlConnection(connectionString);
            await check.OpenAsync();
            await using var count = new NpgsqlCommand("SELECT count(*) FROM location", check);
            Assert.Equal(1L, await count.ExecuteScalarAsync()); // not re-created, not seeded
        }
        finally
        {
            var admin = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = "postgres" };
            await using var server = new NpgsqlConnection(admin.ConnectionString);
            await server.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", server);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
