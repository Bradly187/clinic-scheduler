namespace ClinicScheduler.Core.Entities;

/// <summary>
/// Represents a configurable scheduling window for a specific therapist at a specific location,
/// defined by a start time, end time, and day of week.
/// </summary>
public class TherapistShift
{
    public int Id { get; set; }

    /// <summary>
    /// The start time of the scheduling window.
    /// </summary>
    public TimeOnly StartTime { get; private set; }

    /// <summary>
    /// The end time of the scheduling window. Must be later than <see cref="StartTime"/>.
    /// </summary>
    public TimeOnly EndTime { get; private set; }

    /// <summary>
    /// The day of the week this shift applies to (Sunday = 0 through Saturday = 6).
    /// </summary>
    public DayOfWeek DayOfWeek { get; private set; }

    /// <summary>
    /// Foreign key reference to the <see cref="Entities.Therapist"/> this shift belongs to.
    /// </summary>
    public int TherapistId { get; private set; }

    /// <summary>
    /// Navigation property to the parent <see cref="Entities.Therapist"/>.
    /// </summary>
    public Therapist Therapist { get; private set; } = null!;

    /// <summary>
    /// Foreign key reference to the <see cref="Entities.Location"/> this shift occurs at.
    /// </summary>
    public int LocationId { get; private set; }

    /// <summary>
    /// Navigation property to the <see cref="Entities.Location"/>.
    /// </summary>
    public Location Location { get; private set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Private constructor for EF Core.
    /// </summary>
    private TherapistShift() { }

    /// <summary>
    /// Creates a new <see cref="TherapistShift"/> with validated inputs.
    /// </summary>
    public TherapistShift(TimeOnly startTime, TimeOnly endTime, DayOfWeek dayOfWeek, Therapist therapist, Location location)
    {
        if (startTime >= endTime)
        {
            throw new ArgumentException("Start time must be earlier than end time.", nameof(startTime));
        }

        if (!Enum.IsDefined(dayOfWeek))
        {
            throw new ArgumentOutOfRangeException(nameof(dayOfWeek), "Day of week must be between Sunday (0) and Saturday (6).");
        }

        StartTime = startTime;
        EndTime = endTime;
        DayOfWeek = dayOfWeek;
        Therapist = therapist;
        TherapistId = therapist.Id;
        Location = location;
        LocationId = location.Id;
    }
}
