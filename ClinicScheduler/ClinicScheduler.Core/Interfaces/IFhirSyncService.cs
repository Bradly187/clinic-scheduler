using ClinicScheduler.Core.Entities;

namespace ClinicScheduler.Core.Interfaces;

public interface IFhirSyncService
{
    Task<string> SyncPatientAsync(Patient patient, CancellationToken ct = default);
    Task<string> SyncAppointmentAsync(Appointment appointment, CancellationToken ct = default);
}
