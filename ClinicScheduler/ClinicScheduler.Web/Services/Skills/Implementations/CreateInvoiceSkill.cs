using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Shared.Services;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Staff skill: creates a new invoice for a patient.</summary>
public sealed class CreateInvoiceSkill : ISkill
{
    private readonly IRepository<Patient> _patientRepo;
    private readonly IBillingService _billingService;
    private readonly IClinicTimeFormatter _clock;

    public CreateInvoiceSkill(IRepository<Patient> patientRepo, IBillingService billingService, IClinicTimeFormatter clock)
    {
        _patientRepo = patientRepo;
        _billingService = billingService;
        _clock = clock;
    }

    public string Name => "create_invoice";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "create_invoice",
            ["description"] = "Creates a new draft invoice for a patient. Staff only.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["patientName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Full or partial name of the patient."
                    },
                    ["dueInDays"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Number of days until invoice is due. Default 30."
                    }
                },
                ["required"] = new JsonArray { "patientName" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var patientName = arguments?["patientName"]?.GetValue<string>();
        var dueInDays = arguments?["dueInDays"]?.GetValue<int>() ?? 30;

        if (string.IsNullOrWhiteSpace(patientName))
            return "Error: patientName is required.";

        var patients = await _patientRepo.FindAsync(
            p => p.FirstName.Contains(patientName) || p.LastName.Contains(patientName));
        if (patients.Count == 0) return $"No patient found matching '{patientName}'.";
        if (patients.Count > 1)
            return $"Multiple patients match '{patientName}': {string.Join(", ", patients.Select(p => p.FullName))}. Please be more specific.";

        var patient = patients.First();
        var dueDate = DateTime.UtcNow.AddDays(dueInDays);

        var invoice = await _billingService.CreateInvoiceAsync(patient.Id, dueDate);

        return $"Created draft invoice {invoice.InvoiceNumber} for {patient.FullName}. Due date: {dueDate:MMM d, yyyy}. " +
               $"Add line items and send it when ready.";
    }
}
