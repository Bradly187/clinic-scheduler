using ClinicSchedulerV2.Data.Database;

namespace ClinicSchedulerV2.Tests;

/// <summary>Rules from 06_care.sql: waitlist, visit notes, notifications and sp_book_appointment.</summary>
public class CareRuleTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private const string InsertAppointment =
        "INSERT INTO appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time) VALUES ";

    // ---- Waitlist ------------------------------------------------------------------------

    [Fact]
    public async Task A_patient_waits_once_per_service_and_a_booked_entry_names_their_matching_visit()
    {
        await using var s = await DbSession.BeginAsync(db);

        // Jennifer Lee (5) is already waiting for a follow-up (service 2)
        var v = await s.RejectedAsync(
            "INSERT INTO waitlist_entry (patient_id, service_type_id, earliest_date, latest_date) VALUES (5, 2, clinic_now()::date, clinic_now()::date + 7)");
        Assert.Equal((DbRuleKind.Unique, "uq_waitlist_one_waiting"), (v.Kind, v.Constraint));
        v = await s.RejectedAsync(
            "INSERT INTO waitlist_entry (patient_id, service_type_id, earliest_date, latest_date) VALUES (5, 5, clinic_now()::date + 7, clinic_now()::date)");
        Assert.Equal("ck_waitlist_dates", v.Constraint);
        v = await s.RejectedAsync(
            "INSERT INTO waitlist_entry (patient_id, service_type_id, earliest_date, latest_date) VALUES (5, 5, '2020-01-01', '2020-02-01')");
        Assert.Equal("rule_waitlist_window_past", v.Constraint);

        var entry = await s.ScalarAsync<int>("SELECT entry_id FROM waitlist_entry WHERE patient_id = 5");
        v = await s.RejectedAsync($"UPDATE waitlist_entry SET status = 'Booked' WHERE entry_id = {entry}");
        Assert.Equal("ck_waitlist_booked", v.Constraint);
        // Appointment 33 is Linda Carter's infusion, not Jennifer's follow-up
        v = await s.RejectedAsync($"UPDATE waitlist_entry SET status = 'Booked', appointment_id = 33 WHERE entry_id = {entry}");
        Assert.Equal("rule_waitlist_wrong_appointment", v.Constraint);

        await s.ExecAsync($"UPDATE waitlist_entry SET status = 'Booked', appointment_id = 27 WHERE entry_id = {entry}");
        v = await s.RejectedAsync($"UPDATE waitlist_entry SET status = 'Removed' WHERE entry_id = {entry}");
        Assert.Equal("rule_waitlist_closed", v.Constraint);
    }

    // ---- Visit notes ---------------------------------------------------------------------

    [Fact]
    public async Task Notes_are_written_by_the_visits_provider_and_are_final_once_signed()
    {
        await using var s = await DbSession.BeginAsync(db, actingEmployeeId: null);

        // Appointment 16 was Dr. Gonzalez (1); 17 was canceled; 29 has not happened yet
        var v = await s.RejectedAsync("INSERT INTO visit_note (appointment_id, author_id, body) VALUES (16, 2, 'x')");
        Assert.Equal("Only the visit's provider can write its notes", v.Message);
        v = await s.RejectedAsync("INSERT INTO visit_note (appointment_id, author_id, body) VALUES (17, 1, 'x')");
        Assert.Equal("rule_note_canceled_visit", v.Constraint);
        v = await s.RejectedAsync("INSERT INTO visit_note (appointment_id, author_id, body, signed_at) VALUES (29, 1, 'x', clinic_now())");
        Assert.Equal("rule_note_sign_incomplete", v.Constraint);
        v = await s.RejectedAsync("INSERT INTO visit_note (appointment_id, author_id, body) VALUES (16, 1, '   ')");
        Assert.Equal("ck_note_body", v.Constraint);

        // Drafting before the visit is fine; signing waits for the visit to be completed
        var note = await s.ScalarAsync<int>("INSERT INTO visit_note (appointment_id, author_id, body) VALUES (29, 1, 'Pre-visit plan') RETURNING note_id");
        v = await s.RejectedAsync($"UPDATE visit_note SET signed_at = clinic_now() WHERE note_id = {note}");
        Assert.Equal("rule_note_sign_incomplete", v.Constraint);

        // The sample note for appointment 16 is signed
        v = await s.RejectedAsync("UPDATE visit_note SET body = 'changed' WHERE appointment_id = 16");
        Assert.Equal("rule_note_signed", v.Constraint);
        v = await s.RejectedAsync("DELETE FROM visit_note WHERE appointment_id = 16");
        Assert.Equal("rule_note_signed", v.Constraint);

        // The unsigned draft for appointment 12 can be finished and signed by its author
        Assert.Equal(1, await s.ExecAsync("UPDATE visit_note SET body = body || ' ESR 12.', signed_at = clinic_now() WHERE appointment_id = 12"));
        Assert.True(await s.ScalarAsync<bool>("SELECT updated_at IS NOT NULL FROM visit_note WHERE appointment_id = 12"));
    }

    // ---- Notifications -------------------------------------------------------------------

    [Fact]
    public async Task Booking_and_canceling_notify_the_patient_and_the_provider()
    {
        await using var s = await DbSession.BeginAsync(db);
        var appointment = await s.ScalarAsync<int>(
            InsertAppointment + "(3, 1, 2, 2, 9, '2026-12-08 09:00', '2026-12-08 09:30') RETURNING appointment_id");

        var rows = await s.RowsAsync(
            $"SELECT recipient_patient_id, recipient_employee_id, kind, body FROM notification WHERE appointment_id = {appointment} ORDER BY notification_id");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["3", "", "AppointmentBooked", "You are booked for Follow-up visit on Tue Dec 8 at 9:00 AM with Maria Gonzalez, MD at Medical Center."], rows[0]);
        Assert.Equal(["", "1", "AppointmentBooked"], rows[1][..3]);

        await s.ExecAsync($"INSERT INTO cancellation_request (appointment_id, source, submitted_by, reason) VALUES ({appointment}, 'Clinic', 9, 'Provider out')");
        Assert.Equal(2L, await s.ScalarAsync<long>(
            $"SELECT count(*) FROM notification WHERE appointment_id = {appointment} AND kind = 'AppointmentCanceled'"));
    }

    [Fact]
    public async Task A_canceled_future_visit_alerts_staff_and_patients_waiting_for_that_kind_of_slot()
    {
        await using var s = await DbSession.BeginAsync(db);
        // A Wednesday one to two weeks out, so the slot is always in the future
        var day = await s.ScalarAsync<DateOnly>(
            "SELECT d::date FROM generate_series(clinic_now()::date + 7, clinic_now()::date + 13, INTERVAL '1 day') d WHERE EXTRACT(ISODOW FROM d) = 3");
        // Jennifer Lee (5) waits for a follow-up at Memorial City with Dr. Gonzalez; cover that day
        await s.ExecAsync($"UPDATE waitlist_entry SET latest_date = DATE '{day:yyyy-MM-dd}' + 7 WHERE patient_id = 5");

        // David Ramirez's follow-up with her there, then the clinic cancels it
        var appointment = await s.ScalarAsync<int>(
            InsertAppointment + $"(6, 1, 6, 2, 13, '{day:yyyy-MM-dd} 10:00', '{day:yyyy-MM-dd} 10:30') RETURNING appointment_id");
        await s.ExecAsync($"INSERT INTO cancellation_request (appointment_id, source, submitted_by, reason) VALUES ({appointment}, 'Clinic', 10, 'Provider schedule change')");

        // Staff who work at Memorial City: Kevin Brooks (10), Angela Reyes (11), Monica Patel (13)
        var staff = await s.RowsAsync(
            "SELECT recipient_employee_id FROM notification WHERE kind = 'WaitlistSlotOpened' AND recipient_employee_id IS NOT NULL ORDER BY 1");
        Assert.Equal(["10", "11", "13"], staff.Select(r => r[0]));
        Assert.EndsWith("Waiting: Jennifer Lee.", await s.ScalarAsync<string>(
            "SELECT body FROM notification WHERE kind = 'WaitlistSlotOpened' AND recipient_employee_id = 10"));
        Assert.Equal(1L, await s.ScalarAsync<long>(
            "SELECT count(*) FROM notification WHERE kind = 'WaitlistSlotOpened' AND recipient_patient_id = 5"));
        // Nobody else waits for a follow-up at Memorial City, and the canceled patient is not told
        Assert.Equal(0L, await s.ScalarAsync<long>(
            "SELECT count(*) FROM notification WHERE kind = 'WaitlistSlotOpened' AND recipient_patient_id IN (6, 10)"));
    }

    [Fact]
    public async Task Request_decisions_reach_the_patient_without_duplicate_notices()
    {
        await using var s = await DbSession.BeginAsync(db);
        var appointmentId = await s.ScalarAsync<int>("SELECT sp_approve_request(7, 13, 1, 6, '2026-12-07 10:00')");

        // The booking notice became the approval: one notice to David Ramirez, not two
        var patient = await s.RowsAsync($"SELECT kind, title FROM notification WHERE recipient_patient_id = 6 AND appointment_id = {appointmentId}");
        Assert.Equal([["RequestApproved", "Request approved"]], patient);

        await s.ExecAsync("SELECT sp_deny_request(9, 12, 'Please book through your provider')");
        Assert.Equal("Your request for a follow-up visit was not approved: Please book through your provider.",
            await s.ScalarAsync<string>("SELECT body FROM notification WHERE recipient_patient_id = 9 AND kind = 'RequestDenied'"));
    }

    [Fact]
    public async Task Notifications_come_only_from_the_database_and_readers_only_mark_their_own_read()
    {
        await using var s = await DbSession.BeginAsync(db, actingEmployeeId: null);
        var v = await s.RejectedAsync(
            "INSERT INTO notification (recipient_patient_id, kind, title, body) VALUES (1, 'InvoiceSent', 'Fake', 'Pay now')");
        Assert.Equal("rule_notification_fixed", v.Constraint);

        // Invoice 1 sent a notice to Linda Carter (patient 1)
        var notice = await s.ScalarAsync<int>("SELECT notification_id FROM notification WHERE recipient_patient_id = 1 AND kind = 'InvoiceSent'");
        var robert = await s.CreateUserAsync("ralvarez", patientId: 2);
        var linda = await s.CreateUserAsync("lcarter", patientId: 1);

        await s.ActAsUserAsync(robert);
        v = await s.RejectedAsync($"UPDATE notification SET read_at = clinic_now() WHERE notification_id = {notice}");
        Assert.Equal("You can only mark your own notifications read", v.Message);

        await s.ActAsUserAsync(linda);
        v = await s.RejectedAsync($"UPDATE notification SET body = 'edited' WHERE notification_id = {notice}");
        Assert.Equal("Only the read time of a notification can change", v.Message);
        Assert.Equal(1, await s.ExecAsync($"UPDATE notification SET read_at = clinic_now() WHERE notification_id = {notice}"));
    }

    // ---- Booking into any free room --------------------------------------------------------

    [Fact]
    public async Task Booking_at_a_location_takes_the_first_room_every_rule_accepts()
    {
        await using var s = await DbSession.BeginAsync(db);
        // Nov 23, 11:00 at the Medical Center: Exam 1 (room 1) is taken by appointment 29
        var first = await s.ScalarAsync<int>("SELECT sp_book_appointment(3, 3, 2, 1, '2026-11-23 11:00', '2026-11-23 11:30', 9)");
        Assert.Equal(2, await s.ScalarAsync<int>($"SELECT room_id FROM appointment WHERE appointment_id = {first}"));

        // A rule no other room can fix is raised as itself: Dr. Gonzalez is already busy
        var v = await s.RejectedAsync("SELECT sp_book_appointment(7, 1, 2, 1, '2026-11-23 11:00', '2026-11-23 11:30', 9)");
        Assert.Equal("rule_provider_double_booked", v.Constraint);

        // Fill the remaining standard rooms (3 and 4) with other providers, then one more
        await s.ExecAsync("SELECT sp_book_appointment(5, 4, 2, 1, '2026-11-23 11:00', '2026-11-23 11:30', 9)");
        await s.ExecAsync("SELECT sp_book_appointment(6, 8, 5, 1, '2026-11-23 11:00', '2026-11-23 11:30', 9)");
        v = await s.RejectedAsync("SELECT sp_book_appointment(7, 5, 5, 1, '2026-11-23 11:00', '2026-11-23 11:30', 9)");
        Assert.Equal(("rule_no_room_available", "Every standard room at location 1 is full at that time"), (v.Constraint, v.Message));
    }
}
