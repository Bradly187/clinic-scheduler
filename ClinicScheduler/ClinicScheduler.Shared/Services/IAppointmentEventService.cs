namespace ClinicScheduler.Shared.Services;

public interface IAppointmentEventService
{
    /// <summary>
    /// Triggered when an appointment is booked, canceled, or rescheduled anywhere in the application.
    /// </summary>
    event Action? OnAppointmentsChanged;

    /// <summary>
    /// Notifies all subscribers that the appointments have changed.
    /// </summary>
    void NotifyAppointmentsChanged();
}

public class AppointmentEventService : IAppointmentEventService
{
    public event Action? OnAppointmentsChanged;

    public void NotifyAppointmentsChanged()
    {
        OnAppointmentsChanged?.Invoke();
    }
}
