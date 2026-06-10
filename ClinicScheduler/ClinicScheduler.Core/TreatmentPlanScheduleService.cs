using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;

namespace ClinicScheduler.Core.Services;

/// <summary>
/// Outcome of generating the appointment series for a treatment plan.
/// Generation is best-effort: fully booked clinics can leave sessions unbooked,
/// which is reported rather than thrown.
/// </summary>
public sealed class TreatmentPlanScheduleResult
{
    /// <summary>The appointments created by this generation run, in chronological order.</summary>
    public required IReadOnlyList<Appointment> Created { get; init; }

    /// <summary>Sessions the plan still needed when generation started.</summary>
    public required int SessionsRequested { get; init; }

    /// <summary>Sessions successfully booked in this run.</summary>
    public int SessionsBooked => Created.Count;

    /// <summary>Sessions that could not be placed within the search horizon.</summary>
    public int SessionsUnbooked => SessionsRequested - SessionsBooked;
}

/// <summary>
/// Generates the recurring appointment series for a treatment plan: FrequencyPerWeek
/// sessions per week until the plan's TotalDays sessions are booked. Bookings go
/// through <see cref="AppointmentSchedulingService"/>, so location hours, slot
/// duration, conflicts, and daily capacity are all enforced. Prefers the requested
/// days and time, falling back to the nearest slot on the same day, then to other
/// days in the same week.
/// </summary>
public class TreatmentPlanScheduleService
{
    private readonly IRepository<TreatmentPlan> _treatmentPlanRepository;
    private readonly IRepository<Appointment> _appointmentRepository;
    private readonly AppointmentSchedulingService _schedulingService;

    public TreatmentPlanScheduleService(
        IRepository<TreatmentPlan> treatmentPlanRepository,
        IRepository<Appointment> appointmentRepository,
        AppointmentSchedulingService schedulingService)
    {
        _treatmentPlanRepository = treatmentPlanRepository;
        _appointmentRepository = appointmentRepository;
        _schedulingService = schedulingService;
    }

    /// <summary>Default weekday patterns by sessions-per-week.</summary>
    public static IReadOnlyList<DayOfWeek> DefaultDaysFor(int frequencyPerWeek) => frequencyPerWeek switch
    {
        2 => [DayOfWeek.Monday, DayOfWeek.Thursday],
        3 => [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
        4 => [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        _ => throw new ArgumentOutOfRangeException(nameof(frequencyPerWeek), "Frequency must be 2, 3, or 4.")
    };

    /// <summary>
    /// Books the plan's remaining sessions (TotalDays minus active appointments already
    /// linked to the plan), starting from the later of the plan start date and tomorrow.
    /// </summary>
    /// <param name="treatmentPlanId">The plan to generate appointments for.</param>
    /// <param name="roomId">The room to book sessions in; its location determines hours and slot length.</param>
    /// <param name="preferredTime">Time of day to aim for; the nearest valid slot is used.</param>
    /// <param name="preferredDays">
    /// Days of week to book on; must contain exactly FrequencyPerWeek distinct days.
    /// Null uses the default pattern for the plan's frequency.
    /// </param>
    /// <exception cref="ArgumentException">Plan not found, or preferred days don't match the plan frequency.</exception>
    /// <exception cref="InvalidOperationException">Plan is not active, or already fully booked.</exception>
    public async Task<TreatmentPlanScheduleResult> GenerateAppointmentsAsync(
        int treatmentPlanId,
        int roomId,
        TimeOnly preferredTime,
        IReadOnlyCollection<DayOfWeek>? preferredDays = null,
        CancellationToken ct = default)
    {
        var plan = await _treatmentPlanRepository.GetByIdAsync(treatmentPlanId, ct)
            ?? throw new ArgumentException("Treatment plan not found.", nameof(treatmentPlanId));

        if (plan.Status != TreatmentPlanStatus.Active)
            throw new InvalidOperationException("Appointments can only be generated for an active treatment plan.");

        var targetDays = preferredDays is null
            ? DefaultDaysFor(plan.FrequencyPerWeek)
            : preferredDays.Distinct().ToList();

        if (targetDays.Count != plan.FrequencyPerWeek)
            throw new ArgumentException(
                $"Preferred days must contain exactly {plan.FrequencyPerWeek} distinct days to match the plan's frequency.",
                nameof(preferredDays));

        // Sessions already booked against this plan count toward TotalDays
        var existing = await _appointmentRepository.FindAsync(a =>
            a.TreatmentPlanId == treatmentPlanId &&
            a.Status != AppointmentStatus.Canceled &&
            a.Status != AppointmentStatus.Missed, ct);

        var remaining = plan.TotalDays - existing.Count;
        if (remaining <= 0)
            throw new InvalidOperationException(
                $"This treatment plan already has all {plan.TotalDays} sessions booked.");

        var sessionsRequested = remaining;

        // Never book into the past; give at least a day's notice
        var planStart = DateTime.SpecifyKind(plan.StartDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var earliest = DateTime.UtcNow.Date.AddDays(1);
        var searchStart = planStart > earliest ? planStart : earliest;

        // Search up to twice the nominal plan length plus a buffer, so a busy clinic
        // can still place sessions without the loop running unbounded
        var weeksNeeded = (int)Math.Ceiling((double)remaining / plan.FrequencyPerWeek);
        var horizonWeeks = weeksNeeded * 2 + 8;

        var created = new List<Appointment>();
        var weekStart = StartOfWeek(searchStart);

        for (var week = 0; week < horizonWeeks && remaining > 0; week++)
        {
            var thisWeek = weekStart.AddDays(week * 7);
            var sessionsThisWeek = Math.Min(plan.FrequencyPerWeek, remaining);

            // Preferred days first, then the rest of the week as fallback, each day at most once
            var candidateDays = Enumerable.Range(0, 7)
                .Select(offset => thisWeek.AddDays(offset))
                .Where(d => d >= searchStart)
                .OrderBy(d => targetDays.Contains(d.DayOfWeek) ? 0 : 1)
                .ThenBy(d => d);

            foreach (var day in candidateDays)
            {
                if (sessionsThisWeek == 0) break;

                var booked = await TryBookSessionAsync(plan, roomId, day, preferredTime, ct);
                if (booked is null) continue;

                created.Add(booked);
                sessionsThisWeek--;
                remaining--;
            }
        }

        return new TreatmentPlanScheduleResult
        {
            Created = created.OrderBy(a => a.StartTime).ToList(),
            SessionsRequested = sessionsRequested
        };
    }

    /// <summary>
    /// Attempts to book one session on the given day, trying the slot nearest the
    /// preferred time first. Returns null when no slot on the day can be booked.
    /// </summary>
    private async Task<Appointment?> TryBookSessionAsync(
        TreatmentPlan plan, int roomId, DateTime day, TimeOnly preferredTime, CancellationToken ct)
    {
        var (slotStarts, _) = await _schedulingService.GetDailySlotsForRoomAsync(roomId, day, ct);
        if (slotStarts.Count == 0) return null;

        var orderedSlots = slotStarts
            .Where(s => s > DateTime.UtcNow)
            .OrderBy(s => Math.Abs((s.TimeOfDay - preferredTime.ToTimeSpan()).TotalMinutes))
            .ThenBy(s => s);

        foreach (var slot in orderedSlots)
        {
            try
            {
                var appointment = await _schedulingService.CreateAppointmentAsync(
                    plan.PatientId, plan.TherapistId, roomId, slot, ct);

                appointment.TreatmentPlanId = plan.Id;
                await _appointmentRepository.UpdateAsync(appointment, ct);
                return appointment;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("capacity", StringComparison.OrdinalIgnoreCase))
            {
                // The whole day is at capacity — no point trying other slots
                return null;
            }
            catch (InvalidOperationException)
            {
                // Slot conflict — try the next-nearest slot
            }
            catch (ArgumentException)
            {
                // Slot rejected by validation — try the next-nearest slot
            }
        }

        return null;
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        // Monday-based weeks
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-offset);
    }
}
