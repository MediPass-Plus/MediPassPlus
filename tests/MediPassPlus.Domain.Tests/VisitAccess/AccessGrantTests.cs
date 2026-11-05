using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Domain.Tests.VisitAccess;

public class AccessGrantTests
{
    private static readonly DateTime GrantedAt = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_ForApprovedRequest_IsActiveUntilTheTwoHourCap()
    {
        var request = CreateApprovedRequest();

        var grant = AccessGrant.Create(request, GrantedAt);

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
        var request = AccessRequest.Create(42, 7, Guid.NewGuid(), GrantedAt);

        Assert.Throws<InvalidOperationException>(() => AccessGrant.Create(request, GrantedAt));
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

    [Fact]
    public void Create_NullRequest_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AccessGrant.Create(null!, GrantedAt));
    }

    private static AccessRequest CreateApprovedRequest()
    {
        var request = AccessRequest.Create(42, 7, Guid.NewGuid(), GrantedAt);
        request.Approve();
        return request;
    }

    private static AccessGrant CreateGrant() => AccessGrant.Create(CreateApprovedRequest(), GrantedAt);
}
