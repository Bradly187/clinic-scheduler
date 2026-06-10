namespace ClinicScheduler.Core.Entities;

/// <summary>
/// Represents a physical clinic location.
/// </summary>
public class Location
{
    public int Id { get; set; }
    
    public string Name { get; private set; }
    public string Address { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? ZipCode { get; private set; }
    
    /// <summary>
    /// The IANA Time Zone ID for this location (e.g., "America/Chicago").
    /// Helps in correctly scheduling appointments across different regions.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// The maximum number of patients this location can serve in a single day.
    /// Defaults to 12.
    /// </summary>
    public int DailyCapacity { get; private set; } = 12;

    /// <summary>
    /// Length of one appointment slot at this location, in minutes. Defaults to 30.
    /// Appointments must be exactly this long and start on a slot boundary measured
    /// from the start of the location's operating window.
    /// </summary>
    public int SlotDurationMinutes { get; private set; } = DefaultSlotDurationMinutes;

    /// <summary>Default slot length applied to new locations.</summary>
    public const int DefaultSlotDurationMinutes = 30;

    /// <summary>Shortest slot length a location may configure, in minutes.</summary>
    public const int MinSlotDurationMinutes = 5;

    /// <summary>Longest slot length a location may configure, in minutes.</summary>
    public const int MaxSlotDurationMinutes = 240;

    /// <summary><see cref="SlotDurationMinutes"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan SlotDuration => TimeSpan.FromMinutes(SlotDurationMinutes);

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Room> Rooms { get; set; } = [];

    /// <summary>
    /// Navigation property for the configurable time slots at this location.
    /// </summary>
    public ICollection<TimeSlot> TimeSlots { get; set; } = [];

    /// <summary>
    /// Private constructor for EF Core.
    /// </summary>
    private Location()
    {
        Name = string.Empty;
        Address = string.Empty;
    }

    public Location(string name, string address)
    {
        Name = name;
        Address = address;
    }

    public void UpdateAddress(string address, string? city, string? state, string? zipCode)
    {
        Address = address;
        City = city;
        State = state;
        ZipCode = zipCode;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDetails(string name, string address, string? city, string? state, string? zipCode, string? timeZone)
    {
        Name = name;
        Address = address;
        City = city;
        State = state;
        ZipCode = zipCode;
        TimeZone = timeZone;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the appointment slot length for this location.
    /// </summary>
    /// <param name="minutes">Slot length in minutes, between <see cref="MinSlotDurationMinutes"/> and <see cref="MaxSlotDurationMinutes"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minutes"/> is outside the allowed range.</exception>
    public void SetSlotDuration(int minutes)
    {
        if (minutes is < MinSlotDurationMinutes or > MaxSlotDurationMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes),
                $"Slot duration must be between {MinSlotDurationMinutes} and {MaxSlotDurationMinutes} minutes.");
        }

        SlotDurationMinutes = minutes;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the daily patient capacity for this location.
    /// </summary>
    /// <param name="capacity">The maximum number of patients per day. Must be greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is not a positive integer.</exception>
    public void SetDailyCapacity(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Daily capacity must be a positive integer.");
        }

        DailyCapacity = capacity;
        UpdatedAt = DateTime.UtcNow;
    }
}