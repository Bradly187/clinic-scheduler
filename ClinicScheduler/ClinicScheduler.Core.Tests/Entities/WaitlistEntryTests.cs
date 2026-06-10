using ClinicScheduler.Core.Entities;
using FluentAssertions;

namespace ClinicScheduler.Core.Tests.Entities;

public class WaitlistEntryTests
{
    private static Patient MakePatient() =>
        new("Pat", "Ient", "pat@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };

    private static WaitlistEntry MakeEntry() =>
        new(MakePatient(), new DateOnly(2030, 6, 3), new DateOnly(2030, 7, 3));

    private static Appointment MakeAppointment()
    {
        var location = new Location("Main", "123 St") { Id = 1 };
        var therapist = new Therapist("Dr", "T", "dr@clinic.com") { Id = 1 };
        var room = new Room("Room1", 1, location) { Id = 1 };
        return new Appointment(MakePatient(), therapist, room,
            new DateTime(2030, 6, 10, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Constructor_LatestBeforeEarliest_Throws()
    {
        var act = () => new WaitlistEntry(MakePatient(), new DateOnly(2030, 6, 10), new DateOnly(2030, 6, 9));
        act.Should().Throw<ArgumentException>().WithMessage("*on or after*");
    }

    [Fact]
    public void Constructor_WindowOver90Days_Throws()
    {
        var act = () => new WaitlistEntry(MakePatient(), new DateOnly(2030, 1, 1), new DateOnly(2030, 6, 1));
        act.Should().Throw<ArgumentException>().WithMessage("*90 days*");
    }

    [Fact]
    public void Constructor_TimeFromNotBeforeTimeTo_Throws()
    {
        var act = () => new WaitlistEntry(MakePatient(), new DateOnly(2030, 6, 3), new DateOnly(2030, 6, 10),
            preferredTimeFrom: new TimeOnly(15, 0), preferredTimeTo: new TimeOnly(14, 0));
        act.Should().Throw<ArgumentException>().WithMessage("*earlier than*");
    }

    [Theory]
    [InlineData(2030, 6, 3, 9, 0, true)]    // first day, any time
    [InlineData(2030, 7, 3, 16, 30, true)]  // last day
    [InlineData(2030, 6, 2, 9, 0, false)]   // before window
    [InlineData(2030, 7, 4, 9, 0, false)]   // after window
    public void Matches_RespectsDateWindow(int y, int m, int d, int h, int min, bool expected)
    {
        var entry = MakeEntry();
        entry.Matches(new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc)).Should().Be(expected);
    }

    [Theory]
    [InlineData(13, 59, false)]
    [InlineData(14, 0, true)]
    [InlineData(16, 0, true)]
    [InlineData(16, 1, false)]
    public void Matches_RespectsTimeWindow(int hour, int minute, bool expected)
    {
        var entry = new WaitlistEntry(MakePatient(), new DateOnly(2030, 6, 3), new DateOnly(2030, 7, 3),
            preferredTimeFrom: new TimeOnly(14, 0), preferredTimeTo: new TimeOnly(16, 0));

        entry.Matches(new DateTime(2030, 6, 10, hour, minute, 0, DateTimeKind.Utc)).Should().Be(expected);
    }

    [Fact]
    public void Fulfill_ActiveEntry_SetsStatusAndAppointment()
    {
        var entry = MakeEntry();
        var appointment = MakeAppointment();

        entry.Fulfill(appointment);

        entry.Status.Should().Be(WaitlistStatus.Fulfilled);
        entry.FulfilledAppointment.Should().BeSameAs(appointment);
    }

    [Fact]
    public void Fulfill_CanceledEntry_Throws()
    {
        var entry = MakeEntry();
        entry.Cancel();

        var act = () => entry.Fulfill(MakeAppointment());
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cancel_ActiveEntry_SetsCanceled()
    {
        var entry = MakeEntry();
        entry.Cancel();
        entry.Status.Should().Be(WaitlistStatus.Canceled);
    }

    [Fact]
    public void Expire_ActiveEntry_SetsExpired()
    {
        var entry = MakeEntry();
        entry.Expire();
        entry.Status.Should().Be(WaitlistStatus.Expired);
    }

    [Fact]
    public void Cancel_FulfilledEntry_Throws()
    {
        var entry = MakeEntry();
        entry.Fulfill(MakeAppointment());

        var act = entry.Cancel;
        act.Should().Throw<InvalidOperationException>();
    }
}
