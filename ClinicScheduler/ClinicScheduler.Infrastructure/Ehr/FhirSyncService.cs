using ClinicScheduler.Core.Configuration;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using DomainPatient = ClinicScheduler.Core.Entities.Patient;
using DomainAppointment = ClinicScheduler.Core.Entities.Appointment;
using FhirPatient = Hl7.Fhir.Model.Patient;
using FhirAppointment = Hl7.Fhir.Model.Appointment;
using Task = System.Threading.Tasks.Task;

namespace ClinicScheduler.Infrastructure.Ehr;

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

    public async Task<string> SyncPatientAsync(DomainPatient patient, CancellationToken ct = default)
    {
        if (_fhirClient == null)
        {
            _logger.LogWarning("FHIR Sync skipped: BaseUrl is not configured.");
            return patient.FhirId ?? string.Empty;
        }

        try
        {
            var fhirPatient = new FhirPatient
            {
                Id = patient.FhirId,
                Name = new List<HumanName>
                {
                    new HumanName { Family = patient.LastName, Given = new[] { patient.FirstName } }
                },
                BirthDate = patient.DateOfBirth.ToString("yyyy-MM-dd")
            };

            if (!string.IsNullOrWhiteSpace(patient.Email))
            {
                fhirPatient.Telecom.Add(new ContactPoint { System = ContactPoint.ContactPointSystem.Email, Value = patient.Email });
            }

            if (!string.IsNullOrWhiteSpace(patient.Phone))
            {
                fhirPatient.Telecom.Add(new ContactPoint { System = ContactPoint.ContactPointSystem.Phone, Value = patient.Phone });
            }

            if (string.IsNullOrEmpty(patient.FhirId))
            {
                var created = await _fhirClient.CreateAsync(fhirPatient);
                _logger.LogInformation("Successfully created FHIR Patient {Id}", created.Id);
                return created.Id;
            }
            else
            {
                var updated = await _fhirClient.UpdateAsync(fhirPatient);
                _logger.LogInformation("Successfully updated FHIR Patient {Id}", updated.Id);
                return updated.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync Patient {Id} to FHIR.", patient.Id);
            return patient.FhirId ?? string.Empty;
        }
    }

    public async Task<string> SyncAppointmentAsync(DomainAppointment appointment, CancellationToken ct = default)
    {
        if (_fhirClient == null)
        {
            _logger.LogWarning("FHIR Sync skipped: BaseUrl is not configured.");
            return appointment.FhirId ?? string.Empty;
        }

        try
        {
            var fhirAppointment = new FhirAppointment
            {
                Id = appointment.FhirId,
                Status = appointment.Status switch
                {
                    AppointmentStatus.Scheduled => FhirAppointment.AppointmentStatus.Booked,
                    AppointmentStatus.Completed => FhirAppointment.AppointmentStatus.Fulfilled,
                    AppointmentStatus.Canceled => FhirAppointment.AppointmentStatus.Cancelled,
                    AppointmentStatus.Missed => FhirAppointment.AppointmentStatus.Noshow,
                    _ => FhirAppointment.AppointmentStatus.Booked
                },
                Start = new DateTimeOffset(appointment.StartTime),
                End = new DateTimeOffset(appointment.EndTime)
            };

            if (!string.IsNullOrWhiteSpace(appointment.Notes))
            {
                fhirAppointment.Comment = appointment.Notes;
            }

            // In a real implementation we would attach Participant references (Patient, Practitioner, Location)
            if (appointment.Patient?.FhirId != null)
            {
                fhirAppointment.Participant.Add(new FhirAppointment.ParticipantComponent
                {
                    Actor = new ResourceReference($"Patient/{appointment.Patient.FhirId}"),
                    Status = ParticipationStatus.Accepted
                });
            }

            if (string.IsNullOrEmpty(appointment.FhirId))
            {
                var created = await _fhirClient.CreateAsync(fhirAppointment);
                _logger.LogInformation("Successfully created FHIR Appointment {Id}", created.Id);
                return created.Id;
            }
            else
            {
                var updated = await _fhirClient.UpdateAsync(fhirAppointment);
                _logger.LogInformation("Successfully updated FHIR Appointment {Id}", updated.Id);
                return updated.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync Appointment {Id} to FHIR.", appointment.Id);
            return appointment.FhirId ?? string.Empty;
        }
    }
}
