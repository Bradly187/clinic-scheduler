using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Exceptions;
using ClinicScheduler.Core.Interfaces;

namespace ClinicScheduler.Core.Services;

/// <summary>A waitlist entry that was successfully booked into an appointment.</summary>
public sealed class WaitlistFulfillment
{
    public required WaitlistEntry Entry { get; init; }
    public required Appointment Appointment { get; init; }
}

/// <summary>
/// Matches active waitlist entries to open appointment slots. Bookings go through
/// <see cref="AppointmentSchedulingService"/>, so location hours, slot duration,
/// conflicts, and daily capacity are enforced. Entries are processed first-come,
/// first-served (oldest CreatedAt first).
/// </summary>
public class WaitlistService
{
    private readonly IRepository<WaitlistEntry> _waitlistRepository;
    private readonly IRepository<Room> _roomRepository;
    private readonly IRepository<Therapist> _therapistRepository;
    private readonly AppointmentSchedulingService _schedulingService;

    public WaitlistService(
        IRepository<WaitlistEntry> waitlistRepository,
        IRepository<Room> roomRepository,
        IRepository<Therapist> therapistRepository,
        AppointmentSchedulingService schedulingService)
    {
        _waitlistRepository = waitlistRepository;
        _roomRepository = roomRepository;
        _therapistRepository = therapistRepository;
        _schedulingService = schedulingService;
    }

    /// <summary>
    /// Sweeps all active entries in FIFO order: expires entries whose window has
    /// passed, and books the first available slot for entries that match one.
    /// Returns the fulfillments made during this run.
    /// </summary>
    public async Task<IReadOnlyList<WaitlistFulfillment>> ProcessWaitlistAsync(CancellationToken ct = default)
    {
        var activeEntries = (await _waitlistRepository.FindAsync(
                w => w.Status == WaitlistStatus.Active, ct))
            .OrderBy(w => w.CreatedAt)
            .ToList();

        var fulfillments = new List<WaitlistFulfillment>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        foreach (var entry in activeEntries)
        {
            if (entry.LatestDate < today)
            {
                entry.Expire();
                await _waitlistRepository.UpdateAsync(entry, ct);
                continue;
            }

            var appointment = await TryFulfillEntryAsync(entry, ct);
            if (appointment is not null)
                fulfillments.Add(new WaitlistFulfillment { Entry = entry, Appointment = appointment });
        }

        return fulfillments;
    }

    /// <summary>
    /// Offers a just-freed slot (from a canceled or deleted appointment) to the oldest
    /// matching active entry. Much cheaper than a full sweep; call this on cancellation.
    /// Returns the fulfillment, or null when no entry matches or booking fails.
    /// </summary>
    public async Task<WaitlistFulfillment?> TryFulfillFreedSlotAsync(
        int roomId, int therapistId, DateTime slotStart, CancellationToken ct = default)
    {
        if (slotStart <= DateTime.UtcNow) return null;

        var room = await _roomRepository.GetByIdAsync(roomId, ct);
        if (room is null) return null;

        var candidates = (await _waitlistRepository.FindAsync(
                w => w.Status == WaitlistStatus.Active, ct))
            .Where(w => w.Matches(slotStart))
            .Where(w => w.LocationId is null || w.LocationId == room.LocationId)
            .Where(w => w.TherapistId is null || w.TherapistId == therapistId)
            .OrderBy(w => w.CreatedAt);

        foreach (var entry in candidates)
        {
            try
            {
                var appointment = await _schedulingService.CreateAppointmentAsync(
                    entry.PatientId, entry.TherapistId ?? therapistId, roomId, slotStart, ct);

                entry.Fulfill(appointment);
                await _waitlistRepository.UpdateAsync(entry, ct);
                return new WaitlistFulfillment { Entry = entry, Appointment = appointment };
            }
            catch (CapacityExceededException)
            {
                // The location is at capacity that day — no candidate can take the slot
                return null;
            }
            catch (InvalidOperationException)
            {
                // This candidate conflicts (e.g. patient already booked then) — try the next
            }
            catch (ArgumentException)
            {
                // Slot no longer valid for this location — nothing to offer
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Searches the entry's window for the first bookable slot and books it.
    /// Returns null when nothing in the window can be booked.
    /// </summary>
    private async Task<Appointment?> TryFulfillEntryAsync(WaitlistEntry entry, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var searchStart = entry.EarliestDate > today.AddDays(1) ? entry.EarliestDate : today.AddDays(1);

        var rooms = (await _roomRepository.FindAsync(
                r => entry.LocationId == null || r.LocationId == entry.LocationId, ct))
            .ToList();
        if (rooms.Count == 0) return null;

        List<int> therapistIds;
        if (entry.TherapistId is { } preferredTherapist)
        {
            therapistIds = [preferredTherapist];
        }
        else
        {
            // Any clinical therapist (mirror the UI's filtering of non-clinical staff)
            therapistIds = (await _therapistRepository.GetAllAsync(ct))
                .Where(t => !string.IsNullOrWhiteSpace(t.Specialty)
                    && !t.Specialty.Contains("admin", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Id)
                .ToList();
        }
        if (therapistIds.Count == 0) return null;

        for (var date = searchStart; date <= entry.LatestDate; date = date.AddDays(1))
        {
            var day = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

            foreach (var room in rooms)
            {
                var locationFullToday = false;

                foreach (var therapistId in therapistIds)
                {
                    if (locationFullToday) break;
                    
                    var (slotStarts, _) = await _schedulingService.GetDailySlotsForRoomAsync(room.Id, therapistId, day, ct);

                    foreach (var slot in slotStarts)
                    {
                        if (slot <= DateTime.UtcNow || !entry.Matches(slot)) continue;

                        try
                        {
                            var appointment = await _schedulingService.CreateAppointmentAsync(
                                entry.PatientId, therapistId, room.Id, slot, ct);

                            entry.Fulfill(appointment);
                            await _waitlistRepository.UpdateAsync(entry, ct);
                            return appointment;
                        }
                        catch (CapacityExceededException)
                        {
                            // The room's location is full for the day — skip its remaining slots
                            locationFullToday = true;
                            break;
                        }
                        catch (InvalidOperationException)
                        {
                            // Conflict — try the next slot
                        }
                        catch (ArgumentException)
                        {
                            // Slot rejected by validation — try the next slot
                            break;
                        }
                    }
                }
            }
        }

        return null;
    }
}
