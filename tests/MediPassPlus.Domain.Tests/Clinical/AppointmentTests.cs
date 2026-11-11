using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Domain.Tests.Clinical;

/// <summary>
/// Every allowed transition and every skipped state (FR-BKG-08). A refused transition must leave the status and the
/// timestamps exactly as they were.
/// </summary>
public class AppointmentTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewAppointment_StartsBookedWithNoTransitionTimestamp()
    {
        var appointment = NewAppointment();

        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
        Assert.Null(appointment.CheckedInAtUtc);
        Assert.Null(appointment.InVisitAtUtc);
        Assert.Null(appointment.CompletedAtUtc);
        Assert.Null(appointment.CancelledAtUtc);
    }

    [Fact]
    public void TryCheckIn_FromBooked_SetsCheckedInAndTheTimestamp()
    {
        var appointment = NewAppointment();

        Assert.True(appointment.TryCheckIn(Now));

        Assert.Equal(AppointmentStatus.CheckedIn, appointment.Status);
        Assert.Equal(Now, appointment.CheckedInAtUtc);
        Assert.Null(appointment.InVisitAtUtc);
        Assert.Null(appointment.CompletedAtUtc);
        Assert.Null(appointment.CancelledAtUtc);
    }

    [Fact]
    public void TryMarkInVisit_FromCheckedIn_SetsInVisitAndTheTimestamp()
    {
        var appointment = NewAppointment();
        appointment.TryCheckIn(Now.AddMinutes(-10));

        Assert.True(appointment.TryMarkInVisit(Now));

        Assert.Equal(AppointmentStatus.InVisit, appointment.Status);
        Assert.Equal(Now, appointment.InVisitAtUtc);
        Assert.Equal(Now.AddMinutes(-10), appointment.CheckedInAtUtc);
        Assert.Null(appointment.CompletedAtUtc);
    }

    [Fact]
    public void TryComplete_FromInVisit_SetsCompletedAndTheTimestamp()
    {
        var appointment = NewAppointment();
        appointment.TryCheckIn(Now.AddMinutes(-20));
        appointment.TryMarkInVisit(Now.AddMinutes(-10));

        Assert.True(appointment.TryComplete(Now));

        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal(Now, appointment.CompletedAtUtc);
        Assert.Null(appointment.CancelledAtUtc);
    }

    [Fact]
    public void TryCancel_FromBooked_SetsCancelledAndTheTimestamp()
    {
        var appointment = NewAppointment();

        Assert.True(appointment.TryCancel(Now));

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(Now, appointment.CancelledAtUtc);
        Assert.Null(appointment.CheckedInAtUtc);
        Assert.Null(appointment.InVisitAtUtc);
        Assert.Null(appointment.CompletedAtUtc);
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked, AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.CheckedIn, AppointmentStatus.InVisit)]
    [InlineData(AppointmentStatus.InVisit, AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Booked, AppointmentStatus.Cancelled)]
    public void AllowedTransitions_Succeed(AppointmentStatus from, AppointmentStatus to)
    {
        var appointment = NewAppointment(from);

        var succeeded = Transition(appointment, to, Now);

        Assert.True(succeeded);
        Assert.Equal(to, appointment.Status);
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked, AppointmentStatus.InVisit)]
    [InlineData(AppointmentStatus.Booked, AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.CheckedIn, AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.CheckedIn, AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.InVisit, AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.InVisit, AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed, AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Completed, AppointmentStatus.InVisit)]
    [InlineData(AppointmentStatus.Completed, AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Cancelled, AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Cancelled, AppointmentStatus.InVisit)]
    [InlineData(AppointmentStatus.Cancelled, AppointmentStatus.Completed)]
    public void SkippedOrBackwardTransitions_AreRefusedAndLeaveTheStateUntouched(
        AppointmentStatus from,
        AppointmentStatus to)
    {
        var appointment = NewAppointment(from);

        var succeeded = Transition(appointment, to, Now);

        Assert.False(succeeded);
        Assert.Equal(from, appointment.Status);
        Assert.Null(appointment.CheckedInAtUtc);
        Assert.Null(appointment.InVisitAtUtc);
        Assert.Null(appointment.CompletedAtUtc);
        Assert.Null(appointment.CancelledAtUtc);
    }

    [Fact]
    public void TryCancel_WhenThePatientWasCheckedIn_IsRefused()
    {
        // FR-BKG-04 / US-08 "before the visit starts" is interpreted as before the slot and while still Booked:
        // once staff checks the patient in, the patient has arrived and cancellation is refused.
        var appointment = NewAppointment();
        appointment.TryCheckIn(Now.AddMinutes(-5));

        Assert.False(appointment.TryCancel(Now));

        Assert.Equal(AppointmentStatus.CheckedIn, appointment.Status);
        Assert.Equal(Now.AddMinutes(-5), appointment.CheckedInAtUtc);
        Assert.Null(appointment.CancelledAtUtc);
    }

    [Theory]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.InVisit)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void RepeatedTransition_IsRefusedAndKeepsTheFirstTimestamp(AppointmentStatus state)
    {
        var appointment = NewAppointment();
        ReachState(appointment, state, Now);

        var succeeded = Transition(appointment, state, Now.AddHours(1));

        Assert.False(succeeded);
        Assert.Equal(state, appointment.Status);
        Assert.Equal(Now, TimestampFor(appointment, state));
    }

    private static void ReachState(Appointment appointment, AppointmentStatus state, DateTime utcNow)
    {
        switch (state)
        {
            case AppointmentStatus.CheckedIn:
                Assert.True(appointment.TryCheckIn(utcNow));
                break;
            case AppointmentStatus.InVisit:
                Assert.True(appointment.TryCheckIn(utcNow));
                Assert.True(appointment.TryMarkInVisit(utcNow));
                break;
            case AppointmentStatus.Completed:
                Assert.True(appointment.TryCheckIn(utcNow));
                Assert.True(appointment.TryMarkInVisit(utcNow));
                Assert.True(appointment.TryComplete(utcNow));
                break;
            case AppointmentStatus.Cancelled:
                Assert.True(appointment.TryCancel(utcNow));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Booked is the initial state.");
        }
    }

    private static DateTime? TimestampFor(Appointment appointment, AppointmentStatus state) =>
        state switch
        {
            AppointmentStatus.CheckedIn => appointment.CheckedInAtUtc,
            AppointmentStatus.InVisit => appointment.InVisitAtUtc,
            AppointmentStatus.Completed => appointment.CompletedAtUtc,
            AppointmentStatus.Cancelled => appointment.CancelledAtUtc,
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Booked has no transition timestamp.")
        };

    private static bool Transition(Appointment appointment, AppointmentStatus to, DateTime utcNow) =>
        to switch
        {
            AppointmentStatus.CheckedIn => appointment.TryCheckIn(utcNow),
            AppointmentStatus.InVisit => appointment.TryMarkInVisit(utcNow),
            AppointmentStatus.Completed => appointment.TryComplete(utcNow),
            AppointmentStatus.Cancelled => appointment.TryCancel(utcNow),
            _ => throw new ArgumentOutOfRangeException(nameof(to), to, "Booked is the initial state, not a transition.")
        };

    private static Appointment NewAppointment(AppointmentStatus status = AppointmentStatus.Booked) =>
        new()
        {
            Id = 42,
            PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DoctorId = 7,
            DoctorSlotId = 3,
            Status = status,
            CreatedAtUtc = Now.AddDays(-1)
        };
}
