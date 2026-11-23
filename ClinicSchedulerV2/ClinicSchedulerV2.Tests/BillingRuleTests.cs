using ClinicSchedulerV2.Data.Database;
using Npgsql;

namespace ClinicSchedulerV2.Tests;

/// <summary>Rules from 05_billing.sql. Sample invoices: 1 Paid, 2 Sent (part paid), 3 Draft, 4 Sent (overdue).</summary>
public class BillingRuleTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static string NewDraft(int patientId) =>
        $"INSERT INTO invoice (patient_id, created_by) VALUES ({patientId}, 11) RETURNING invoice_id";

    [Fact]
    public async Task Sample_invoices_load_through_the_rules_with_derived_totals()
    {
        await using var s = await DbSession.BeginAsync(db);
        var rows = await s.RowsAsync("SELECT invoice_id, status, total, paid, balance, is_overdue FROM vw_invoice_summary WHERE invoice_id <= 4 ORDER BY 1");
        Assert.Equal(
            [
                ["1", "Paid", "815.00", "815.00", "0.00", "False"],
                ["2", "Sent", "1850.00", "500.00", "1350.00", "False"],
                ["3", "Draft", "205.00", "0", "205.00", "False"],
                ["4", "Sent", "250.00", "0", "250.00", "True"],
            ],
            rows);
    }

    [Fact]
    public async Task Lines_only_change_on_drafts_and_only_bill_that_patients_completed_visits_once()
    {
        await using var s = await DbSession.BeginAsync(db);
        var draft = await s.ScalarAsync<int>(NewDraft(1));

        // Appointment 2 is Robert Alvarez's; 33 is Linda's but not completed; 1 is already on invoice 1
        var v = await s.RejectedAsync($"INSERT INTO invoice_line (invoice_id, appointment_id, description, unit_price) VALUES ({draft}, 2, 'x', 10)");
        Assert.Equal("rule_line_wrong_patient", v.Constraint);
        v = await s.RejectedAsync($"INSERT INTO invoice_line (invoice_id, appointment_id, description, unit_price) VALUES ({draft}, 33, 'x', 10)");
        Assert.Equal("Appointment 33 is Scheduled; only completed visits can be billed", v.Message);
        v = await s.RejectedAsync($"INSERT INTO invoice_line (invoice_id, appointment_id, description, unit_price) VALUES ({draft}, 1, 'x', 10)");
        Assert.Equal("Appointment 1 is already billed on invoice 1", v.Message);

        // Sent invoices are closed to edits
        v = await s.RejectedAsync("INSERT INTO invoice_line (invoice_id, description, unit_price) VALUES (2, 'Late fee', 25)");
        Assert.Equal("rule_invoice_not_draft", v.Constraint);
        v = await s.RejectedAsync("UPDATE invoice_line SET unit_price = 1 WHERE invoice_id = 2");
        Assert.Equal("rule_invoice_not_draft", v.Constraint);

        // Voiding a draft frees its visits to be billed again
        await s.ExecAsync("UPDATE invoice SET status = 'Void', void_reason = 'Wrong codes' WHERE invoice_id = 3");
        Assert.Equal(1, await s.ExecAsync(
            $"INSERT INTO invoice_line (invoice_id, appointment_id, description, unit_price) VALUES ({await s.ScalarAsync<int>(NewDraft(3))}, 10, 'Injection', 180)"));
    }

    [Fact]
    public async Task Invoices_move_only_through_legal_status_changes()
    {
        await using var s = await DbSession.BeginAsync(db);
        var empty = await s.ScalarAsync<int>(NewDraft(5));

        var v = await s.RejectedAsync($"UPDATE invoice SET status = 'Sent' WHERE invoice_id = {empty}");
        Assert.Equal("rule_invoice_empty", v.Constraint);
        v = await s.RejectedAsync("INSERT INTO invoice (patient_id, created_by, status) VALUES (5, 11, 'Sent')");
        Assert.Equal("A new invoice starts as a draft", v.Message);
        v = await s.RejectedAsync("UPDATE invoice SET status = 'Paid' WHERE invoice_id = 3");
        Assert.Equal("An invoice cannot go from Draft to Paid", v.Message);
        v = await s.RejectedAsync("UPDATE invoice SET status = 'Paid' WHERE invoice_id = 2");
        Assert.Equal("rule_invoice_balance_due", v.Constraint);
        v = await s.RejectedAsync("UPDATE invoice SET status = 'Draft' WHERE invoice_id = 4");
        Assert.Equal("rule_invoice_transition", v.Constraint);

        // Voiding needs a reason, and is refused once money has been taken
        v = await s.RejectedAsync("UPDATE invoice SET status = 'Void' WHERE invoice_id = 4");
        Assert.Equal((DbRuleKind.Check, "ck_invoice_void"), (v.Kind, v.Constraint));
        v = await s.RejectedAsync("UPDATE invoice SET status = 'Void', void_reason = 'x' WHERE invoice_id = 2");
        Assert.Equal("rule_invoice_has_payments", v.Constraint);

        // Paid is final; only drafts can be deleted
        v = await s.RejectedAsync("UPDATE invoice SET notes = 'edit' WHERE invoice_id = 1");
        Assert.Equal("rule_invoice_closed", v.Constraint);
        v = await s.RejectedAsync("DELETE FROM invoice WHERE invoice_id = 4");
        Assert.Equal("rule_invoice_delete_draft_only", v.Constraint);
        Assert.Equal(1, await s.ExecAsync("DELETE FROM invoice WHERE invoice_id = 3")); // its lines go with it

        // Sending stamps the dates: issued today, due in 30 days
        var draft = await s.ScalarAsync<int>(NewDraft(5));
        await s.ExecAsync($"INSERT INTO invoice_line (invoice_id, description, unit_price) VALUES ({draft}, 'Copy of records', 15)");
        await s.ExecAsync($"UPDATE invoice SET status = 'Sent' WHERE invoice_id = {draft}");
        Assert.True(await s.ScalarAsync<bool>(
            $"SELECT issued_on = clinic_now()::date AND due_on = issued_on + 30 FROM invoice WHERE invoice_id = {draft}"));
    }

    [Fact]
    public async Task Payments_only_on_sent_invoices_never_over_the_balance_and_paying_in_full_closes_it()
    {
        await using var s = await DbSession.BeginAsync(db);

        var v = await s.RejectedAsync("INSERT INTO payment (invoice_id, amount, method, recorded_by) VALUES (3, 10, 'Cash', 9)");
        Assert.Equal("rule_payment_invoice_not_open", v.Constraint);
        v = await s.RejectedAsync("INSERT INTO payment (invoice_id, amount, method, recorded_by) VALUES (2, 1350.01, 'Card', 9)");
        Assert.Equal("Payment of 1350.01 is more than the 1350.00 still due on invoice 2", v.Message);
        v = await s.RejectedAsync("INSERT INTO payment (invoice_id, amount, method, recorded_by) VALUES (2, 0, 'Card', 9)");
        Assert.Equal("ck_payment_amount", v.Constraint);

        await s.ExecAsync("INSERT INTO payment (invoice_id, amount, method, recorded_by) VALUES (2, 1000, 'Card', 9)");
        Assert.Equal("Sent", await s.ScalarAsync<string>("SELECT status::text FROM invoice WHERE invoice_id = 2"));
        await s.ExecAsync("INSERT INTO payment (invoice_id, amount, method, recorded_by) VALUES (2, 350, 'Card', 9)");
        Assert.Equal("Paid", await s.ScalarAsync<string>("SELECT status::text FROM invoice WHERE invoice_id = 2"));

        v = await s.RejectedAsync("UPDATE payment SET amount = 1 WHERE invoice_id = 2");
        Assert.Equal("rule_payment_immutable", v.Constraint);
        v = await s.RejectedAsync("DELETE FROM payment WHERE invoice_id = 2");
        Assert.Equal("rule_payment_immutable", v.Constraint);
    }

    [Fact]
    public async Task Two_simultaneous_full_payments_settle_the_invoice_once()
    {
        // This test commits, so it bills its own invoice rather than touching the sample ones
        await using var setup = await db.OpenAsync();
        await using var create = new NpgsqlCommand(
            """
            WITH i AS (INSERT INTO invoice (patient_id, created_by) VALUES (7, 11) RETURNING invoice_id)
            INSERT INTO invoice_line (invoice_id, description, unit_price) SELECT invoice_id, 'Records request', 250 FROM i
            RETURNING invoice_id
            """, setup);
        var invoice = (int)(await create.ExecuteScalarAsync())!;
        await using (var send = new NpgsqlCommand($"UPDATE invoice SET status = 'Sent' WHERE invoice_id = {invoice}", setup))
            await send.ExecuteNonQueryAsync();

        // Two clerks record the full 250.00 at the same moment
        await using var first = await db.OpenAsync();
        await using var second = await db.OpenAsync();
        await using var firstTx = await first.BeginTransactionAsync();
        await using (var pay = new NpgsqlCommand(
            $"INSERT INTO payment (invoice_id, amount, method, recorded_by) VALUES ({invoice}, 250, 'Cash', 9)", first, firstTx))
            await pay.ExecuteNonQueryAsync();

        var secondPay = Task.Run(async () =>
        {
            await using var pay = new NpgsqlCommand(
                $"INSERT INTO payment (invoice_id, amount, method, recorded_by) VALUES ({invoice}, 250, 'Card', 10)", second);
            await pay.ExecuteNonQueryAsync();
        });
        await Task.Delay(500);
        Assert.False(secondPay.IsCompleted, "the second payment should wait for the first");
        await firstTx.CommitAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => secondPay);
        Assert.Equal("rule_payment_invoice_not_open", DbRuleViolation.From(ex)?.Constraint); // already Paid

        await using var total = new NpgsqlCommand($"SELECT SUM(amount) FROM payment WHERE invoice_id = {invoice}", setup);
        Assert.Equal(250m, await total.ExecuteScalarAsync());
    }

    [Fact]
    public async Task The_superbill_carries_what_an_insurer_needs_and_each_patient_has_one_primary_policy()
    {
        await using var s = await DbSession.BeginAsync(db);
        // Appointment 2 was Luis Herrera, RN running Robert Alvarez's rituximab infusion on Oct 5
        var line = Assert.Single(await s.RowsAsync(
            """
            SELECT patient, payer_name, member_id, provider, credential, npi,
                   to_char(service_date, 'YYYY-MM-DD'), billing_code, amount
            FROM vw_superbill WHERE invoice_id = 2 AND billing_code = '96413'
            """));
        Assert.Equal(["Robert Alvarez", "Aetna", "W2849302711", "Luis Herrera", "RN", "1649027381", "2026-10-05", "96413", "650.00"], line);
        // The drug line has no visit, so it is not on the superbill
        Assert.Equal(1L, await s.ScalarAsync<long>("SELECT count(*) FROM vw_superbill WHERE invoice_id = 2"));

        var v = await s.RejectedAsync(
            "INSERT INTO insurance_policy (patient_id, payer_name, member_id, effective_from) VALUES (1, 'Cigna', 'C1', '2026-01-01')");
        Assert.Equal((DbRuleKind.Unique, "uq_policy_one_primary"), (v.Kind, v.Constraint));
    }
}
