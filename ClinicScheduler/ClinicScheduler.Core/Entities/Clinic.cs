namespace ClinicScheduler.Core.Entities;

/// <summary>
/// The tenant root. A <see cref="Clinic"/> is one customer of the ClinicAgent platform and
/// owns all clinical and scheduling data beneath it (locations, patients, therapists,
/// appointments, treatment plans, encounters, etc.).
///
/// Tenant isolation is enforced at the data layer: every tenant-owned entity carries a
/// <c>ClinicId</c>, and the current clinic is resolved server-side from the authenticated
/// user — never from a caller-supplied argument. See <c>docs/multi-tenancy-design.md</c>.
/// </summary>
public class Clinic
{
    public int Id { get; set; }

    /// <summary>Display name of the clinic (e.g., "Riverside Pain &amp; Rehab").</summary>
    public string Name { get; private set; }

    /// <summary>
    /// Stable, opaque, URL-safe identifier for the clinic. Used where a clinic must appear in
    /// a public surface (token audience, subdomain, route) without exposing the numeric key.
    /// </summary>
    public string Slug { get; private set; }

    /// <summary>Whether the clinic is active. Inactive clinics retain data but cannot be used.</summary>
    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Tenant-owned navigations (Locations, Patients, Therapists, ...) are added in the stage-2
    // pass that puts ClinicId on every tenant entity. See docs/multi-tenancy-design.md.

    /// <summary>Private constructor for EF Core.</summary>
    private Clinic()
    {
        Name = string.Empty;
        Slug = string.Empty;
    }

    public Clinic(string name, string slug)
    {
        Name = name;
        Slug = slug;
    }

    public void UpdateDetails(string name, string slug)
    {
        Name = name;
        Slug = slug;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active)
    {
        if (IsActive == active) return;
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
    }
}
