using ClinicSchedulerV2.Data.Database;

namespace ClinicSchedulerV2.Tests;

/// <summary>
/// The 22 tests from db/mysql/trigger_tests.sql (T1-T22), run against the PostgreSQL port,
/// plus tests for the rules that became declarative constraints in the port.
/// Nothing here goes through application code: every rule is enforced by the database.
/// </summary>
public class SchedulingRuleTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private const string InsertAppointment =
        "INSERT INTO appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time) VALUES ";

    [Fact]
    public void Sample_data_loads_with_every_rule_active()
    {
        Assert.Equal(["01_schema.sql", "02_rules.sql", "03_sample_data.sql"], db.AppliedScripts);
    }

    // ---- Scheduling rules (trg_appointment_before_insert) ------------------------------

    [Fact]
    public async Task T01_Provider_cannot_be_double_booked()
    {
        await using var s = await DbSession.BeginAsync(db);
        // Dr. Gonzalez (1) already sees Linda Carter on Nov 23 at 11:00 (appointment 29)
        var v = await s.RejectedAsync(InsertAppointment + "(3, 1, 2, 2, 9, '2026-11-23 11:00', '2026-11-23 11:30')");
        Assert.Equal("rule_provider_double_booked", v.Constraint);
        Assert.Equal("Provider 1 is already booked at that time (appointment 29)", v.Message);
    }

    [Fact]
    public async Task T02_Patient_cannot_be_double_booked()
    {
        await using var s = await DbSession.BeginAsync(db);
        // Linda Carter (1) has a lab draw on Nov 30 at 08:00 (appointment 32)
        var v = await s.RejectedAsync(InsertAppointment + "(1, 1, 1, 2, 9, '2026-11-30 08:00', '2026-11-30 08:30')");
        Assert.Equal("rule_patient_double_booked", v.Constraint);
        Assert.Equal("Patient 1 already has an appointment at that time (appointment 32)", v.Message);
    }

    [Fact]
    public async Task T03_Room_type_must_match_the_service()
    {
        await using var s = await DbSession.BeginAsync(db);
        var v = await s.RejectedAsync(InsertAppointment + "(3, 5, 1, 3, 9, '2026-12-07 09:00', '2026-12-07 11:00')");
        Assert.Equal("rule_room_type_mismatch", v.Constraint);
        Assert.Equal("Room type mismatch: this service requires room type Infusion, but room 1 is Standard", v.Message);
    }

    [Fact]
    public async Task T04_Provider_must_be_qualified_for_the_service()
    {
        await using var s = await DbSession.BeginAsync(db);
        var v = await s.RejectedAsync(InsertAppointment + "(3, 5, 1, 2, 9, '2026-12-07 09:00', '2026-12-07 09:30')");
        Assert.Equal("rule_provider_not_qualified", v.Constraint);
        Assert.Equal("Provider 5 is not qualified for service 2", v.Message);
    }

    [Fact]
    public async Task T05_Provider_must_work_at_the_rooms_location()
    {
        await using var s = await DbSession.BeginAsync(db);
        var v = await s.RejectedAsync(InsertAppointment + "(3, 2, 1, 2, 9, '2026-12-07 09:00', '2026-12-07 09:30')");
        Assert.Equal("rule_provider_wrong_location", v.Constraint);
        Assert.Equal("Provider 2 does not work at location 1", v.Message);
    }

    [Fact]
    public async Task T06_One_nurse_may_run_overlapping_infusions_in_the_same_suite()
    {
        await using var s = await DbSession.BeginAsync(db);
        // Emily Tran, RN (7) already runs appointment 38 in the Clear Lake suite, 13:00-15:00
        Assert.Equal(1, await s.ExecAsync(InsertAppointment + "(12, 7, 15, 3, 12, '2026-12-03 13:30', '2026-12-03 15:30')"));
    }

    [Fact]
    public async Task T07_Infusion_exemption_does_not_cover_a_different_suite()
    {
        await using var s = await DbSession.BeginAsync(db);
        var v = await s.RejectedAsync(InsertAppointment + "(11, 7, 10, 3, 13, '2026-12-03 13:30', '2026-12-03 15:30')");
        Assert.Equal("rule_provider_double_booked", v.Constraint);
        Assert.Equal("Provider 7 is already booked at that time (appointment 38)", v.Message);
    }

    [Fact]
    public async Task T08_Room_capacity_allows_the_third_infusion_chair_but_not_a_fourth()
    {
        await using var s = await DbSession.BeginAsync(db);
        Assert.Equal(1, await s.ExecAsync(InsertAppointment + "(2, 6, 5, 3, 10, '2026-11-30 09:00', '2026-11-30 11:00')"));
        var v = await s.RejectedAsync(InsertAppointment + "(3, 6, 5, 3, 10, '2026-11-30 09:00', '2026-11-30 11:00')");
        Assert.Equal("rule_room_full", v.Constraint);
        Assert.Equal("Room 5 is full at that time (capacity 3)", v.Message);
    }

    [Fact]
    public async Task T09_Location_cannot_exceed_its_concurrent_patient_limit()
    {
        await using var s = await DbSession.BeginAsync(db);
        await s.ExecAsync(InsertAppointment + "(2, 6, 5, 3, 10, '2026-11-30 09:00', '2026-11-30 11:00')");
        await s.ExecAsync("UPDATE location SET max_concurrent_patients = 3 WHERE location_id = 1");
        var v = await s.RejectedAsync(InsertAppointment + "(7, 1, 1, 2, 9, '2026-11-30 09:00', '2026-11-30 09:30')");
        Assert.Equal("rule_location_full", v.Constraint);
        Assert.Equal("Location 1 is at its limit of 3 concurrent patients at that time", v.Message);
    }

    // ---- Subtype rule ------------------------------------------------------------------

    [Fact]
    public async Task T10_An_employee_cannot_be_both_provider_and_staff()
    {
        await using var s = await DbSession.BeginAsync(db);
        var v = await s.RejectedAsync("INSERT INTO staff (employee_id, job_title) VALUES (1, 'Front Desk')");
        Assert.Equal("rule_subtype_disjoint", v.Constraint);
        Assert.Equal("Employee is already a Provider; an employee cannot be both Provider and Staff", v.Message);

        v = await s.RejectedAsync("INSERT INTO provider (employee_id, npi, credential) VALUES (9, '1999999999', 'RN')");
        Assert.Equal("Employee is already Staff; an employee cannot be both Provider and Staff", v.Message);
    }

    // ---- Cancellation rules ------------------------------------------------------------

    [Fact]
    public async Task T11_Only_one_pending_cancellation_per_appointment()
    {
        await using var s = await DbSession.BeginAsync(db);
        var v = await s.RejectedAsync(
            "INSERT INTO cancellation_request (appointment_id, source, reason) VALUES (26, 'Patient', 'Asking again')");
        Assert.Equal("rule_cancel_one_pending", v.Constraint);
        Assert.Equal("This appointment already has a pending cancellation request", v.Message);
    }

    [Fact]
    public async Task T12_Only_a_scheduled_appointment_can_receive_a_cancellation_request()
    {
        await using var s = await DbSession.BeginAsync(db);
        var v = await s.RejectedAsync(
            "INSERT INTO cancellation_request (appointment_id, source, reason) VALUES (1, 'Patient', 'Too late')");
        Assert.Equal("rule_cancel_not_scheduled", v.Constraint);
        Assert.Equal("Only a Scheduled appointment can receive a cancellation request", v.Message);
    }

    [Fact]
    public async Task T13_Clinic_cancellations_are_approved_automatically_and_cancel_the_appointment()
    {
        await using var s = await DbSession.BeginAsync(db);
        await s.ExecAsync(
            "INSERT INTO cancellation_request (appointment_id, source, submitted_by, reason) VALUES (28, 'Clinic', 12, 'Provider called in sick')");

        var row = Assert.Single(await s.RowsAsync(
            """
            SELECT cr.source, cr.status, cr.submitted_by, cr.reviewed_by, cr.reviewed_at = cr.submitted_at, a.status
            FROM cancellation_request cr JOIN appointment a ON a.appointment_id = cr.appointment_id
            WHERE cr.appointment_id = 28
            """));
        Assert.Equal(["Clinic", "Approved", "12", "12", "True", "Canceled"], row);
    }

    [Fact]
    public async Task T14_Approving_a_patient_cancellation_stamps_the_review_and_cancels_the_appointment()
    {
        await using var s = await DbSession.BeginAsync(db);
        await s.ExecAsync("UPDATE cancellation_request SET status = 'Approved', reviewed_by = 9 WHERE cancel_request_id = 8");

        var row = Assert.Single(await s.RowsAsync(
            """
            SELECT cr.status, cr.reviewed_by, cr.reviewed_at IS NOT NULL, a.appointment_id, a.status
            FROM cancellation_request cr JOIN appointment a ON a.appointment_id = cr.appointment_id
            WHERE cr.cancel_request_id = 8
            """));
        Assert.Equal(["Approved", "9", "True", "29", "Canceled"], row);
    }

    [Fact]
    public async Task Denying_a_patient_cancellation_leaves_the_appointment_scheduled()
    {
        await using var s = await DbSession.BeginAsync(db);
        await s.ExecAsync("UPDATE cancellation_request SET status = 'Denied', reviewed_by = 9 WHERE cancel_request_id = 8");
        Assert.Equal("Scheduled", await s.ScalarAsync<string>("SELECT status::text FROM appointment WHERE appointment_id = 29"));
    }

    // ---- Stored routines ---------------------------------------------------------------

    [Fact]
    public async Task T15_Approving_a_request_books_the_appointment_and_links_it()
    {
        await using var s = await DbSession.BeginAsync(db);
        var appointmentId = await s.ScalarAsync<int>("SELECT sp_approve_request(7, 13, 1, 6, '2026-12-07 10:00')");

        var request = Assert.Single(await s.RowsAsync(
            "SELECT status, reviewed_by, appointment_id FROM appointment_request WHERE request_id = 7"));
        Assert.Equal(["Approved", "13", appointmentId.ToString()], request);

        // Follow-up visits default to 30 minutes, taken from service_type
        var appointment = Assert.Single(await s.RowsAsync(
            $"SELECT patient_id, provider_id, room_id, to_char(start_time, 'HH24:MI'), to_char(end_time, 'HH24:MI'), status, created_by FROM appointment WHERE appointment_id = {appointmentId}"));
        Assert.Equal(["6", "1", "6", "10:00", "10:30", "Scheduled", "13"], appointment);
    }

    [Fact]
    public async Task Approving_a_request_into_a_conflict_is_rejected_and_leaves_it_pending()
    {
        await using var s = await DbSession.BeginAsync(db);
        // Dr. Gonzalez is busy on Nov 23 at 11:00, so the booking inside the routine fails
        var v = await s.RejectedAsync("SELECT sp_approve_request(7, 13, 1, 6, '2026-11-23 11:00')");
        Assert.Equal("rule_provider_double_booked", v.Constraint);
        Assert.Equal("Pending", await s.ScalarAsync<string>("SELECT status::text FROM appointment_request WHERE request_id = 7"));

        v = await s.RejectedAsync("SELECT sp_approve_request(1, 13, 1, 6, '2026-12-07 10:00')");
        Assert.Equal("Only a Pending request can be approved", v.Message);
        v = await s.RejectedAsync("SELECT sp_approve_request(999, 13, 1, 6, '2026-12-07 10:00')");
        Assert.Equal("Appointment request not found", v.Message);
    }

    [Fact]
    public async Task T16_Denying_a_request_records_the_reason()
    {
        await using var s = await DbSession.BeginAsync(db);
        await s.ExecAsync("SELECT sp_deny_request(8, 12, 'Labs not due until January per treatment plan')");

        var row = Assert.Single(await s.RowsAsync(
            "SELECT status, reviewed_by, decision_note, appointment_id FROM appointment_request WHERE request_id = 8"));
        Assert.Equal(["Denied", "12", "Labs not due until January per treatment plan", ""], row);

        var v = await s.RejectedAsync("SELECT sp_deny_request(8, 12, 'again')");
        Assert.Equal("Only a Pending request can be denied", v.Message);
    }

    [Fact]
    public async Task T17_A_missed_appointment_is_rescheduled_into_the_first_open_slot()
    {
        await using var s = await DbSession.BeginAsync(db);
        // Jennifer Lee misses her Nov 18 follow-up with Dr. Gonzalez (appointment 27)
        await s.ExecAsync("UPDATE appointment SET status = 'Missed' WHERE appointment_id = 27");
        var newId = await s.ScalarAsync<int>("SELECT sp_reschedule_missed(27, 13)");

        var row = Assert.Single(await s.RowsAsync(
            $"""
            SELECT rescheduled_from_id, patient_id, provider_id, service_type_id, plan_item_id, created_by, status,
                   end_time - start_time = INTERVAL '30 minutes',
                   start_time::date > GREATEST(DATE '2026-11-18', clinic_now()::date),
                   start_time::date <= GREATEST(DATE '2026-11-18', clinic_now()::date) + 30,
                   notes
            FROM appointment WHERE appointment_id = {newId}
            """));
        Assert.Equal(["27", "5", "1", "2", "9", "13", "Scheduled", "True", "True", "True",
                      "Auto-rescheduled from missed appointment 27"], row);

        // The first open slot really is the first: no earlier candidate would have been accepted
        var earlier = await s.ScalarAsync<long>(
            $"""
            SELECT count(*) FROM appointment n
            WHERE n.appointment_id = {newId}
              AND n.start_time = (GREATEST(DATE '2026-11-18', clinic_now()::date) + 1) + TIME '08:00'
            """);
        if (DateTime.Now < new DateTime(2026, 11, 18))
            Assert.Equal(1L, earlier); // Nov 19 at 08:00, as in the original test run

        var v = await s.RejectedAsync("SELECT sp_reschedule_missed(27, 13)");
        Assert.Equal("This appointment has already been rescheduled", v.Message);
    }

    [Fact]
    public async Task T18_Only_a_missed_appointment_can_be_rescheduled()
    {
        await using var s = await DbSession.BeginAsync(db);
        var v = await s.RejectedAsync("SELECT sp_reschedule_missed(17, 13)"); // 17 is Canceled
        Assert.Equal("rule_reschedule_not_missed", v.Constraint);
        Assert.Equal("Only a Missed appointment can be rescheduled", v.Message);

        v = await s.RejectedAsync("SELECT sp_reschedule_missed(999, 13)");
        Assert.Equal("Appointment not found", v.Message);
    }

    [Fact]
    public async Task Rescheduling_skips_slots_the_rules_reject()
    {
        await using var s = await DbSession.BeginAsync(db);
        // Appointment 30 (Nov 24 10:00, Dr. Gonzalez, room 6) is missed. Block her whole next
        // working day at every site, so the routine has to move on to the day after.
        await s.ExecAsync("UPDATE appointment SET status = 'Missed' WHERE appointment_id = 30");
        var nextDay = await s.ScalarAsync<DateTime>(
            """
            SELECT d::timestamp FROM generate_series(GREATEST(DATE '2026-11-24', clinic_now()::date) + 1,
                                                     GREATEST(DATE '2026-11-24', clinic_now()::date) + 7, INTERVAL '1 day') d
            WHERE EXTRACT(ISODOW FROM d) BETWEEN 1 AND 5 ORDER BY d LIMIT 1
            """);
        await s.ExecAsync("DELETE FROM cancellation_request WHERE appointment_id IN (SELECT appointment_id FROM appointment WHERE provider_id = 1 AND start_time::date = '" + nextDay.ToString("yyyy-MM-dd") + "')");
        await s.ExecAsync("UPDATE appointment SET status = 'Canceled' WHERE provider_id = 1 AND start_time::date = '" + nextDay.ToString("yyyy-MM-dd") + "'");
        await s.ExecAsync(InsertAppointment + $"(12, 1, 2, 1, 9, '{nextDay:yyyy-MM-dd} 08:00', '{nextDay:yyyy-MM-dd} 17:00')");

        var newId = await s.ScalarAsync<int>("SELECT sp_reschedule_missed(30, 13)");
        var start = await s.ScalarAsync<DateTime>($"SELECT start_time FROM appointment WHERE appointment_id = {newId}");
        Assert.True(start.Date > nextDay.Date, $"expected a slot after {nextDay:d}, got {start}");
    }

    // ---- Views -------------------------------------------------------------------------

    [Fact]
    public async Task T19_Provider_schedule_view_lists_upcoming_visits_without_canceled_ones()
    {
        await using var s = await DbSession.BeginAsync(db);
        var rows = await s.RowsAsync(
            """
            SELECT appt_date, start_time, end_time, patient, service, room_name, status
            FROM vw_provider_schedule
            WHERE provider_id = 1 AND appt_date >= '2026-11-13'
            ORDER BY appt_date, start_time
            """);
        Assert.Equal(3, rows.Count);
        Assert.Equal(["2026-11-18", "09:00", "09:30", "Jennifer Lee", "Follow-up visit", "Exam 1", "Scheduled"], rows[0]);
        Assert.Equal("Linda Carter", rows[1][3]);
        Assert.Equal("David Ramirez", rows[2][3]);
    }

    [Fact]
    public async Task T20_Room_schedule_view_shows_the_infusion_suite_filling_up()
    {
        await using var s = await DbSession.BeginAsync(db);
        await s.ExecAsync(InsertAppointment + "(2, 6, 5, 3, 10, '2026-11-30 09:00', '2026-11-30 11:00')");
        var rows = await s.RowsAsync(
            "SELECT room_name, capacity, start_time, end_time, patient, provider FROM vw_room_schedule WHERE room_id = 5 AND appt_date = '2026-11-30' ORDER BY patient");
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(["Infusion Suite", "3", "09:00", "11:00"], r[..4]));
        Assert.Equal(["Anthony Davis", "Linda Carter", "Robert Alvarez"], rows.Select(r => r[4]));
    }

    [Fact]
    public async Task T21_Pending_request_view_is_one_work_queue_of_both_kinds()
    {
        await using var s = await DbSession.BeginAsync(db);
        var rows = await s.RowsAsync(
            "SELECT request_type, request_id, patient, service FROM vw_pending_requests ORDER BY submitted_at");
        Assert.Equal(5, rows.Count(r => r[0] == "Appointment request"));
        Assert.Equal(3, rows.Count(r => r[0] == "Cancellation request"));
        Assert.Equal(["Appointment request", "7", "David Ramirez", "Follow-up visit"], rows[0]); // oldest first
    }

    // ---- Audit log ---------------------------------------------------------------------

    [Fact]
    public async Task T22_Every_change_is_audited_with_the_acting_employee_and_old_and_new_values()
    {
        await using var s = await DbSession.BeginAsync(db, actingEmployeeId: 13);
        Assert.Equal(0L, await s.ScalarAsync<long>("SELECT count(*) FROM audit_log")); // the sample load is not audited

        var appointmentId = await s.ScalarAsync<int>(
            InsertAppointment + "(3, 1, 2, 2, 9, '2026-12-08 09:00', '2026-12-08 09:30') RETURNING appointment_id");
        await s.ExecAsync($"UPDATE appointment SET status = 'Completed' WHERE appointment_id = {appointmentId}");
        await s.ExecAsync("DELETE FROM appointment_request WHERE request_id = 11 AND status = 'Pending'");
        // Ids come from sequences, which other tests advance even though they roll back
        var cancelId = await s.ScalarAsync<int>(
            "INSERT INTO cancellation_request (appointment_id, source, submitted_by, reason) VALUES (28, 'Clinic', 12, 'Provider called in sick') RETURNING cancel_request_id");

        var rows = await s.RowsAsync(
            "SELECT table_name, record_id, action, changed_by, old_values ->> 'status', new_values ->> 'status' FROM audit_log ORDER BY audit_id");
        Assert.Equal(
            [
                ["Appointment", appointmentId.ToString(), "INSERT", "13", "", "Scheduled"],
                ["Appointment", appointmentId.ToString(), "UPDATE", "13", "Scheduled", "Completed"],
                ["AppointmentRequest", "11", "DELETE", "13", "Pending", ""],
                ["CancellationRequest", cancelId.ToString(), "INSERT", "13", "", "Approved"],
                // the cancellation trigger's own update of the appointment is audited too
                ["Appointment", "28", "UPDATE", "13", "Scheduled", "Canceled"],
            ],
            rows);
    }

    [Fact]
    public async Task Audit_records_null_when_no_employee_is_identified()
    {
        await using var s = await DbSession.BeginAsync(db, actingEmployeeId: null);
        await s.ExecAsync("UPDATE appointment SET notes = 'x' WHERE appointment_id = 25");
        Assert.Equal("", Assert.Single(await s.RowsAsync("SELECT changed_by FROM audit_log"))[0]);
    }

    // ---- Updates are re-validated ------------------------------------------------------

    [Fact]
    public async Task Moving_an_appointment_into_a_conflict_is_rejected_but_status_changes_are_not_rechecked()
    {
        await using var s = await DbSession.BeginAsync(db);
        // Appointment 30 is Dr. Gonzalez on Nov 24; moving it onto appointment 29's slot must fail
        var v = await s.RejectedAsync(
            "UPDATE appointment SET start_time = '2026-11-23 11:00', end_time = '2026-11-23 11:30' WHERE appointment_id = 30");
        Assert.Equal("rule_provider_double_booked", v.Constraint);

        // Make appointment 29's provider unqualified; completing the visit still works because
        // a status change does not re-run the booking checks
        await s.ExecAsync("DELETE FROM provider_qualification WHERE employee_id = 1 AND service_type_id = 2");
        Assert.Equal(1, await s.ExecAsync("UPDATE appointment SET status = 'Completed' WHERE appointment_id = 29"));
        v = await s.RejectedAsync("UPDATE appointment SET room_id = 2 WHERE appointment_id = 30");
        Assert.Equal("rule_provider_not_qualified", v.Constraint);
    }

    [Fact]
    public async Task Reinstating_a_canceled_appointment_is_checked_like_a_new_booking()
    {
        await using var s = await DbSession.BeginAsync(db);
        // Appointment 31 (Canceled): Daniel Okafor, room 1, Nov 25 14:00. Take his slot, then reinstate.
        await s.ExecAsync(InsertAppointment + "(3, 4, 2, 2, 9, '2026-11-25 14:00', '2026-11-25 14:30')");
        var v = await s.RejectedAsync("UPDATE appointment SET status = 'Scheduled' WHERE appointment_id = 31");
        Assert.Equal("rule_provider_double_booked", v.Constraint);
    }

    [Fact]
    public async Task Inactive_rooms_and_mismatched_plan_items_are_rejected()
    {
        await using var s = await DbSession.BeginAsync(db);
        await s.ExecAsync("UPDATE room SET is_active = FALSE WHERE room_id = 2");
        var v = await s.RejectedAsync(InsertAppointment + "(3, 1, 2, 2, 9, '2026-12-08 09:00', '2026-12-08 09:30')");
        Assert.Equal("Room 2 is inactive", v.Message);

        // Plan item 1 is Linda Carter's infusion, not patient 3's follow-up
        v = await s.RejectedAsync(
            "INSERT INTO appointment (patient_id, provider_id, room_id, service_type_id, plan_item_id, created_by, start_time, end_time) VALUES (3, 1, 1, 2, 1, 9, '2026-12-08 09:00', '2026-12-08 09:30')");
        Assert.Equal("rule_plan_item_mismatch", v.Constraint);
    }

    // ---- Single-row rules: CHECK constraints -------------------------------------------

    [Theory]
    [InlineData("'2026-12-05 09:00', '2026-12-05 09:30'", "ck_appt_weekday")]   // a Saturday
    [InlineData("'2026-12-08 07:30', '2026-12-08 08:00'", "ck_appt_hours")]
    [InlineData("'2026-12-08 16:30', '2026-12-08 17:30'", "ck_appt_hours")]
    [InlineData("'2026-12-08 09:15', '2026-12-08 09:45'", "ck_appt_slot_grid")]
    [InlineData("'2026-12-08 09:30', '2026-12-08 09:00'", "ck_appt_order")]
    [InlineData("'2026-12-08 16:00', '2026-12-09 09:00'", "ck_appt_same_day")]
    public async Task Appointments_must_fit_clinic_hours_and_the_slot_grid(string times, string constraint)
    {
        await using var s = await DbSession.BeginAsync(db);
        var v = await s.RejectedAsync(InsertAppointment + $"(3, 1, 2, 2, 9, {times})");
        Assert.Equal(DbRuleKind.Check, v.Kind);
        Assert.Equal(constraint, v.Constraint);
    }

    [Theory]
    [InlineData("UPDATE room SET capacity = 2 WHERE room_id = 1", "ck_room_type_capacity")]
    [InlineData("UPDATE room SET capacity = 4 WHERE room_id = 5", "ck_room_type_capacity")]
    [InlineData("UPDATE provider SET npi = '12345' WHERE employee_id = 1", "ck_provider_npi")]
    [InlineData("UPDATE location SET zip = '7709A' WHERE location_id = 1", "ck_location_zip")]
    [InlineData("UPDATE service_type SET default_duration_min = 45 WHERE service_type_id = 1", "ck_service_duration")]
    [InlineData("UPDATE treatment_plan_item SET interval_weeks = 0 WHERE plan_item_id = 1", "ck_planitem_interval")]
    [InlineData("UPDATE medication_administration SET site = NULL WHERE administration_id = 15", "ck_medadmin_site")]
    [InlineData("UPDATE appointment_request SET status = 'Approved' WHERE request_id = 7", "ck_apptreq_status")]
    [InlineData("UPDATE cancellation_request SET status = 'Approved' WHERE cancel_request_id = 7", "ck_cancel_status")]
    [InlineData("INSERT INTO cancellation_request (appointment_id, source, reason) VALUES (25, 'Clinic', 'no submitter')", "ck_cancel_source")]
    public async Task Single_row_rules_are_check_constraints(string sql, string constraint)
    {
        await using var s = await DbSession.BeginAsync(db);
        var v = await s.RejectedAsync(sql);
        Assert.Equal(DbRuleKind.Check, v.Kind);
        Assert.Equal(constraint, v.Constraint);
    }

    // ---- Rules that became declarative constraints in the PostgreSQL port ---------------

    [Fact]
    public async Task Patient_overlap_is_also_an_exclusion_constraint()
    {
        await using var s = await DbSession.BeginAsync(db);
        // With the trigger out of the way, the constraint alone still refuses the row
        await s.ExecAsync("ALTER TABLE appointment DISABLE TRIGGER trg_appointment_before_insert");
        var v = await s.RejectedAsync(InsertAppointment + "(1, 1, 1, 2, 9, '2026-11-30 08:00', '2026-11-30 08:30')");
        Assert.Equal(DbRuleKind.Exclusion, v.Kind);
        Assert.Equal("ex_appt_patient_no_overlap", v.Constraint);

        // Canceled visits never block: appointment 17 (Canceled) overlaps nothing it would stop
        Assert.Equal(1, await s.ExecAsync(InsertAppointment + "(5, 1, 6, 2, 9, '2026-10-28 09:00', '2026-10-28 09:30')"));
    }

    [Fact]
    public async Task One_pending_cancellation_is_also_a_partial_unique_index()
    {
        await using var s = await DbSession.BeginAsync(db);
        await s.ExecAsync("ALTER TABLE cancellation_request DISABLE TRIGGER trg_cancel_before_insert");
        var v = await s.RejectedAsync(
            "INSERT INTO cancellation_request (appointment_id, source, reason) VALUES (26, 'Patient', 'Asking again')");
        Assert.Equal(DbRuleKind.Unique, v.Kind);
        Assert.Equal("uq_cancel_one_pending", v.Constraint);

        // A denied request does not block a new one (appointment 30 has one Denied, one Pending in the sample data)
        Assert.Equal(2L, await s.ScalarAsync<long>("SELECT count(*) FROM cancellation_request WHERE appointment_id = 30"));
    }
}
