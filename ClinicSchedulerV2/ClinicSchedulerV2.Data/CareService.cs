using ClinicSchedulerV2.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace ClinicSchedulerV2.Data;

/// <summary>Waitlist, visit notes and notifications (rules in 06_care.sql).</summary>
public sealed class CareService(ClinicDb clinic) : DbService(clinic)
{
    public Task<WriteResult> JoinWaitlistAsync(
        int patientId, int serviceTypeId, int? locationId, int? providerId, DateOnly earliest, DateOnly latest, string? notes) =>
        WriteAsync(async (db, _) =>
        {
            var entry = new WaitlistEntry
            {
                PatientId = patientId,
                ServiceTypeId = serviceTypeId,
                PreferredLocationId = locationId,
                PreferredProviderId = providerId,
                EarliestDate = earliest,
                LatestDate = latest,
                Notes = SchedulingService.Clean(notes),
            };
            db.Waitlist.Add(entry);
            await db.SaveChangesAsync();
            return entry.EntryId;
        });

    public Task<WriteResult> LeaveWaitlistAsync(int entryId) => SetWaitlistAsync(entryId, WaitlistStatus.Removed, null);

    /// <summary>Links a waiting entry to the appointment it became.</summary>
    public Task<WriteResult> MarkWaitlistBookedAsync(int entryId, int appointmentId) =>
        SetWaitlistAsync(entryId, WaitlistStatus.Booked, appointmentId);

    /// <summary>Creates or updates the signed-in provider's draft note for a visit.</summary>
    public Task<WriteResult> SaveNoteAsync(int? noteId, int appointmentId, string body) => WriteAsync(async (db, actor) =>
    {
        if (noteId is { } id)
        {
            await db.VisitNotes.Where(n => n.NoteId == id)
                .ExecuteUpdateAsync(set => set.SetProperty(n => n.Body, body));
            return id;
        }
        var note = new VisitNote { AppointmentId = appointmentId, AuthorId = actor.EmployeeId ?? 0, Body = body };
        db.VisitNotes.Add(note);
        await db.SaveChangesAsync();
        return note.NoteId;
    });

    public Task<WriteResult> SignNoteAsync(int noteId) => WriteAsync(async (db, _) =>
    {
        await db.Database.ExecuteSqlAsync($"UPDATE visit_note SET signed_at = clinic_now() WHERE note_id = {noteId}");
        return noteId;
    });

    public Task<WriteResult> DeleteNoteAsync(int noteId) => WriteAsync(async (db, _) =>
    {
        await db.VisitNotes.Where(n => n.NoteId == noteId).ExecuteDeleteAsync();
        return noteId;
    });

    public Task<WriteResult> MarkReadAsync(int notificationId) => WriteAsync(async (db, _) =>
    {
        await db.Database.ExecuteSqlAsync(
            $"UPDATE notification SET read_at = clinic_now() WHERE notification_id = {notificationId} AND read_at IS NULL");
        return notificationId;
    });

    /// <summary>Marks everything addressed to the current actor read.</summary>
    public Task<WriteResult> MarkAllReadAsync() => WriteAsync(async (db, actor) => await db.Database.ExecuteSqlAsync(
        $"""
        UPDATE notification SET read_at = clinic_now()
        WHERE read_at IS NULL
          AND (recipient_patient_id = {actor.PatientId} OR recipient_employee_id = {(actor.PatientId is null ? actor.EmployeeId : null)})
        """));

    /// <summary>Notifications addressed to the current actor, newest first.</summary>
    public async Task<List<Notification>> MyNotificationsAsync(int take = 100)
    {
        var actor = await Clinic.ActorAsync();
        await using var db = await Clinic.OpenAsync();
        return await Mine(db, actor).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.NotificationId)
            .Take(take).ToListAsync();
    }

    public async Task<int> UnreadCountAsync()
    {
        var actor = await Clinic.ActorAsync();
        await using var db = await Clinic.OpenAsync();
        return await Mine(db, actor).CountAsync(n => n.ReadAt == null);
    }

    private static IQueryable<Notification> Mine(ClinicDbContext db, Actor actor) => actor.PatientId is { } patient
        ? db.Notifications.Where(n => n.RecipientPatientId == patient)
        : db.Notifications.Where(n => n.RecipientEmployeeId == actor.EmployeeId);

    private Task<WriteResult> SetWaitlistAsync(int entryId, WaitlistStatus status, int? appointmentId) => WriteAsync(async (db, _) =>
    {
        await db.Waitlist.Where(w => w.EntryId == entryId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(w => w.Status, status)
                .SetProperty(w => w.AppointmentId, appointmentId));
        return entryId;
    });
}
