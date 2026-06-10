using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;

namespace ClinicScheduler.Core.Services;

/// <summary>
/// Handles marking an appointment as missed and automatically rescheduling it
/// to the next available slot within the location's configured clinic hours.
/// </summary>
public class MissedAppointmentService
{
    private readonly IRepository<Appointment> _appointmentRepository;
    private readonly IRepository<TreatmentPlan> _treatmentPlanRepository;
    private readonly AppointmentSchedulingService _schedulingService;

    public MissedAppointmentService(
        IRepository<Appointment> appointmentRepository,
        IRepository<TreatmentPlan> treatmentPlanRepository,
        AppointmentSchedulingService schedulingService)
    {
        _appointmentRepository = appointmentRepository;
        _treatmentPlanRepository = treatmentPlanRepository;
        _schedulingService = schedulingService;
    }

    /// <summary>
    /// Marks the appointment as missed, then creates a new appointment at the next
    /// available slot for the same patient, therapist, and room.
    /// If the appointment belongs to a treatment plan, extends the plan's end date
    /// by 7 days to account for the missed session.
    /// </summary>
    /// <returns>The rescheduled appointment.</returns>
    /// <exception cref="ArgumentException">If the appointment is not found.</exception>
    /// <exception cref="InvalidOperationException">If no available slot can be found within 60 days.</exception>
    public async Task<Appointment> MarkMissedAndRescheduleAsync(int appointmentId, CancellationToken ct = default)
    {
        var appointment = await _appointmentRepository.GetByIdAsync(appointmentId, ct)
            ?? throw new ArgumentException($"Appointment {appointmentId} not found.", nameof(appointmentId));

        appointment.MarkAsMissed();
        await _appointmentRepository.UpdateAsync(appointment, ct);

        var newAppointment = await FindAndBookNextSlotAsync(appointment, ct);

        if (appointment.TreatmentPlanId.HasValue)
        {
            var plan = await _treatmentPlanRepository.GetByIdAsync(appointment.TreatmentPlanId.Value, ct);
            if (plan is not null)
            {
                plan.ExtendForMissedSession();
                await _treatmentPlanRepository.UpdateAsync(plan, ct);
            }
        }

        return newAppointment;
    }

    private async Task<Appointment> FindAndBookNextSlotAsync(Appointment missed, CancellationToken ct)
    {
        // Always reschedule at least 7 days from now so the patient has
        // reasonable notice and we never book into the past.
        var searchStart = DateTime.UtcNow.Date.AddDays(7);

        for (var day = 0; day < 60; day++)
        {
            var candidate = searchStart.AddDays(day);

            // Walk through each valid slot for the room's location on this day
            var (slotStarts, slotLength) = await _schedulingService.GetDailySlotsForRoomAsync(
                missed.RoomId, candidate, ct);

            foreach (var slotDateTime in slotStarts)
            {
                try
                {
                    var newAppointment = await _schedulingService.CreateAppointmentAsync(
                        missed.PatientId,
                        missed.TherapistId,
                        missed.RoomId,
                        slotDateTime,
                        slotLength,
                        ct);

                    newAppointment.TreatmentPlanId = missed.TreatmentPlanId;
                    newAppointment.Notes = $"Rescheduled from missed appointment on {missed.StartTime:yyyy-MM-dd HH:mm}.";
                    await _appointmentRepository.UpdateAsync(newAppointment, ct);

                    return newAppointment;
                }
                catch (InvalidOperationException)
                {
                    // Slot is taken — try the next one
                }
            }
        }

        throw new InvalidOperationException(
            "No available slot could be found within 60 days to reschedule the missed appointment.");
    }
}
