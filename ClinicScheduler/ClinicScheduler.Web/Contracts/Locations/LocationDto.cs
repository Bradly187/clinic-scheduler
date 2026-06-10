namespace ClinicScheduler.Web.Contracts.Locations;

/// <summary>
/// Represents a clinic location's information returned from the API.
/// </summary>
public sealed class LocationDto
{
    /// <summary>
    /// The unique identifier for the location.
    /// </summary>
    /// <example>12345</example>
    public int Id { get; init; }
    
    /// <summary>
    /// The name of the clinic location.
    /// </summary>
    /// <example>Downtown Medical Center</example>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// The street address of the location.
    /// </summary>
    /// <example>123 Main Street</example>
    public string Address { get; init; } = string.Empty;
    
    /// <summary>
    /// The city where the location is situated.
    /// </summary>
    /// <example>Dallas</example>
    public string? City { get; init; }
    
    /// <summary>
    /// The state or province where the location is situated.
    /// </summary>
    /// <example>TX</example>
    public string? State { get; init; }
    
    /// <summary>
    /// The postal/ZIP code for the location.
    /// </summary>
    /// <example>75001</example>
    public string? ZipCode { get; init; }
    
    /// <summary>
    /// The IANA time zone identifier for the location.
    /// </summary>
    /// <example>America/Chicago</example>
    public string? TimeZone { get; init; }
    
    /// <summary>
    /// The maximum number of distinct patients this location can serve per day.
    /// </summary>
    /// <example>12</example>
    public int DailyCapacity { get; init; }

    /// <summary>
    /// Length of one appointment slot at this location, in minutes.
    /// </summary>
    /// <example>30</example>
    public int SlotDurationMinutes { get; init; }

    /// <summary>
    /// The location's configured operating windows. Empty means the default
    /// schedule applies (8:00 AM–5:00 PM, Monday through Friday).
    /// </summary>
    public IReadOnlyList<OperatingHoursDto> OperatingHours { get; init; } = [];

    /// <summary>
    /// The timestamp when the location record was created.
    /// </summary>
    /// <example>2024-01-15T10:30:00Z</example>
    public DateTime CreatedAt { get; init; }
    
    /// <summary>
    /// The timestamp when the location record was last updated.
    /// </summary>
    /// <example>2024-01-20T14:45:00Z</example>
    public DateTime UpdatedAt { get; init; }
}