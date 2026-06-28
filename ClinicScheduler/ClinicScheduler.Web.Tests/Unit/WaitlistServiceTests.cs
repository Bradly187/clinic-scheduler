using System.Linq.Expressions;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Services;
using FluentAssertions;
using Moq;

namespace ClinicScheduler.Web.Tests.Unit;

public class WaitlistServiceTests
{
    // Far-future Monday so weekday slot availability is deterministic
    private static readonly DateOnly WindowStart = new(2030, 6, 3);
    private static readonly DateOnly WindowEnd = new(2030, 6, 14);

    [Fact]
    public async Task ProcessWaitlist_BooksFirstAvailableSlotInWindow()
    {
        var fixture = new Fixture();
        var entry = fixture.AddEntry(WindowStart, WindowEnd);

        var fulfillments = await fixture.Sut.ProcessWaitlistAsync();

        fulfillments.Should().HaveCount(1);
        entry.Status.Should().Be(WaitlistStatus.Fulfilled);

        var appointment = fulfillments[0].Appointment;
        DateOnly.FromDateTime(appointment.StartTime).Should().BeOnOrAfter(WindowStart);
        DateOnly.FromDateTime(appointment.StartTime).Should().BeOnOrBefore(WindowEnd);
        appointment.PatientId.Should().Be(fixture.Patient.Id);
    }

    [Fact]
    public async Task ProcessWaitlist_RespectsPreferredTimeWindow()
    {
        var fixture = new Fixture();
        fixture.AddEntry(WindowStart, WindowEnd,
            timeFrom: new TimeOnly(14, 0), timeTo: new TimeOnly(16, 0));

        var fulfillments = await fixture.Sut.ProcessWaitlistAsync();

        fulfillments.Should().HaveCount(1);
        var time = TimeOnly.FromDateTime(fulfillments[0].Appointment.StartTime);
        time.Should().BeOnOrAfter(new TimeOnly(14, 0));
        time.Should().BeOnOrBefore(new TimeOnly(16, 0));
    }

    [Fact]
    public async Task ProcessWaitlist_ExpiresEntriesWhoseWindowPassed()
    {
        var fixture = new Fixture();
        var stale = fixture.AddEntry(new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));

        var fulfillments = await fixture.Sut.ProcessWaitlistAsync();

        fulfillments.Should().BeEmpty();
        stale.Status.Should().Be(WaitlistStatus.Expired);
    }

    [Fact]
    public async Task ProcessWaitlist_WeekendOnlyWindow_LeavesEntryActive()
    {
        var fixture = new Fixture();
        // 2030-06-08/09 is a Saturday/Sunday — no slots under the default schedule
        var entry = fixture.AddEntry(new DateOnly(2030, 6, 8), new DateOnly(2030, 6, 9));

        var fulfillments = await fixture.Sut.ProcessWaitlistAsync();

        fulfillments.Should().BeEmpty();
        entry.Status.Should().Be(WaitlistStatus.Active);
    }

    [Fact]
    public async Task TryFulfillFreedSlot_BooksOldestMatchingEntry()
    {
        var fixture = new Fixture();
        var older = fixture.AddEntry(WindowStart, WindowEnd);
        older.CreatedAt = DateTime.UtcNow.AddDays(-2);
        var newerPatient = new Patient("New", "Comer", "new@test.com", new DateOnly(1992, 1, 1)) { Id = 3 };
        fixture.RegisterPatient(newerPatient);
        var newer = fixture.AddEntry(WindowStart, WindowEnd, patient: newerPatient);
        newer.CreatedAt = DateTime.UtcNow.AddDays(-1);

        var freedSlot = WindowStart.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
        var fulfillment = await fixture.Sut.TryFulfillFreedSlotAsync(
            fixture.Room.Id, fixture.Therapist.Id, freedSlot);

        fulfillment.Should().NotBeNull();
        fulfillment!.Entry.Should().BeSameAs(older);
        fulfillment.Appointment.StartTime.Should().Be(freedSlot);
        newer.Status.Should().Be(WaitlistStatus.Active);
    }

    [Fact]
    public async Task TryFulfillFreedSlot_NoMatchingEntry_ReturnsNull()
    {
        var fixture = new Fixture();
        var entry = fixture.AddEntry(WindowStart, WindowEnd);

        // Freed slot is outside the entry's window
        var freedSlot = new DateTime(2030, 8, 5, 10, 0, 0, DateTimeKind.Utc);
        var fulfillment = await fixture.Sut.TryFulfillFreedSlotAsync(
            fixture.Room.Id, fixture.Therapist.Id, freedSlot);

        fulfillment.Should().BeNull();
        entry.Status.Should().Be(WaitlistStatus.Active);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Real AppointmentSchedulingService + WaitlistService over in-memory lists;
    /// FindAsync mocks compile their predicates so bookings see prior bookings.
    /// </summary>
    private sealed class Fixture
    {
        public Location Location { get; } = new("Main", "123 Main St") { Id = 1 };
        public Patient Patient { get; }
        public Therapist Therapist { get; }
        public Room Room { get; }
        public List<Appointment> Appointments { get; } = [];
        public List<WaitlistEntry> Entries { get; } = [];
        public WaitlistService Sut { get; }

        private readonly Mock<IRepository<Patient>> _patientRepo = new();

        public Fixture()
        {
            Patient = new Patient("Pat", "Ient", "pat@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
            Therapist = new Therapist("Dr", "Therapist", "dr@clinic.com", "555-0100", "Physical Therapy") { Id = 1 };
            Room = new Room("Room1", 1, Location) { Id = 1 };

            var apptRepo = new Mock<IRepository<Appointment>>();
            apptRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Appointment, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Expression<Func<Appointment, bool>> pred, CancellationToken _) =>
                    Appointments.Where(pred.Compile()).ToList());
            apptRepo.Setup(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Appointment a, CancellationToken _) =>
                {
                    Appointments.Add(a);
                    return a;
                });
            apptRepo.Setup(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _patientRepo.Setup(r => r.GetByIdAsync(Patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Patient);

            var therapistRepo = new Mock<IRepository<Therapist>>();
            therapistRepo.Setup(r => r.GetByIdAsync(Therapist.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Therapist);
            therapistRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Therapist> { Therapist });

            var roomRepo = new Mock<IRepository<Room>>();
            roomRepo.Setup(r => r.GetByIdAsync(Room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Room);
            roomRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Room, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Expression<Func<Room, bool>> pred, CancellationToken _) =>
                    new List<Room> { Room }.Where(pred.Compile()).ToList());

            var timeSlotRepo = new Mock<IRepository<TimeSlot>>();
            timeSlotRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TimeSlot, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<TimeSlot>());

            var locationRepo = new Mock<IRepository<Location>>();
            locationRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Location);


            var therapistShiftRepo = new Mock<IRepository<TherapistShift>>();
            therapistShiftRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TherapistShift, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<TherapistShift>());

            var fhirSyncMock = new Mock<IFhirSyncService>();
            
            var schedulingService = new AppointmentSchedulingService(
                apptRepo.Object, _patientRepo.Object, therapistRepo.Object, roomRepo.Object,
                timeSlotRepo.Object, locationRepo.Object, therapistShiftRepo.Object, fhirSyncMock.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<AppointmentSchedulingService>.Instance);

            var waitlistRepo = new Mock<IRepository<WaitlistEntry>>();
            waitlistRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<WaitlistEntry, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Expression<Func<WaitlistEntry, bool>> pred, CancellationToken _) =>
                    Entries.Where(pred.Compile()).ToList());
            waitlistRepo.Setup(r => r.UpdateAsync(It.IsAny<WaitlistEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Sut = new WaitlistService(
                waitlistRepo.Object, roomRepo.Object, therapistRepo.Object, schedulingService);
        }

        public void RegisterPatient(Patient patient) =>
            _patientRepo.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        public WaitlistEntry AddEntry(
            DateOnly earliest, DateOnly latest,
            TimeOnly? timeFrom = null, TimeOnly? timeTo = null,
            Patient? patient = null)
        {
            var entry = new WaitlistEntry(patient ?? Patient, earliest, latest,
                preferredTimeFrom: timeFrom, preferredTimeTo: timeTo);
            Entries.Add(entry);
            return entry;
        }
    }
}
