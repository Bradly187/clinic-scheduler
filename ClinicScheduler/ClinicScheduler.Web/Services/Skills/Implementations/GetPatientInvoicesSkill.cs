using System.Text;
using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Shared.Services;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Staff skill: retrieves invoices for a patient by name.</summary>
public sealed class GetPatientInvoicesSkill : ISkill
{
    private readonly IRepository<Patient> _patientRepo;
    private readonly IBillingService _billingService;
    private readonly IClinicTimeFormatter _clock;

    public GetPatientInvoicesSkill(IRepository<Patient> patientRepo, IBillingService billingService, IClinicTimeFormatter clock)
    {
        _patientRepo = patientRepo;
        _billingService = billingService;
        _clock = clock;
    }

    public string Name => "get_patient_invoices";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "get_patient_invoices",
            ["description"] = "Gets all invoices for a patient. Staff only.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["patientName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Full or partial name of the patient."
                    }
                },
                ["required"] = new JsonArray { "patientName" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var patientName = arguments?["patientName"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(patientName))
            return "Error: patientName is required.";

        var patients = await _patientRepo.FindAsync(
            p => p.FirstName.Contains(patientName) || p.LastName.Contains(patientName));
        if (patients.Count == 0) return $"No patient found matching '{patientName}'.";
        if (patients.Count > 1)
            return $"Multiple patients match '{patientName}': {string.Join(", ", patients.Select(p => p.FullName))}. Please be more specific.";

        var patient = patients.First();
        var invoices = await _billingService.GetPatientInvoicesAsync(patient.Id);

        if (invoices.Count == 0)
            return $"No invoices found for {patient.FullName}.";

        var sb = new StringBuilder($"Invoices for {patient.FullName}:\n");
        foreach (var inv in invoices.OrderByDescending(i => i.IssuedAt))
        {
            sb.AppendLine($"- #{inv.InvoiceNumber} | Status: {inv.Status} | Total: {inv.Total:C} | Balance: {inv.Balance:C} | Issued: {inv.IssuedAt:MMM d, yyyy}");
        }
        return sb.ToString();
    }
}
