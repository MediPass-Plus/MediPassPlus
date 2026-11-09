using MediPassPlus.Application.Privacy;
using MediPassPlus.Application.Tests.VisitAccess;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.Privacy;

public class AccessGrantCheckerTests
{
    private static readonly DateTime Start = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly VisitAccessScope Scope = new(42, 7, PatientId);

    [Fact]
    public async Task RequireActiveGrantAsync_WhenTheGrantIsActive_ReturnsTheGrant()
    {
        var grant = CreateGrant(Start);
        var store = new FakeAccessGrantStore { Grant = grant };
        var checker = CreateChecker(store, FakeVisitAccessScopeService.Allowing(Scope), Start.AddMinutes(30));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.True(result.IsAllowed);
        Assert.Same(grant, result.Grant);
        Assert.Equal(AccessGrantCheckFailure.None, result.Failure);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_PassesTheVerifiedScopeToTheStore()
    {
        var store = new FakeAccessGrantStore { Grant = CreateGrant(Start) };
        var checker = CreateChecker(store, FakeVisitAccessScopeService.Allowing(Scope), Start.AddMinutes(1));

        await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.True(store.WasCalled);
        Assert.Equal(Scope.AppointmentId, store.LastAppointmentId);
        Assert.Equal(Scope.DoctorId, store.LastDoctorId);
        Assert.Equal(Scope.PatientId, store.LastPatientId);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_InsideThePostCompletionGraceWindow_AllowsAccess()
    {
        // ADR-0011 decision 3: completing the visit schedules the end 15 minutes later and the grant stays active,
        // so the doctor can finish the record (UC-18, US-25).
        var grant = CreateGrant(Start);
        grant.CompleteVisit(Start.AddMinutes(1));
        var store = new FakeAccessGrantStore { Grant = grant };
        var checker = CreateChecker(store, FakeVisitAccessScopeService.Allowing(Scope), Start.AddMinutes(10));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.True(result.IsAllowed);
        Assert.Same(grant, result.Grant);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_AfterThePostCompletionGraceWindow_ReturnsGrantEnded()
    {
        var grant = CreateGrant(Start);
        grant.CompleteVisit(Start.AddMinutes(1));
        var store = new FakeAccessGrantStore { Grant = grant };
        var checker = CreateChecker(store, FakeVisitAccessScopeService.Allowing(Scope), Start.AddMinutes(16));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Null(result.Grant);
        Assert.Equal(AccessGrantCheckFailure.GrantEnded, result.Failure);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_WhenThePatientEndedTheGrant_ReturnsGrantEnded()
    {
        var grant = CreateGrant(Start);
        grant.EndByPatient(Start.AddMinutes(2));
        var store = new FakeAccessGrantStore { Grant = grant };
        var checker = CreateChecker(store, FakeVisitAccessScopeService.Allowing(Scope), Start.AddMinutes(3));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Equal(AccessGrantCheckFailure.GrantEnded, result.Failure);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_WhenTheTwoHourLimitIsReached_ReturnsGrantExpired()
    {
        var store = new FakeAccessGrantStore { Grant = CreateGrant(Start) };
        var checker = CreateChecker(store, FakeVisitAccessScopeService.Allowing(Scope), Start.AddHours(2));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Equal(AccessGrantCheckFailure.GrantExpired, result.Failure);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_WhenTheStoredExpiryExceedsTheTwoHourCap_ReturnsGrantExpired()
    {
        // Defence in depth: the two-hour cap is absolute even if a stored row claims a later expiry
        // (FR-ACS-07, UC-24). The tampering simulates a corrupted row the domain never produces.
        var grant = CreateGrant(Start);
        typeof(AccessGrant)
            .GetProperty(nameof(AccessGrant.ExpiresAtUtc))!
            .SetValue(grant, Start.AddHours(5));

        var store = new FakeAccessGrantStore { Grant = grant };
        var checker = CreateChecker(store, FakeVisitAccessScopeService.Allowing(Scope), Start.AddHours(3));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Equal(AccessGrantCheckFailure.GrantExpired, result.Failure);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_WhenTheStoredExpiryIsEarlierThanTheEndedAt_ReturnsGrantExpired()
    {
        // Defence in depth: a corrupted row can have an EndedAtUtc later than ExpiresAtUtc. The stored
        // expiry is still absolute and must reject the request before the lifecycle check (FR-ACS-07).
        var grant = CreateGrant(Start);
        grant.CompleteVisit(Start.AddMinutes(30));
        typeof(AccessGrant)
            .GetProperty(nameof(AccessGrant.ExpiresAtUtc))!
            .SetValue(grant, Start.AddMinutes(40));

        var store = new FakeAccessGrantStore { Grant = grant };
        var checker = CreateChecker(store, FakeVisitAccessScopeService.Allowing(Scope), Start.AddMinutes(42));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Null(result.Grant);
        Assert.Equal(AccessGrantCheckFailure.GrantExpired, result.Failure);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_WhenNoGrantExists_ReturnsGrantNotFound()
    {
        var store = new FakeAccessGrantStore();
        var checker = CreateChecker(store, FakeVisitAccessScopeService.Allowing(Scope), Start.AddMinutes(1));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Equal(AccessGrantCheckFailure.GrantNotFound, result.Failure);
        Assert.True(store.WasCalled);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_WhenTheAppointmentIsMissing_ReturnsAppointmentNotFound()
    {
        var store = new FakeAccessGrantStore { Grant = CreateGrant(Start) };
        var checker = CreateChecker(
            store,
            new FakeVisitAccessScopeService
            {
                DoctorResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.AppointmentNotFound)
            },
            Start.AddMinutes(1));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Equal(AccessGrantCheckFailure.AppointmentNotFound, result.Failure);
        Assert.False(store.WasCalled);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_WhenTheDoctorProfileIsMissing_ReturnsDoctorNotFound()
    {
        var store = new FakeAccessGrantStore { Grant = CreateGrant(Start) };
        var checker = CreateChecker(
            store,
            new FakeVisitAccessScopeService
            {
                DoctorResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.DoctorNotFound)
            },
            Start.AddMinutes(1));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Equal(AccessGrantCheckFailure.DoctorNotFound, result.Failure);
        Assert.False(store.WasCalled);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_WhenTheAppointmentBelongsToAnotherDoctor_ReturnsDoctorMismatch()
    {
        var store = new FakeAccessGrantStore { Grant = CreateGrant(Start) };
        var checker = CreateChecker(
            store,
            new FakeVisitAccessScopeService
            {
                DoctorResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.DoctorMismatch)
            },
            Start.AddMinutes(1));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Equal(AccessGrantCheckFailure.DoctorMismatch, result.Failure);
        Assert.False(store.WasCalled);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_WhenTheGrantBelongsToAnotherPatient_ReturnsScopeMismatch()
    {
        var otherPatientScope = Scope with { PatientId = Guid.NewGuid() };
        var store = new FakeAccessGrantStore { Grant = CreateGrant(Start, otherPatientScope) };
        var checker = CreateChecker(store, FakeVisitAccessScopeService.Allowing(Scope), Start.AddMinutes(1));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Equal(AccessGrantCheckFailure.ScopeMismatch, result.Failure);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_WhenTheGrantBelongsToAnotherDoctor_ReturnsScopeMismatch()
    {
        var otherDoctorScope = Scope with { DoctorId = 8 };
        var store = new FakeAccessGrantStore { Grant = CreateGrant(Start, otherDoctorScope) };
        var checker = CreateChecker(store, FakeVisitAccessScopeService.Allowing(Scope), Start.AddMinutes(1));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Equal(AccessGrantCheckFailure.ScopeMismatch, result.Failure);
    }

    [Fact]
    public async Task RequireActiveGrantAsync_WhenTheScopeReportsAPatientMismatch_ReturnsScopeMismatch()
    {
        var store = new FakeAccessGrantStore { Grant = CreateGrant(Start) };
        var checker = CreateChecker(
            store,
            new FakeVisitAccessScopeService
            {
                DoctorResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.PatientMismatch)
            },
            Start.AddMinutes(1));

        var result = await checker.RequireActiveGrantAsync(DoctorUserId, Scope.AppointmentId);

        Assert.False(result.IsAllowed);
        Assert.Equal(AccessGrantCheckFailure.ScopeMismatch, result.Failure);
    }

    private static AccessGrantChecker CreateChecker(
        FakeAccessGrantStore store,
        IVisitAccessScopeService scopeService,
        DateTime utcNow) =>
        new(scopeService, store, new FixedTimeProvider(new DateTimeOffset(utcNow)));

    private static AccessGrant CreateGrant(DateTime grantedAtUtc, VisitAccessScope? scope = null)
    {
        var effectiveScope = scope ?? Scope;

        var appointment = new Appointment
        {
            Id = effectiveScope.AppointmentId,
            DoctorId = effectiveScope.DoctorId,
            PatientId = effectiveScope.PatientId,
            DoctorSlotId = 1
        };

        var doctor = new Doctor
        {
            Id = effectiveScope.DoctorId,
            UserId = DoctorUserId,
            Specialty = "General",
            ClinicId = 1
        };

        var request = AccessRequest.Create(appointment, doctor, grantedAtUtc);
        request.Approve();

        return AccessGrant.Create(request, appointment, grantedAtUtc);
    }

    private sealed class FakeAccessGrantStore : IAccessGrantStore
    {
        public AccessGrant? Grant { get; set; }

        public bool WasCalled { get; private set; }

        public int LastAppointmentId { get; private set; }

        public int LastDoctorId { get; private set; }

        public Guid LastPatientId { get; private set; }

        public Task<AccessGrant?> FindGrantAsync(
            int appointmentId,
            int doctorId,
            Guid patientId,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastAppointmentId = appointmentId;
            LastDoctorId = doctorId;
            LastPatientId = patientId;

            return Task.FromResult(Grant);
        }
    }
}
