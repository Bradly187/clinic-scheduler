using ClinicSchedulerV2.Data.Database;

namespace ClinicSchedulerV2.Tests;

/// <summary>Rules from 04_accounts.sql: who an account belongs to, and what a patient login may write.</summary>
public class AccountRuleTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private const string InsertAppointment =
        "INSERT INTO appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time) VALUES ";

    [Fact]
    public async Task An_account_belongs_to_exactly_one_active_person_and_each_person_has_one_account()
    {
        await using var s = await DbSession.BeginAsync(db);

        var v = await s.RejectedAsync(
            "INSERT INTO app_user (user_name, normalized_user_name, employee_id, patient_id) VALUES ('x', 'X', 1, 1)");
        Assert.Equal("ck_app_user_one_person", v.Constraint);
        v = await s.RejectedAsync("INSERT INTO app_user (user_name, normalized_user_name) VALUES ('y', 'Y')");
        Assert.Equal("ck_app_user_one_person", v.Constraint);

        await s.CreateUserAsync("mgonzalez", employeeId: 1);
        v = await s.RejectedAsync(
            "INSERT INTO app_user (user_name, normalized_user_name, employee_id) VALUES ('maria2', 'MARIA2', 1)");
        Assert.Equal("uq_app_user_employee", v.Constraint);

        await s.ExecAsync("UPDATE patient SET is_active = FALSE WHERE patient_id = 12");
        v = await s.RejectedAsync(
            "INSERT INTO app_user (user_name, normalized_user_name, patient_id) VALUES ('tgreen', 'TGREEN', 12)");
        Assert.Equal("rule_account_person_inactive", v.Constraint);
    }

    [Fact]
    public async Task Deactivating_a_person_locks_their_account_and_reactivating_unlocks_it()
    {
        await using var s = await DbSession.BeginAsync(db);
        var userId = await s.CreateUserAsync("lcarter", patientId: 1);

        await s.ExecAsync("UPDATE patient SET is_active = FALSE WHERE patient_id = 1");
        Assert.True(await s.ScalarAsync<bool>($"SELECT lockout_end > now() + INTERVAL '1000 years' FROM app_user WHERE id = {userId}"));

        await s.ExecAsync("UPDATE patient SET is_active = TRUE WHERE patient_id = 1");
        Assert.True(await s.ScalarAsync<bool>($"SELECT lockout_end IS NULL FROM app_user WHERE id = {userId}"));
    }

    [Fact]
    public async Task A_patient_login_may_only_request_things_for_themselves()
    {
        await using var s = await DbSession.BeginAsync(db);
        var linda = await s.CreateUserAsync("lcarter", patientId: 1);
        await s.ActAsUserAsync(linda);

        // Own request: fine. Someone else's: refused.
        Assert.Equal(1, await s.ExecAsync(
            "INSERT INTO appointment_request (patient_id, service_type_id, preferred_start) VALUES (1, 2, '2026-12-14 10:00')"));
        var v = await s.RejectedAsync(
            "INSERT INTO appointment_request (patient_id, service_type_id, preferred_start) VALUES (2, 2, '2026-12-14 10:00')");
        Assert.Equal(("rule_patient_scope", "You can only request appointments for yourself"), (v.Constraint, v.Message));

        // Booking directly or reviewing a request is staff work
        v = await s.RejectedAsync(InsertAppointment + "(1, 1, 2, 2, 9, '2026-12-08 09:00', '2026-12-08 09:30')");
        Assert.Equal("rule_patient_scope", v.Constraint);
        v = await s.RejectedAsync("UPDATE appointment SET status = 'Canceled' WHERE appointment_id = 29");
        Assert.Equal("rule_patient_scope", v.Constraint);
        v = await s.RejectedAsync("SELECT sp_deny_request(7, 9, 'no')");
        Assert.Equal("rule_patient_scope", v.Constraint);

        // Cancellation: own appointment yes (33 is Linda's), someone else's no (26 is Maria Santos's)
        Assert.Equal(1, await s.ExecAsync(
            "INSERT INTO cancellation_request (appointment_id, source, reason) VALUES (33, 'Patient', 'Out of town')"));
        v = await s.RejectedAsync(
            "INSERT INTO cancellation_request (appointment_id, source, reason) VALUES (25, 'Patient', 'Not mine')");
        Assert.Equal("You can only ask to cancel your own appointments", v.Message);
        v = await s.RejectedAsync("UPDATE cancellation_request SET status = 'Approved', reviewed_by = 9 WHERE appointment_id = 33");
        Assert.Equal("Only clinic staff can review cancellation requests", v.Message);
    }

    [Fact]
    public async Task A_staff_login_acts_as_its_employee_and_the_audit_names_both()
    {
        await using var s = await DbSession.BeginAsync(db, actingEmployeeId: null);
        var kevin = await s.CreateUserAsync("kbrooks", employeeId: 10);
        await s.ActAsUserAsync(kevin);

        Assert.Equal(10, await s.ScalarAsync<int>("SELECT current_employee_id()"));
        Assert.Equal(0, await s.ScalarAsync<int>("SELECT COALESCE(current_patient_id(), 0)"));

        await s.ExecAsync(InsertAppointment + "(3, 1, 2, 2, 10, '2026-12-08 09:00', '2026-12-08 09:30')");
        var row = Assert.Single(await s.RowsAsync("SELECT changed_by, changed_by_user FROM audit_log WHERE table_name = 'Appointment'"));
        Assert.Equal(["10", kevin.ToString()], row);
    }

    [Fact]
    public async Task Patients_and_other_providers_cannot_write_visit_notes()
    {
        await using var s = await DbSession.BeginAsync(db, actingEmployeeId: null);
        var linda = await s.CreateUserAsync("lcarter", patientId: 1);
        var whitfield = await s.CreateUserAsync("jwhitfield", employeeId: 2);

        await s.ActAsUserAsync(linda);
        var v = await s.RejectedAsync("INSERT INTO visit_note (appointment_id, author_id, body) VALUES (16, 1, 'x')");
        Assert.Equal("Patients cannot write visit notes", v.Message);

        // Dr. Whitfield signed in, writing in Dr. Gonzalez's name
        await s.ActAsUserAsync(whitfield);
        v = await s.RejectedAsync("INSERT INTO visit_note (appointment_id, author_id, body) VALUES (16, 1, 'x')");
        Assert.Equal("rule_note_wrong_author", v.Constraint);
    }
}
