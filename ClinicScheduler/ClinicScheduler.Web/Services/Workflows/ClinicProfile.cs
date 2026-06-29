using Microsoft.Extensions.Configuration;

namespace ClinicScheduler.Web.Services.Workflows;

/// <summary>
/// Tenant-style framing for the AI assistant. Today it carries the clinic's <see cref="Specialty"/>,
/// which packs interpolate into their coordinator/specialist prompts instead of a hardcoded
/// "pain-management" literal — configured via <c>Clinic:Specialty</c> (default "pain-management").
/// </summary>
public sealed class ClinicProfile
{
    public ClinicProfile(IConfiguration configuration)
    {
        var specialty = configuration["Clinic:Specialty"];
        Specialty = string.IsNullOrWhiteSpace(specialty) ? "pain-management" : specialty.Trim();
    }

    /// <summary>The clinic's specialty label, e.g. "pain-management", "cardiology".</summary>
    public string Specialty { get; }

    /// <summary>Convenience descriptor used in prompts, e.g. "pain-management clinic".</summary>
    public string ClinicDescriptor => $"{Specialty} clinic";
}
