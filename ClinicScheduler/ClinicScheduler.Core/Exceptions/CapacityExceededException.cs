namespace ClinicScheduler.Core.Exceptions;

/// <summary>
/// Thrown when booking would exceed a location's daily patient capacity.
/// Derives from <see cref="InvalidOperationException"/> so generic conflict
/// handling (HTTP 409) continues to apply; catch this type specifically when
/// probing for open slots, since the whole day is full — not just one slot.
/// </summary>
public class CapacityExceededException : InvalidOperationException
{
    public CapacityExceededException(string message) : base(message) { }
}
