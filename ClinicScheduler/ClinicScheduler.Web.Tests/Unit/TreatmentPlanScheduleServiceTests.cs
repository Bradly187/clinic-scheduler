using System.Linq.Expressions;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Services;
using FluentAssertions;
using Moq;

namespace ClinicScheduler.Web.Tests.Unit;

public class TreatmentPlanScheduleServiceTests
{
    // Plan starts on a far-future Monday so week alignment is deterministic
    private static readonly DateOnly PlanStart = new(2030, 6, 3); // Monday
    private static readonly TimeOnly NineAm = new(9, 0);

    [Fact]
    public async Task GenerateAppointments_FullSeries_BooksAllSessionsOnPreferredDays()
    {
        var fixture = new Fixture(frequencyPerWeek: 2, totalDays: 20);

        var result = await fixture.Sut.GenerateAppointmentsAsync(PlanId, RoomId, NineAm);

        result.SessionsRequested.Should().Be(20);
        result.SessionsBooked.Should().Be(20);
        result.SessionsUnbooked.Should().Be(0);

        result.Created.Should().AllSatisfy(a =>
        {
            a.TreatmentPlanId.Should().Be(PlanId);
            a.StartTime.DayOfWeek.Should().BeOneOf(DayOfWeek.Monday, DayOfWeek.Thursday);
            TimeOnly.FromDateTime(a.StartTime).Should().Be(NineAm);
        });

        // Exactly 2 sessions per week across 10 consecutive weeks
        result.Created
            .GroupBy(a => (a.StartTime - PlanStart.ToDateTime(TimeOnly.MinValue)).Days / 7)
            .Should().HaveCount(10)
            .And.AllSatisfy(week => week.Should().HaveCount(2));
    }

    [Fact]
    public async Task GenerateAppointments_ExistingSessions_BooksOnlyRemaining()
    {
        var fixture = new Fixture(frequencyPerWeek: 2, totalDays: 20);

        // 18 sessions already booked against the plan (in a non-overlapping earlier month)
        for (var i = 0; i < 18; i++)
        {
            var start = new DateTime(2030, 5, 1, 9, 0, 0, DateTimeKind.Utc).AddDays(i % 20);
            var appt = new Appointment(fixture.Patient, fixture.Therapist, fixture.Room, start, TimeSpan.FromMinutes(30))
            {
                TreatmentPlanId = PlanId
            };
            fixture.Appointments.Add(appt);
        }

        var result = await fixture.Sut.GenerateAppointmentsAsync(PlanId, RoomId, NineAm);

        result.SessionsRequested.Should().Be(2);
        result.SessionsBooked.Should().Be(2);
    }

    [Fact]
    public async Task GenerateAppointments_InactivePlan_ThrowsInvalidOperationException()
    {
        var fixture = new Fixture(frequencyPerWeek: 2, totalDays: 20);
        fixture.Plan.Suspend();

        var act = async () => await fixture.Sut.GenerateAppointmentsAsync(PlanId, RoomId, NineAm);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*active*");
    }

    [Fact]
    public async Task GenerateAppointments_FullyBookedPlan_ThrowsInvalidOperationException()
    {
        var fixture = new Fixture(frequencyPerWeek: 2, totalDays: 20);
        for (var i = 0; i < 20; i++)
        {
            var start = new DateTime(2030, 5, 1, 9, 0, 0, DateTimeKind.Utc).AddDays(i);
            fixture.Appointments.Add(
                new Appointment(fixture.Patient, fixture.Therapist, fixture.Room, start, TimeSpan.FromMinutes(30))
                {
                    TreatmentPlanId = PlanId
                });
        }

        var act = async () => await fixture.Sut.GenerateAppointmentsAsync(PlanId, RoomId, NineAm);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already*");
    }

    [Fact]
    public async Task GenerateAppointments_WrongPreferredDayCount_ThrowsArgumentException()
    {
        var fixture = new Fixture(frequencyPerWeek: 2, totalDays: 20);

        var act = async () => await fixture.Sut.GenerateAppointmentsAsync(
            PlanId, RoomId, NineAm, preferredDays: [DayOfWeek.Monday]);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*exactly 2*");
    }

    [Fact]
    public async Task GenerateAppointments_PreferredSlotTaken_BooksNearestSlotSameDay()
    {
        var fixture = new Fixture(frequencyPerWeek: 2, totalDays: 20);

        // Another patient occupies the therapist at 9:00 on the first Monday
        var otherPatient = new Patient("Other", "Patient", "other@test.com", new DateOnly(1985, 1, 1)) { Id = 2 };
        var blockingStart = PlanStart.ToDateTime(NineAm, DateTimeKind.Utc);
        fixture.Appointments.Add(new Appointment(
            otherPatient, fixture.Therapist, fixture.OtherRoom, blockingStart, TimeSpan.FromMinutes(30)));

        var result = await fixture.Sut.GenerateAppointmentsAsync(PlanId, RoomId, NineAm);

        result.SessionsBooked.Should().Be(20);

        // First Monday's session shifted to the nearest free slot (8:30 wins the tie over 9:30)
        var firstMonday = result.Created.Single(a => a.StartTime.Date == blockingStart.Date);
        TimeOnly.FromDateTime(firstMonday.StartTime).Should().Be(new TimeOnly(8, 30));

        // All other sessions stayed at the preferred time
        result.Created.Where(a => a.StartTime.Date != blockingStart.Date)
            .Should().AllSatisfy(a => TimeOnly.FromDateTime(a.StartTime).Should().Be(NineAm));
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    private const int PlanId = 5;
    private const int RoomId = 1;

    /// <summary>
    /// Wires a real AppointmentSchedulingService over an in-memory appointment list
    /// (FindAsync compiles its predicate), so generation sees its own bookings.
    /// </summary>
    private sealed class Fixture
    {
        public Location Location { get; } = new("Main", "123 Main St") { Id = 1 };
        public Patient Patient { get; }
        public Therapist Therapist { get; }
        public Room Room { get; }
        public Room OtherRoom { get; }
        public TreatmentPlan Plan { get; }
        public List<Appointment> Appointments { get; } = [];
        public TreatmentPlanScheduleService Sut { get; }

        public Fixture(int frequencyPerWeek, int totalDays)
        {
            Patient = new Patient("Pat", "Ient", "pat@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
            Therapist = new Therapist("Dr", "Therapist", "dr@clinic.com") { Id = 1 };
            Room = new Room("Room1", 1, Location) { Id = RoomId };
            OtherRoom = new Room("Room2", 1, Location) { Id = 2 };
            Plan = new TreatmentPlan(Patient, Therapist, frequencyPerWeek, totalDays, PlanStart) { Id = PlanId };

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

            var patientRepo = new Mock<IRepository<Patient>>();
            patientRepo.Setup(r => r.GetByIdAsync(Patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Patient);

            var therapistRepo = new Mock<IRepository<Therapist>>();
            therapistRepo.Setup(r => r.GetByIdAsync(Therapist.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Therapist);

            var roomRepo = new Mock<IRepository<Room>>();
            roomRepo.Setup(r => r.GetByIdAsync(Room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Room);
            roomRepo.Setup(r => r.GetByIdAsync(OtherRoom.Id, It.IsAny<CancellationToken>())).ReturnsAsync(OtherRoom);
            roomRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Room, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Room> { Room, OtherRoom });

            var timeSlotRepo = new Mock<IRepository<TimeSlot>>();
            timeSlotRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TimeSlot, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<TimeSlot>());

            var locationRepo = new Mock<IRepository<Location>>();
            locationRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Location);

            var conflictRepo = new Mock<IRepository<ScheduleConflict>>();
            conflictRepo.Setup(r => r.AddAsync(It.IsAny<ScheduleConflict>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ScheduleConflict sc, CancellationToken _) => sc);

            var schedulingService = new AppointmentSchedulingService(
                apptRepo.Object, patientRepo.Object, therapistRepo.Object, roomRepo.Object,
                timeSlotRepo.Object, locationRepo.Object, conflictRepo.Object);

            var planRepo = new Mock<IRepository<TreatmentPlan>>();
            planRepo.Setup(r => r.GetByIdAsync(PlanId, It.IsAny<CancellationToken>())).ReturnsAsync(Plan);

            Sut = new TreatmentPlanScheduleService(planRepo.Object, apptRepo.Object, schedulingService);
        }
    }
}
