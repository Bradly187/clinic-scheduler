namespace ClinicScheduler.Web.Services.Workflows;

/// <summary>
/// Billing workflow: handles invoice queries, balance checks, payment recording,
/// and invoice creation via the AI agent for staff users.
/// </summary>
public sealed class BillingWorkflowPack : IWorkflowPack
{
    private readonly ClinicProfile _profile;

    public BillingWorkflowPack(ClinicProfile profile) => _profile = profile;

    public string Name => "billing";

    public IEnumerable<SpecialistAgent> GetSpecialists()
    {
        var clinic = _profile.ClinicDescriptor;
        return
        [
            new SpecialistAgent(
                "billing_agent",
                "Handles billing questions: view invoices, check patient balances, record payments, create invoices. Use for any request about money, invoices, payments, or balances.",
                $"You are the Billing specialist for a {clinic}. You help staff manage invoices and payments. " +
                "You can look up patient invoices and balances, create new invoices, and record payments. " +
                "For recording payments, always preview first (confirmed=false) and only confirm after the user agrees. " +
                "Present financial information clearly with dollar amounts.",
                ["get_patient_invoices", "get_patient_balance", "create_invoice", "record_payment"]),
        ];
    }
}
