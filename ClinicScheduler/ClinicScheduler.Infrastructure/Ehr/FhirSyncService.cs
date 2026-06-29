using ClinicScheduler.Core.Configuration;
using ClinicScheduler.Core.Interfaces;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using DomainPatient = ClinicScheduler.Core.Entities.Patient;
using DomainAppointment = ClinicScheduler.Core.Entities.Appointment;
using DomainTherapist = ClinicScheduler.Core.Entities.Therapist;
using DomainLocation = ClinicScheduler.Core.Entities.Location;
using Task = System.Threading.Tasks.Task;

namespace ClinicScheduler.Infrastructure.Ehr;

/// <summary>
/// Pushes domain entities to an external FHIR R4 server. Mapping (domain → resource) lives in
/// <see cref="FhirResourceMapper"/>; this class owns transport: the lazily-built client and the
/// create-or-update decision. When <c>FhirSettings.BaseUrl</c> is unset it is a safe no-op.
/// </summary>
public class FhirSyncService : IFhirSyncService
{
    private readonly FhirSettings _settings;
    private readonly ILogger<FhirSyncService> _logger;
    private readonly FhirClient? _fhirClient;

    public FhirSyncService(IOptions<FhirSettings> options, ILogger<FhirSyncService> logger)
    {
        _settings = options.Value;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            var settings = new FhirClientSettings
            {
                PreferredFormat = ResourceFormat.Json,
                VerifyFhirVersion = true
            };

            _fhirClient = new FhirClient(_settings.BaseUrl, settings);

            if (!string.IsNullOrWhiteSpace(_settings.AuthToken))
            {
                _fhirClient.RequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.AuthToken);
            }
        }
    }

    public Task<string> SyncPatientAsync(DomainPatient patient, CancellationToken ct = default)
        => SyncAsync(FhirResourceMapper.ToFhirPatient(patient), patient.FhirId, "Patient", patient.Id);

    public Task<string> SyncAppointmentAsync(DomainAppointment appointment, CancellationToken ct = default)
        => SyncAsync(FhirResourceMapper.ToFhirAppointment(appointment), appointment.FhirId, "Appointment", appointment.Id);

    public Task<string> SyncTherapistAsync(DomainTherapist therapist, CancellationToken ct = default)
        => SyncAsync(FhirResourceMapper.ToFhirPractitioner(therapist), therapist.FhirId, "Practitioner", therapist.Id);

    public Task<string> SyncLocationAsync(DomainLocation location, CancellationToken ct = default)
        => SyncAsync(FhirResourceMapper.ToFhirLocation(location), location.FhirId, "Location", location.Id);

    /// <summary>
    /// Creates the resource if it has no remote id yet, otherwise updates it. Returns the remote id,
    /// or the existing local <paramref name="existingFhirId"/> when the server is unconfigured or the
    /// call fails (so a sync failure never breaks the domain operation that triggered it).
    /// </summary>
    private async Task<string> SyncAsync(Resource resource, string? existingFhirId, string resourceType, int localId)
    {
        if (_fhirClient == null)
        {
            _logger.LogWarning("FHIR Sync skipped: BaseUrl is not configured.");
            return existingFhirId ?? string.Empty;
        }

        try
        {
            if (string.IsNullOrEmpty(existingFhirId))
            {
                var created = await _fhirClient.CreateAsync(resource);
                _logger.LogInformation("Successfully created FHIR {ResourceType} {Id}", resourceType, created.Id);
                return created.Id;
            }

            var updated = await _fhirClient.UpdateAsync(resource);
            _logger.LogInformation("Successfully updated FHIR {ResourceType} {Id}", resourceType, updated.Id);
            return updated.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync {ResourceType} {Id} to FHIR.", resourceType, localId);
            return existingFhirId ?? string.Empty;
        }
    }
}
