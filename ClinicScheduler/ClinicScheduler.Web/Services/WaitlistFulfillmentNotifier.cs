using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// Sends in-app notifications (and email when configured) to patients whose
/// waitlist entries were just booked into appointments.
/// </summary>
public sealed class WaitlistFulfillmentNotifier(
    ClinicDbContext db,
    IClinicEmailSender emailSender,
    ILogger<WaitlistFulfillmentNotifier> logger)
{
    /// <summary>Notifies each fulfilled entry's patient about the booked appointment.</summary>
    public async Task NotifyAsync(IReadOnlyList<WaitlistFulfillment> fulfillments, CancellationToken ct = default)
    {
        if (fulfillments.Count == 0) return;

        foreach (var fulfillment in fulfillments)
        {
            var appointment = fulfillment.Appointment;
            var patientEmail = appointment.Patient.Email;
            var local = appointment.StartTime.ToLocalTime();
            var message =
                $"A slot opened up! Your appointment with {appointment.Therapist?.FullName} is booked for " +
                $"{local:ddd, MMM d} at {local:h:mm tt}.";

            var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == patientEmail, ct);
            if (user is not null)
            {
                db.Notifications.Add(new Notification(
                    user.Id,
                    NotificationType.WaitlistFulfilled,
                    "Waitlist: Appointment Booked",
                    message,
                    appointment.Id));
            }

            if (emailSender.IsConfigured && !string.IsNullOrWhiteSpace(patientEmail))
            {
                try
                {
                    await emailSender.SendAsync(patientEmail, "Waitlist: Appointment Booked", message, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to email waitlist fulfillment for appointment {AppointmentId}",
                        appointment.Id);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
