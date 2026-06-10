using System.ComponentModel.DataAnnotations;

namespace ClinicScheduler.Web.Contracts.Locations;

/// <summary>
/// Request model for updating an existing clinic location's information.
/// </summary>
public sealed class UpdateLocationRequest
{
    /// <summary>
    /// The updated name of the clinic location.
    /// </summary>
    /// <example>Downtown Medical Center</example>
    [Required]
    [StringLength(150)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The updated street address of the location.
    /// </summary>
    /// <example>123 Main Street</example>
    [Required]
    [StringLength(250)]
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// The updated city where the location is situated (optional).
    /// </summary>
    /// <example>Dallas</example>
    [StringLength(100)]
    public string? City { get; init; }

    /// <summary>
    /// The updated state or province where the location is situated (optional).
    /// </summary>
    /// <example>TX</example>
    [StringLength(3)]
    public string? State { get; init; }

    /// <summary>
    /// The updated postal/ZIP code for the location (optional).
    /// </summary>
    /// <example>75001</example>
    [StringLength(20)]
    public string? ZipCode { get; init; }

    /// <summary>
    /// The updated IANA time zone identifier for the location (optional).
    /// </summary>
    /// <example>America/Chicago</example>
    [StringLength(100)]
    public string? TimeZone { get; init; }

    /// <summary>
    /// Updated slot length in minutes (optional; null leaves the current value unchanged).
    /// </summary>
    /// <example>45</example>
    [Range(5, 240)]
    public int? SlotDurationMinutes { get; init; }

    /// <summary>
    /// Updated operating windows (optional). Null leaves the current configuration
    /// unchanged; an empty list clears it, restoring the default 8:00 AM–5:00 PM
    /// weekday schedule.
    /// </summary>
    public List<OperatingHoursDto>? OperatingHours { get; init; }
}