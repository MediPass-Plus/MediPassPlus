using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

/// <summary>
/// The patient's immediate end: ownership is verified against the appointment, an ended or expired grant is refused,
/// and the end commits with exactly one <c>AccessEnded</c> audit row (FR-ACS-09, FR-ACS-11, UC-19).
/// </summary>
public class AccessGrantEndServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherPatientId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private const string IpAddress = "203.0.113.10";

    [Fact]
    public async Task EndAsync_WhenThePatientOwnsAnActiveGrant_EndsItImmediatelyAndAudits()
    {
        var appointment = CreateAppointment();
        var grant = CreateGrant(appointment, grantedAtUtc: Now.UtcDateTime.AddMinutes(-10));
        var store = new FakeAccessGrantEndStore { Grant = grant, TrackedGrant = grant };
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.EndAsync(grant.Id, PatientId, IpAddress);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<AccessGrantEndedResponse>(result.Grant);
        Assert.Equal(grant.Id, response.GrantId);
        Assert.Equal(Now.UtcDateTime, response.EndedAtUtc);
        Assert.Equal(nameof(GrantEndReason.PatientEnded), response.EndReason);

        Assert.Equal(Now.UtcDateTime, grant.EndedAtUtc);
        Assert.Equal(GrantEndReason.PatientEnded, grant.EndReason);
        Assert.False(grant.IsActiveAt(Now.UtcDateTime));
        Assert.Equal(1, store.SaveCount);

        var scope = Assert.IsType<VisitAccessScope>(store.LastScope);
        Assert.Equal(appointment.Id, scope.AppointmentId);
        Assert.Equal(appointment.DoctorId, scope.DoctorId);
        Assert.Equal(PatientId, scope.PatientId);

        var audit = Assert.Single(auditWriter.AddedEntries);
        Assert.Equal(PatientId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Equal(AuditAction.AccessEnded, audit.Action);
        Assert.Equal(AuditEntityType.AccessGrant, audit.EntityType);
        Assert.Equal(grant.Id.ToString(), audit.EntityId);
        Assert.Equal(IpAddress, audit.IpAddress);
        Assert.Equal(grant.Id, audit.AccessGrantId);
    }

    [Fact]
    public async Task EndAsync_WhenNoGrantHasTheIdentifier_ReturnsGrantNotFound()
    {
        var store = new FakeAccessGrantEndStore();
        var (service, auditWriter) = CreateService(CreateAppointment(), store);

        var result = await service.EndAsync(Guid.NewGuid(), PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessGrantEndFailure.GrantNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
        Assert.False(store.WasLoaded);
        Assert.Empty(auditWriter.AddedEntries);
    }

    [Fact]
    public async Task EndAsync_WhenThePatientDoesNotOwnTheAppointment_ReturnsPatientMismatch()
    {
        var appointment = CreateAppointment();
        var grant = CreateGrant(appointment);
        var store = new FakeAccessGrantEndStore { Grant = grant, TrackedGrant = grant };
        var (service, auditWriter) = CreateService(
            appointment,
            store,
            VisitAccessScopeResult.Denied(VisitAccessScopeFailure.PatientMismatch));

        var result = await service.EndAsync(grant.Id, OtherPatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessGrantEndFailure.PatientMismatch, result.Failure);
        Assert.Equal(0, store.SaveCount);
        Assert.False(store.WasLoaded);
        Assert.Null(grant.EndedAtUtc);
        Assert.Empty(auditWriter.AddedEntries);
    }

    [Fact]
    public async Task EndAsync_WhenTheAppointmentNoLongerExists_ReturnsGrantNotFound()
    {
        var appointment = CreateAppointment();
        var grant = CreateGrant(appointment);
        var store = new FakeAccessGrantEndStore { Grant = grant, TrackedGrant = grant };
        var (service, _) = CreateService(
            appointment,
            store,
            VisitAccessScopeResult.Denied(VisitAccessScopeFailure.AppointmentNotFound));

        var result = await service.EndAsync(grant.Id, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessGrantEndFailure.GrantNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task EndAsync_WhenTheGrantAlreadyEnded_ReturnsGrantNotActiveWithoutAudit()
    {
        var appointment = CreateAppointment();
        var grant = CreateGrant(appointment, grantedAtUtc: Now.UtcDateTime.AddMinutes(-30));
        grant.EndByPatient(Now.UtcDateTime.AddMinutes(-5));
        var store = new FakeAccessGrantEndStore { Grant = grant, TrackedGrant = grant };
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.EndAsync(grant.Id, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessGrantEndFailure.GrantNotActive, result.Failure);
        Assert.Equal(Now.UtcDateTime.AddMinutes(-5), grant.EndedAtUtc);
        Assert.Equal(0, store.SaveCount);
        Assert.Empty(auditWriter.AddedEntries);
    }

    [Fact]
    public async Task EndAsync_DuringThePostCompletionGraceWindow_OverridesTheScheduledEnd()
    {
        var appointment = CreateAppointment();
        var grant = CreateGrant(appointment, grantedAtUtc: Now.UtcDateTime.AddMinutes(-30));

        // Completion schedules the end 15 minutes later, so the grant is still active during the grace window.
        grant.CompleteVisit(Now.UtcDateTime.AddMinutes(-5));
        var store = new FakeAccessGrantEndStore { Grant = grant, TrackedGrant = grant };
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.EndAsync(grant.Id, PatientId, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.UtcDateTime, grant.EndedAtUtc);
        Assert.Equal(GrantEndReason.PatientEnded, grant.EndReason);
        Assert.Equal(1, store.SaveCount);
        Assert.Single(auditWriter.AddedEntries);
    }

    [Fact]
    public async Task EndAsync_WhenTheTwoHourLimitPassed_ReturnsGrantNotActive()
    {
        var appointment = CreateAppointment();
        var grant = CreateGrant(appointment, grantedAtUtc: Now.UtcDateTime.AddHours(-3));
        var store = new FakeAccessGrantEndStore { Grant = grant, TrackedGrant = grant };
        var (service, _) = CreateService(appointment, store);

        var result = await service.EndAsync(grant.Id, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessGrantEndFailure.GrantNotActive, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task EndAsync_WhenTheGrantNoLongerMatchesTheVerifiedScope_ReturnsGrantNotFound()
    {
        var appointment = CreateAppointment();
        var grant = CreateGrant(appointment);
        var store = new FakeAccessGrantEndStore { Grant = grant, TrackedGrant = null };
        var (service, _) = CreateService(appointment, store);

        var result = await service.EndAsync(grant.Id, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessGrantEndFailure.GrantNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
        Assert.Null(grant.EndedAtUtc);
    }

    [Fact]
    public async Task EndAsync_WhenAnotherRequestEndedTheGrantFirst_ReturnsGrantConflict()
    {
        var appointment = CreateAppointment();
        var grant = CreateGrant(appointment);
        var store = new FakeAccessGrantEndStore
        {
            Grant = grant,
            TrackedGrant = grant,
            ConflictOnSave = true
        };
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.EndAsync(grant.Id, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessGrantEndFailure.GrantConflict, result.Failure);
        Assert.Null(result.Grant);
        Assert.Equal(1, store.SaveCount);

        // The audit row was staged with the end, but the refused save means it never commits.
        Assert.Single(auditWriter.AddedEntries);
        Assert.Equal(grant.Id, auditWriter.AddedEntries[0].AccessGrantId);
    }

    [Fact]
    public async Task EndAsync_WhenTheIpAddressIsBlank_Throws()
    {
        var store = new FakeAccessGrantEndStore();
        var (service, _) = CreateService(CreateAppointment(), store);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.EndAsync(Guid.NewGuid(), PatientId, " "));
    }

    private static (AccessGrantEndService Service, FakeAuditWriter AuditWriter) CreateService(
        Appointment appointment,
        FakeAccessGrantEndStore store,
        VisitAccessScopeResult? patientResult = null)
    {
        var auditWriter = new FakeAuditWriter();
        var scope = new FakeVisitAccessScopeService
        {
            PatientResult = patientResult
                ?? VisitAccessScopeResult.Allowed(
                    new VisitAccessScope(appointment.Id, appointment.DoctorId, appointment.PatientId))
        };

        return (
            new AccessGrantEndService(scope, store, auditWriter, new FixedTimeProvider(Now)),
            auditWriter);
    }

    private static AccessGrant CreateGrant(Appointment appointment, DateTime? grantedAtUtc = null)
    {
        var request = AccessRequest.Create(
            appointment,
            CreateDoctor(appointment.DoctorId),
            Now.UtcDateTime.AddMinutes(-20));
        request.Approve();

        return AccessGrant.Create(request, appointment, grantedAtUtc ?? Now.UtcDateTime);
    }

    private static Appointment CreateAppointment()
    {
        var start = Now.UtcDateTime.Date.AddHours(9);
        var slot = new DoctorSlot
        {
            Id = 1,
            DoctorId = 7,
            StartTimeUtc = start,
            EndTimeUtc = start.AddMinutes(30),
            IsAvailable = true
        };

        return new Appointment
        {
            Id = 42,
            DoctorId = 7,
            PatientId = PatientId,
            DoctorSlotId = slot.Id,
            DoctorSlot = slot,
            Status = AppointmentStatus.InVisit,
            CreatedAtUtc = Now.UtcDateTime.AddDays(-1)
        };
    }

    private static Doctor CreateDoctor(int id) =>
        new()
        {
            Id = id,
            UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Specialty = "General",
            ClinicId = 1
        };
}
