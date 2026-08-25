using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Staff skill: gets a patient's outstanding billing balance.</summary>
public sealed class GetPatientBalanceSkill : ISkill
{
    private readonly IRepository<Patient> _patientRepo;
    private readonly IBillingService _billingService;

    public GetPatientBalanceSkill(IRepository<Patient> patientRepo, IBillingService billingService)
    {
        _patientRepo = patientRepo;
        _billingService = billingService;
    }

    public string Name => "get_patient_balance";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "get_patient_balance",
            ["description"] = "Gets the outstanding billing balance for a patient. Staff only.",
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
        var balance = await _billingService.GetPatientBalanceAsync(patient.Id);

        return balance == 0
            ? $"{patient.FullName} has no outstanding balance."
            : $"{patient.FullName} has an outstanding balance of {balance:C}.";
    }
}
