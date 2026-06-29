using Hl7.Fhir.Model;

using DomainPatient = ClinicScheduler.Core.Entities.Patient;
using DomainAppointment = ClinicScheduler.Core.Entities.Appointment;
using DomainTherapist = ClinicScheduler.Core.Entities.Therapist;
using DomainLocation = ClinicScheduler.Core.Entities.Location;
using DomainAppointmentStatus = ClinicScheduler.Core.Entities.AppointmentStatus;
using FhirPatient = Hl7.Fhir.Model.Patient;
using FhirAppointment = Hl7.Fhir.Model.Appointment;
using FhirPractitioner = Hl7.Fhir.Model.Practitioner;
using FhirLocation = Hl7.Fhir.Model.Location;

namespace ClinicScheduler.Infrastructure.Ehr;

/// <summary>
/// Pure domain → FHIR R4 resource mapping. Holds no I/O, so it can be unit-tested without a
/// FHIR server. <see cref="FhirSyncService"/> owns the create/update transport; this owns shape.
/// </summary>
public static class FhirResourceMapper
{
    /// <summary>The US National Provider Identifier identifier system URI.</summary>
    public const string NpiSystem = "http://hl7.org/fhir/sid/us-npi";

    public static FhirPatient ToFhirPatient(DomainPatient patient)
    {
        var resource = new FhirPatient
        {
            Id = patient.FhirId,
            Name = { new HumanName { Family = patient.LastName, Given = new[] { patient.FirstName } } },
            BirthDate = patient.DateOfBirth.ToString("yyyy-MM-dd")
        };

        if (!string.IsNullOrWhiteSpace(patient.Email))
            resource.Telecom.Add(new ContactPoint { System = ContactPoint.ContactPointSystem.Email, Value = patient.Email });
        if (!string.IsNullOrWhiteSpace(patient.Phone))
            resource.Telecom.Add(new ContactPoint { System = ContactPoint.ContactPointSystem.Phone, Value = patient.Phone });

        return resource;
    }

    public static FhirPractitioner ToFhirPractitioner(DomainTherapist therapist)
    {
        var resource = new FhirPractitioner
        {
            Id = therapist.FhirId,
            Name = { new HumanName { Family = therapist.LastName, Given = new[] { therapist.FirstName } } }
        };

        if (!string.IsNullOrWhiteSpace(therapist.Email))
            resource.Telecom.Add(new ContactPoint { System = ContactPoint.ContactPointSystem.Email, Value = therapist.Email });
        if (!string.IsNullOrWhiteSpace(therapist.Phone))
            resource.Telecom.Add(new ContactPoint { System = ContactPoint.ContactPointSystem.Phone, Value = therapist.Phone });
        if (!string.IsNullOrWhiteSpace(therapist.NpiNumber))
            resource.Identifier.Add(new Identifier(NpiSystem, therapist.NpiNumber));

        return resource;
    }

    public static FhirLocation ToFhirLocation(DomainLocation location)
    {
        var resource = new FhirLocation
        {
            Id = location.FhirId,
            Name = location.Name
        };

        if (!string.IsNullOrWhiteSpace(location.Address) || !string.IsNullOrWhiteSpace(location.City))
        {
            resource.Address = new Address
            {
                City = location.City,
                State = location.State,
                PostalCode = location.ZipCode
            };
            if (!string.IsNullOrWhiteSpace(location.Address))
                resource.Address.Line = new[] { location.Address };
        }

        return resource;
    }

    public static FhirAppointment ToFhirAppointment(DomainAppointment appointment)
    {
        var resource = new FhirAppointment
        {
            Id = appointment.FhirId,
            Status = MapStatus(appointment.Status),
            Start = new DateTimeOffset(appointment.StartTime),
            End = new DateTimeOffset(appointment.EndTime)
        };

        if (!string.IsNullOrWhiteSpace(appointment.Notes))
            resource.Comment = appointment.Notes;

        // Attach participant references for whichever related resources have already been synced.
        AddParticipant(resource, "Patient", appointment.Patient?.FhirId);
        AddParticipant(resource, "Practitioner", appointment.Therapist?.FhirId);
        AddParticipant(resource, "Location", appointment.Room?.Location?.FhirId);

        return resource;
    }

    private static void AddParticipant(FhirAppointment appointment, string resourceType, string? fhirId)
    {
        if (string.IsNullOrWhiteSpace(fhirId)) return;
        appointment.Participant.Add(new FhirAppointment.ParticipantComponent
        {
            Actor = new ResourceReference($"{resourceType}/{fhirId}"),
            Status = ParticipationStatus.Accepted
        });
    }

    private static FhirAppointment.AppointmentStatus MapStatus(DomainAppointmentStatus status) => status switch
    {
        DomainAppointmentStatus.Scheduled => FhirAppointment.AppointmentStatus.Booked,
        DomainAppointmentStatus.Completed => FhirAppointment.AppointmentStatus.Fulfilled,
        DomainAppointmentStatus.Canceled => FhirAppointment.AppointmentStatus.Cancelled,
        DomainAppointmentStatus.Missed => FhirAppointment.AppointmentStatus.Noshow,
        _ => FhirAppointment.AppointmentStatus.Booked
    };
}
