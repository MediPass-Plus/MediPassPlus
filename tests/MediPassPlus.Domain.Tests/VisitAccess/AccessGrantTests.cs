using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Domain.Tests.VisitAccess;

public class AccessGrantTests
{
    private static readonly DateTime GrantedAt = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_ForApprovedRequest_IsActiveUntilTheTwoHourCap()
    {
        var request = CreateApprovedRequest(out var appointment);

        var grant = AccessGrant.Create(request, appointment, GrantedAt);

        Assert.Equal(request.Id, grant.AccessRequestId);
        Assert.Equal(request.AppointmentId, grant.AppointmentId);
        Assert.Equal(request.DoctorId, grant.DoctorId);
        Assert.Equal(request.PatientId, grant.PatientId);
        Assert.Equal(GrantedAt, grant.GrantedAtUtc);
        Assert.Equal(GrantedAt.AddHours(2), grant.ExpiresAtUtc);
        Assert.Null(grant.EndedAtUtc);
        Assert.Null(grant.EndReason);
        Assert.True(grant.IsActiveAt(GrantedAt));
        Assert.Equal(AccessGrantStatus.Active, grant.StatusAt(GrantedAt));
    }

    [Fact]
    public void Create_ForPendingRequest_Throws()
    {
        var appointment = CreateAppointment();
        var request = AccessRequest.Create(appointment, CreateDoctor(), GrantedAt);

        Assert.Throws<InvalidOperationException>(() => AccessGrant.Create(request, appointment, GrantedAt));
    }

    [Fact]
    public void Create_WhenRequestIsForAnotherAppointment_Throws()
    {
        var request = CreateApprovedRequest(out _);
        var otherAppointment = CreateAppointment(id: 43);

        Assert.Throws<InvalidOperationException>(
            () => AccessGrant.Create(request, otherAppointment, GrantedAt));
    }

    [Fact]
    public void Create_WhenAppointmentDoctorChanged_Throws()
    {
        var request = CreateApprovedRequest(out var appointment);
        appointment.DoctorId = 8;

        Assert.Throws<InvalidOperationException>(
            () => AccessGrant.Create(request, appointment, GrantedAt));
    }

    [Fact]
    public void Create_WhenAppointmentPatientChanged_Throws()
    {
        var request = CreateApprovedRequest(out var appointment);
        appointment.PatientId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(
            () => AccessGrant.Create(request, appointment, GrantedAt));
    }

    [Fact]
    public void Create_NullRequest_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => AccessGrant.Create(null!, CreateAppointment(), GrantedAt));
    }

    [Fact]
    public void Create_NullAppointment_Throws()
    {
        var request = CreateApprovedRequest(out _);

        Assert.Throws<ArgumentNullException>(
            () => AccessGrant.Create(request, null!, GrantedAt));
    }

    [Fact]
    public void CompleteVisit_SchedulesTheEndFifteenMinutesLater_AndStaysActiveDuringTheGraceWindow()
    {
        var grant = CreateGrant();

        grant.CompleteVisit(GrantedAt.AddMinutes(30));

        Assert.Equal(GrantedAt.AddMinutes(45), grant.EndedAtUtc);
        Assert.Equal(GrantEndReason.VisitCompleted, grant.EndReason);
        Assert.True(grant.IsActiveAt(GrantedAt.AddMinutes(44)));
        Assert.Equal(AccessGrantStatus.Active, grant.StatusAt(GrantedAt.AddMinutes(44)));
        Assert.False(grant.IsActiveAt(GrantedAt.AddMinutes(45)));
        Assert.Equal(AccessGrantStatus.Ended, grant.StatusAt(GrantedAt.AddMinutes(45)));
    }

    [Fact]
    public void CompleteVisit_NearTheTimeLimit_CapsTheEndAtTheTwoHourLimit()
    {
        var grant = CreateGrant();

        grant.CompleteVisit(GrantedAt.AddMinutes(115));

        Assert.Equal(GrantedAt.AddHours(2), grant.EndedAtUtc);
        Assert.Equal(AccessGrantStatus.Active, grant.StatusAt(GrantedAt.AddMinutes(119)));
        Assert.Equal(AccessGrantStatus.Ended, grant.StatusAt(GrantedAt.AddHours(2)));
    }

    [Fact]
    public void CompleteVisit_Repeated_KeepsTheEarliestEnd()
    {
        var grant = CreateGrant();
        grant.CompleteVisit(GrantedAt.AddMinutes(10));
        var firstEnd = grant.EndedAtUtc;

        grant.CompleteVisit(GrantedAt.AddMinutes(20));

        Assert.Equal(firstEnd, grant.EndedAtUtc);
        Assert.Equal(GrantEndReason.VisitCompleted, grant.EndReason);
    }

    [Fact]
    public void CompleteVisit_AfterTheGrantEnded_Throws()
    {
        var grant = CreateGrant();
        grant.EndByPatient(GrantedAt.AddMinutes(10));

        Assert.Throws<InvalidOperationException>(() => grant.CompleteVisit(GrantedAt.AddMinutes(20)));
    }

    [Fact]
    public void EndByPatient_EndsTheGrantImmediately()
    {
        var grant = CreateGrant();

        grant.EndByPatient(GrantedAt.AddMinutes(10));

        Assert.Equal(GrantedAt.AddMinutes(10), grant.EndedAtUtc);
        Assert.Equal(GrantEndReason.PatientEnded, grant.EndReason);
        Assert.True(grant.IsActiveAt(GrantedAt.AddMinutes(10).AddTicks(-1)));
        Assert.False(grant.IsActiveAt(GrantedAt.AddMinutes(10)));
        Assert.Equal(AccessGrantStatus.Ended, grant.StatusAt(GrantedAt.AddMinutes(10)));
    }

    [Fact]
    public void EndByPatient_DuringTheGraceWindow_OverridesTheScheduledEnd()
    {
        var grant = CreateGrant();
        grant.CompleteVisit(GrantedAt.AddMinutes(30));

        grant.EndByPatient(GrantedAt.AddMinutes(40));

        Assert.Equal(GrantedAt.AddMinutes(40), grant.EndedAtUtc);
        Assert.Equal(GrantEndReason.PatientEnded, grant.EndReason);
        Assert.Equal(AccessGrantStatus.Ended, grant.StatusAt(GrantedAt.AddMinutes(40)));
    }

    [Fact]
    public void EndByPatient_AfterTheTimeLimit_Throws()
    {
        var grant = CreateGrant();

        Assert.Throws<InvalidOperationException>(() => grant.EndByPatient(GrantedAt.AddHours(2)));
    }

    [Fact]
    public void StatusAt_AtTheTwoHourLimitWithoutAnEnd_IsExpired()
    {
        var grant = CreateGrant();

        Assert.True(grant.IsActiveAt(GrantedAt.AddHours(2).AddTicks(-1)));
        Assert.False(grant.IsActiveAt(GrantedAt.AddHours(2)));
        Assert.Equal(AccessGrantStatus.Expired, grant.StatusAt(GrantedAt.AddHours(2)));
        Assert.Equal(AccessGrantStatus.Expired, grant.StatusAt(GrantedAt.AddHours(3)));
        Assert.Null(grant.EndReason);
    }

    private static AccessRequest CreateApprovedRequest(out Appointment appointment)
    {
        appointment = CreateAppointment();
        var request = AccessRequest.Create(appointment, CreateDoctor(), GrantedAt);
        request.Approve();
        return request;
    }

    private static AccessGrant CreateGrant()
    {
        var request = CreateApprovedRequest(out var appointment);
        return AccessGrant.Create(request, appointment, GrantedAt);
    }

    private static Appointment CreateAppointment(int id = 42) =>
        new()
        {
            Id = id,
            DoctorId = 7,
            PatientId = Guid.NewGuid()
        };

    private static Doctor CreateDoctor() =>
        new()
        {
            Id = 7,
            UserId = Guid.NewGuid()
        };
}
