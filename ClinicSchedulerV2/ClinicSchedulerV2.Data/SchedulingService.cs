using ClinicSchedulerV2.Data.Database;
using ClinicSchedulerV2.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace ClinicSchedulerV2.Data;

/// <summary>The outcome of a write: either it was stored, or the database rejected it.</summary>
public readonly record struct WriteResult(DbRuleViolation? Violation, int? Id = null)
{
    public bool Succeeded => Violation is null;
}

public sealed record BookingRequest(
    int PatientId, int ProviderId, int RoomId, int ServiceTypeId, int? PlanItemId,
    DateTime Start, DateTime End, string? Notes);

/// <summary>
/// Every write the app makes. There is deliberately no validation here: each method sends the
/// change to PostgreSQL and reports what the constraints, triggers and routines decided.
/// </summary>
public sealed class SchedulingService(ClinicDb clinic)
{
    public Task<WriteResult> BookAsync(BookingRequest booking) => WriteAsync(async db =>
    {
        var appointment = new Appointment
        {
            PatientId = booking.PatientId,
            ProviderId = booking.ProviderId,
            RoomId = booking.RoomId,
            ServiceTypeId = booking.ServiceTypeId,
            PlanItemId = booking.PlanItemId,
            CreatedBy = clinic.ActingEmployeeId ?? 0,
            StartTime = booking.Start,
            EndTime = booking.End,
            Notes = string.IsNullOrWhiteSpace(booking.Notes) ? null : booking.Notes.Trim(),
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.AppointmentId;
    });

    public Task<WriteResult> SetStatusAsync(int appointmentId, AppointmentStatus status) => WriteAsync(async db =>
    {
        await db.Appointments.Where(a => a.AppointmentId == appointmentId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, status));
        return appointmentId;
    });

    public Task<WriteResult> MoveAsync(int appointmentId, int roomId, DateTime start, DateTime end) => WriteAsync(async db =>
    {
        await db.Appointments.Where(a => a.AppointmentId == appointmentId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.RoomId, roomId)
                .SetProperty(a => a.StartTime, start)
                .SetProperty(a => a.EndTime, end));
        return appointmentId;
    });

    /// <summary>The clinic cancels a visit. The trigger approves it and cancels the appointment.</summary>
    public Task<WriteResult> ClinicCancelAsync(int appointmentId, string reason) => WriteAsync(db => ScalarAsync(db,
        """
        INSERT INTO cancellation_request (appointment_id, source, submitted_by, reason)
        VALUES (@appointment, 'Clinic', @staff::integer, @reason)
        RETURNING cancel_request_id
        """,
        ("appointment", appointmentId), ("staff", clinic.ActingEmployeeId), ("reason", reason)));

    /// <summary>A patient asks to cancel. The request waits in the staff queue.</summary>
    public Task<WriteResult> PatientCancelRequestAsync(int appointmentId, string reason) => WriteAsync(db => ScalarAsync(db,
        """
        INSERT INTO cancellation_request (appointment_id, source, reason)
        VALUES (@appointment, 'Patient', @reason)
        RETURNING cancel_request_id
        """,
        ("appointment", appointmentId), ("reason", reason)));

    /// <summary>Staff decide a patient's cancellation request. Approval cancels the appointment (trigger).</summary>
    public Task<WriteResult> ReviewCancellationAsync(int cancelRequestId, bool approve) => WriteAsync(async db =>
    {
        var staffId = clinic.ActingEmployeeId;
        var status = approve ? RequestStatus.Approved : RequestStatus.Denied;
        await db.CancellationRequests.Where(c => c.CancelRequestId == cancelRequestId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.ReviewedBy, staffId));
        return cancelRequestId;
    });

    public Task<WriteResult> SubmitAppointmentRequestAsync(
        int patientId, int serviceTypeId, int? preferredLocationId, DateTime preferredStart, string? notes) =>
        WriteAsync(async db =>
        {
            var request = new AppointmentRequest
            {
                PatientId = patientId,
                ServiceTypeId = serviceTypeId,
                PreferredLocationId = preferredLocationId,
                PreferredStart = preferredStart,
                PatientNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            };
            db.AppointmentRequests.Add(request);
            await db.SaveChangesAsync();
            return request.RequestId;
        });

    /// <summary>Calls sp_approve_request, which books the appointment and links it to the request.</summary>
    public Task<WriteResult> ApproveRequestAsync(int requestId, int providerId, int roomId, DateTime start) =>
        WriteAsync(db => ScalarAsync(db,
            "SELECT sp_approve_request(@request, @staff::integer, @provider, @room, @start::timestamp)",
            ("request", requestId), ("staff", clinic.ActingEmployeeId), ("provider", providerId),
            ("room", roomId), ("start", DateTime.SpecifyKind(start, DateTimeKind.Unspecified))));

    public Task<WriteResult> DenyRequestAsync(int requestId, string note) => WriteAsync(async db =>
    {
        await ScalarAsync(db, "SELECT 1 FROM sp_deny_request(@request, @staff::integer, @note)",
            ("request", requestId), ("staff", clinic.ActingEmployeeId), ("note", note));
        return requestId;
    });

    /// <summary>Calls sp_reschedule_missed, which finds the first slot every rule accepts.</summary>
    public Task<WriteResult> RescheduleMissedAsync(int appointmentId) => WriteAsync(db => ScalarAsync(db,
        "SELECT sp_reschedule_missed(@appointment, @staff::integer)",
        ("appointment", appointmentId), ("staff", clinic.ActingEmployeeId)));

    /// <summary>Saves any tracked changes (patients, rooms, locations) and reports a rejection.</summary>
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

    private static async Task<int> ScalarAsync(
        ClinicDbContext db, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<WriteResult> WriteAsync(Func<ClinicDbContext, Task<int>> write)
    {
        try
        {
            await using var db = await clinic.OpenAsync();
            return new WriteResult(null, await write(db));
        }
        catch (Exception ex) when (DbRuleViolation.From(ex) is { } violation)
        {
            return new WriteResult(violation);
        }
    }
}
