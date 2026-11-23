using ClinicSchedulerV2.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace ClinicSchedulerV2.Data;

public sealed record BookingRequest(
    int PatientId, int ProviderId, int RoomId, int ServiceTypeId, int? PlanItemId,
    DateTime Start, DateTime End, string? Notes);

/// <summary>Appointments, appointment requests and cancellations.</summary>
public sealed class SchedulingService(ClinicDb clinic) : DbService(clinic)
{
    public Task<WriteResult> BookAsync(BookingRequest booking) => WriteAsync(async (db, actor) =>
    {
        var appointment = new Appointment
        {
            PatientId = booking.PatientId,
            ProviderId = booking.ProviderId,
            RoomId = booking.RoomId,
            ServiceTypeId = booking.ServiceTypeId,
            PlanItemId = booking.PlanItemId,
            CreatedBy = actor.EmployeeId ?? 0, // the foreign key to staff rejects anyone else
            StartTime = booking.Start,
            EndTime = booking.End,
            Notes = Clean(booking.Notes),
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.AppointmentId;
    });

    /// <summary>Calls sp_book_appointment, which takes the first room at the location every rule accepts.</summary>
    public Task<WriteResult> BookAtLocationAsync(
        int patientId, int providerId, int serviceTypeId, int locationId, DateTime start, DateTime end, string? notes) =>
        WriteAsync((db, actor) => ScalarAsync(db,
            "SELECT sp_book_appointment(@patient, @provider, @service, @location, @start::timestamp, @end::timestamp, @staff::integer, @notes)",
            ("patient", patientId), ("provider", providerId), ("service", serviceTypeId), ("location", locationId),
            ("start", start), ("end", end), ("staff", actor.EmployeeId), ("notes", Clean(notes))));

    public Task<WriteResult> SetStatusAsync(int appointmentId, AppointmentStatus status) => WriteAsync(async (db, _) =>
    {
        await db.Appointments.Where(a => a.AppointmentId == appointmentId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, status));
        return appointmentId;
    });

    public Task<WriteResult> MoveAsync(int appointmentId, int roomId, DateTime start, DateTime end) => WriteAsync(async (db, _) =>
    {
        await db.Appointments.Where(a => a.AppointmentId == appointmentId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.RoomId, roomId)
                .SetProperty(a => a.StartTime, start)
                .SetProperty(a => a.EndTime, end));
        return appointmentId;
    });

    /// <summary>The clinic cancels a visit. The trigger approves it and cancels the appointment.</summary>
    public Task<WriteResult> ClinicCancelAsync(int appointmentId, string reason) => WriteAsync((db, actor) => ScalarAsync(db,
        """
        INSERT INTO cancellation_request (appointment_id, source, submitted_by, reason)
        VALUES (@appointment, 'Clinic', @staff::integer, @reason)
        RETURNING cancel_request_id
        """,
        ("appointment", appointmentId), ("staff", actor.EmployeeId), ("reason", reason)));

    /// <summary>A patient asks to cancel. The request waits in the staff queue.</summary>
    public Task<WriteResult> PatientCancelRequestAsync(int appointmentId, string reason) => WriteAsync((db, _) => ScalarAsync(db,
        """
        INSERT INTO cancellation_request (appointment_id, source, reason)
        VALUES (@appointment, 'Patient', @reason)
        RETURNING cancel_request_id
        """,
        ("appointment", appointmentId), ("reason", reason)));

    /// <summary>Staff decide a patient's cancellation request. Approval cancels the appointment (trigger).</summary>
    public Task<WriteResult> ReviewCancellationAsync(int cancelRequestId, bool approve) => WriteAsync(async (db, actor) =>
    {
        var status = approve ? RequestStatus.Approved : RequestStatus.Denied;
        await db.CancellationRequests.Where(c => c.CancelRequestId == cancelRequestId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.ReviewedBy, actor.EmployeeId));
        return cancelRequestId;
    });

    public Task<WriteResult> SubmitAppointmentRequestAsync(
        int patientId, int serviceTypeId, int? preferredLocationId, DateTime preferredStart, string? notes) =>
        WriteAsync(async (db, _) =>
        {
            var request = new AppointmentRequest
            {
                PatientId = patientId,
                ServiceTypeId = serviceTypeId,
                PreferredLocationId = preferredLocationId,
                PreferredStart = preferredStart,
                PatientNotes = Clean(notes),
            };
            db.AppointmentRequests.Add(request);
            await db.SaveChangesAsync();
            return request.RequestId;
        });

    /// <summary>A patient withdraws a request that has not been reviewed yet.</summary>
    public Task<WriteResult> WithdrawRequestAsync(int requestId) => WriteAsync(async (db, _) =>
    {
        await db.AppointmentRequests.Where(r => r.RequestId == requestId).ExecuteDeleteAsync();
        return requestId;
    });

    /// <summary>Calls sp_approve_request, which books the appointment and links it to the request.</summary>
    public Task<WriteResult> ApproveRequestAsync(int requestId, int providerId, int roomId, DateTime start) =>
        WriteAsync((db, actor) => ScalarAsync(db,
            "SELECT sp_approve_request(@request, @staff::integer, @provider, @room, @start::timestamp)",
            ("request", requestId), ("staff", actor.EmployeeId), ("provider", providerId),
            ("room", roomId), ("start", start)));

    public Task<WriteResult> DenyRequestAsync(int requestId, string note) => WriteAsync(async (db, actor) =>
    {
        await ScalarAsync(db, "SELECT 1 FROM sp_deny_request(@request, @staff::integer, @note)",
            ("request", requestId), ("staff", actor.EmployeeId), ("note", note));
        return requestId;
    });

    /// <summary>Calls sp_reschedule_missed, which finds the first slot every rule accepts.</summary>
    public Task<WriteResult> RescheduleMissedAsync(int appointmentId) => WriteAsync((db, actor) => ScalarAsync(db,
        "SELECT sp_reschedule_missed(@appointment, @staff::integer)",
        ("appointment", appointmentId), ("staff", actor.EmployeeId)));

    internal static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
