using ClinicScheduler.Core.Entities;
using ClinicScheduler.Infrastructure.Ehr;
using FluentAssertions;
using Xunit;
using FhirAppointment = Hl7.Fhir.Model.Appointment;
using FhirContactPoint = Hl7.Fhir.Model.ContactPoint;
using FhirEncounter = Hl7.Fhir.Model.Encounter;

namespace ClinicScheduler.Web.Tests.Unit;

/// <summary>
/// Pure domain → FHIR R4 mapping tests — no FHIR server. Locks in the Sprint 3 broadening
/// (Practitioner, Location) and the appointment participant references.
/// </summary>
public class FhirResourceMapperTests
{
    [Fact]
    public void ToFhirPatient_MapsNameBirthdateAndTelecom()
    {
        var patient = new Patient("John", "Doe", "john@test.com", new DateOnly(1990, 1, 2), "555-1234");
        patient.SetFhirId("p1");

        var fhir = FhirResourceMapper.ToFhirPatient(patient);

        fhir.Id.Should().Be("p1");
        fhir.Name.Should().ContainSingle();
        fhir.Name[0].Family.Should().Be("Doe");
        fhir.Name[0].Given.Should().ContainSingle().Which.Should().Be("John");
        fhir.BirthDate.Should().Be("1990-01-02");
        fhir.Telecom.Should().Contain(t => t.System == FhirContactPoint.ContactPointSystem.Email && t.Value == "john@test.com");
        fhir.Telecom.Should().Contain(t => t.System == FhirContactPoint.ContactPointSystem.Phone && t.Value == "555-1234");
    }

    [Fact]
    public void ToFhirPractitioner_MapsNameTelecomAndNpiIdentifier()
    {
        var therapist = new Therapist("Jane", "Smith", "jane@test.com", "555-2222", "Physical Therapy", "1234567890");
        therapist.SetFhirId("t1");

        var fhir = FhirResourceMapper.ToFhirPractitioner(therapist);

        fhir.Id.Should().Be("t1");
        fhir.Name[0].Family.Should().Be("Smith");
        fhir.Telecom.Should().Contain(t => t.Value == "jane@test.com");
        fhir.Identifier.Should().ContainSingle()
            .Which.Should().Match<Hl7.Fhir.Model.Identifier>(i =>
                i.System == FhirResourceMapper.NpiSystem && i.Value == "1234567890");
    }

    [Fact]
    public void ToFhirPractitioner_OmitsNpiIdentifier_WhenAbsent()
    {
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");

        var fhir = FhirResourceMapper.ToFhirPractitioner(therapist);

        fhir.Identifier.Should().BeEmpty();
    }

    [Fact]
    public void ToFhirLocation_MapsNameAndAddress()
    {
        var location = new Location("Main Clinic", "123 Main St");
        location.UpdateDetails("Main Clinic", "123 Main St", "Springfield", "IL", "62701", "America/Chicago");
        location.SetFhirId("l1");

        var fhir = FhirResourceMapper.ToFhirLocation(location);

        fhir.Id.Should().Be("l1");
        fhir.Name.Should().Be("Main Clinic");
        fhir.Address.City.Should().Be("Springfield");
        fhir.Address.State.Should().Be("IL");
        fhir.Address.PostalCode.Should().Be("62701");
        fhir.Address.Line.Should().ContainSingle().Which.Should().Be("123 Main St");
    }

    [Fact]
    public void ToFhirAppointment_MapsStatusTimesAndAllParticipants_WhenRelatedResourcesSynced()
    {
        var (appointment, _) = BuildAppointment(patientFhir: "p1", therapistFhir: "t1", locationFhir: "l1");

        var fhir = FhirResourceMapper.ToFhirAppointment(appointment);

        fhir.Status.Should().Be(FhirAppointment.AppointmentStatus.Booked);
        fhir.Start.Should().NotBeNull();
        fhir.End.Should().NotBeNull();

        var references = fhir.Participant.Select(p => p.Actor.Reference).ToList();
        references.Should().BeEquivalentTo("Patient/p1", "Practitioner/t1", "Location/l1");
    }

    [Fact]
    public void ToFhirAppointment_OmitsParticipants_WhenRelatedResourcesNotSynced()
    {
        var (appointment, _) = BuildAppointment(patientFhir: null, therapistFhir: null, locationFhir: null);

        var fhir = FhirResourceMapper.ToFhirAppointment(appointment);

        fhir.Participant.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled, FhirAppointment.AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.Completed, FhirAppointment.AppointmentStatus.Fulfilled)]
    [InlineData(AppointmentStatus.Canceled, FhirAppointment.AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Missed, FhirAppointment.AppointmentStatus.Noshow)]
    public void ToFhirAppointment_MapsStatus(AppointmentStatus domain, FhirAppointment.AppointmentStatus expected)
    {
        var (appointment, _) = BuildAppointment(patientFhir: "p1", therapistFhir: "t1", locationFhir: "l1");
        DriveToStatus(appointment, domain);

        FhirResourceMapper.ToFhirAppointment(appointment).Status.Should().Be(expected);
    }

    [Fact]
    public void ToFhirEncounter_MapsStatusClassReasonAndReferences()
    {
        var patient = new Patient("John", "Doe", "john@test.com", new DateOnly(1990, 1, 1));
        patient.SetFhirId("p1");
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        therapist.SetFhirId("t1");
        var location = new Location("Main Clinic", "123 Main St");
        location.SetFhirId("l1");

        var encounter = new Encounter(patient, DateTime.UtcNow, "Back pain", therapist, location);
        encounter.SetFhirId("e1");

        var fhir = FhirResourceMapper.ToFhirEncounter(encounter);

        fhir.Id.Should().Be("e1");
        fhir.Status.Should().Be(FhirEncounter.EncounterStatus.Planned);
        fhir.Class.Code.Should().Be("AMB");
        fhir.Subject!.Reference.Should().Be("Patient/p1");
        fhir.Participant.Should().ContainSingle().Which.Individual!.Reference.Should().Be("Practitioner/t1");
        fhir.Location.Should().ContainSingle().Which.Location!.Reference.Should().Be("Location/l1");
        fhir.ReasonCode.Should().ContainSingle().Which.Text.Should().Be("Back pain");
        fhir.Period.Start.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ToFhirEncounter_OmitsReferences_WhenRelatedResourcesNotSynced()
    {
        var patient = new Patient("John", "Doe", "john@test.com", new DateOnly(1990, 1, 1));
        var encounter = new Encounter(patient, DateTime.UtcNow);

        var fhir = FhirResourceMapper.ToFhirEncounter(encounter);

        fhir.Subject.Should().BeNull();
        fhir.Participant.Should().BeEmpty();
        fhir.Location.Should().BeEmpty();
        fhir.ReasonCode.Should().BeEmpty();
    }

    private static (Appointment, Location) BuildAppointment(string? patientFhir, string? therapistFhir, string? locationFhir)
    {
        var patient = new Patient("John", "Doe", "john@test.com", new DateOnly(1990, 1, 1));
        if (patientFhir != null) patient.SetFhirId(patientFhir);

        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        if (therapistFhir != null) therapist.SetFhirId(therapistFhir);

        var location = new Location("Main Clinic", "123 Main St");
        if (locationFhir != null) location.SetFhirId(locationFhir);

        var room = new Room("Room 1", 1, location);
        var appointment = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1));
        return (appointment, location);
    }

    private static void DriveToStatus(Appointment appointment, AppointmentStatus status)
    {
        switch (status)
        {
            case AppointmentStatus.Completed: appointment.Complete(); break;
            case AppointmentStatus.Canceled: appointment.Cancel(); break;
            case AppointmentStatus.Missed: appointment.MarkAsMissed(); break;
            case AppointmentStatus.Scheduled: break;
        }
    }
}
