using ClinicScheduler.Core.Entities;

namespace ClinicScheduler.Core.Interfaces;

public interface IFhirSyncService
{
    Task<string> SyncPatientAsync(Patient patient, CancellationToken ct = default);
    Task<string> SyncAppointmentAsync(Appointment appointment, CancellationToken ct = default);

    /// <summary>Pushes a therapist to the EHR as a FHIR Practitioner; returns the remote id.</summary>
    Task<string> SyncTherapistAsync(Therapist therapist, CancellationToken ct = default);

    /// <summary>Pushes a clinic location to the EHR as a FHIR Location; returns the remote id.</summary>
    Task<string> SyncLocationAsync(Location location, CancellationToken ct = default);
}
