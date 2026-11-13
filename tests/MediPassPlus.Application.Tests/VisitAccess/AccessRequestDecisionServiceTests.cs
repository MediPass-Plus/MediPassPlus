using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

public class AccessRequestDecisionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string IpAddress = "203.0.113.10";

    [Fact]
    public async Task ApproveAsync_WhenThePatientOwnsTheAppointment_CreatesTheGrantAndConsumesTheCredential()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var credential = CreateCredential(request, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, request, [credential]);
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.ApproveAsync(request.Id, PatientId, IpAddress);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<AccessGrantResponse>(result.Grant);
        Assert.Equal(request.Id, response.AccessRequestId);
        Assert.Equal(appointment.Id, response.AppointmentId);
        Assert.Equal(appointment.DoctorId, response.DoctorId);
        Assert.Equal(Now.UtcDateTime, response.GrantedAtUtc);
        Assert.Equal(Now.UtcDateTime.AddHours(2), response.ExpiresAtUtc);

        var grant = Assert.Single(store.AddedGrants);
        Assert.Equal(response.GrantId, grant.Id);
        Assert.Equal(request.Id, grant.AccessRequestId);
        Assert.Equal(appointment.Id, grant.AppointmentId);
        Assert.Equal(appointment.DoctorId, grant.DoctorId);
        Assert.Equal(PatientId, grant.PatientId);
        Assert.Equal(Now.UtcDateTime, grant.GrantedAtUtc);
        Assert.Equal(Now.UtcDateTime.AddHours(2), grant.ExpiresAtUtc);
        Assert.True(grant.IsActiveAt(Now.UtcDateTime));

        Assert.Equal(AccessRequestStatus.Approved, request.Status);
        Assert.Equal(Now.UtcDateTime, credential.ConsumedAtUtc);
        Assert.Null(credential.RevokedAtUtc);
        Assert.Equal(1, store.SaveCount);

        // The scope that reached the store must be the one resolved from the appointment, not a client value
        // (FR-ACS-06).
        var scope = Assert.IsType<VisitAccessScope>(store.LastScope);
        Assert.Equal(appointment.Id, scope.AppointmentId);
        Assert.Equal(appointment.DoctorId, scope.DoctorId);
        Assert.Equal(PatientId, scope.PatientId);

        var audit = Assert.Single(auditWriter.AddedEntries);
        Assert.Equal(PatientId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Equal(AuditAction.AccessGrantCreated, audit.Action);
        Assert.Equal(AuditEntityType.AccessGrant, audit.EntityType);
        Assert.Equal(grant.Id.ToString(), audit.EntityId);
        Assert.Equal(IpAddress, audit.IpAddress);
        Assert.Equal(grant.Id, audit.AccessGrantId);
    }

    [Fact]
    public async Task ApproveAsync_WhenTheOnlyCredentialIsExpired_RevokesItAndStillCreatesTheGrant()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var expired = CreateCredential(request, Now.UtcDateTime.AddMinutes(-5));
        var store = CreateStore(appointment, request, [expired]);
        var (service, _) = CreateService(appointment, store);

        var result = await service.ApproveAsync(request.Id, PatientId, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Single(store.AddedGrants);
        Assert.Null(expired.ConsumedAtUtc);
        Assert.Equal(Now.UtcDateTime, expired.RevokedAtUtc);
    }

    [Fact]
    public async Task ApproveAsync_WhenTheRequestHasNoCredentials_StillCreatesTheGrant()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var store = CreateStore(appointment, request, []);
        var (service, _) = CreateService(appointment, store);

        var result = await service.ApproveAsync(request.Id, PatientId, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Single(store.AddedGrants);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task ApproveAsync_WhenTheRequestDoesNotExist_ReturnsRequestNotFoundWithoutCheckingScope()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessRequestDecisionStore();
        var scope = new FakeVisitAccessScopeService();
        var service = new AccessRequestDecisionService(
            scope,
            store,
            new FakeAuditWriter(),
            new FixedTimeProvider(Now));

        var result = await service.ApproveAsync(Guid.NewGuid(), PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.RequestNotFound, result.Failure);
        Assert.False(scope.WasPatientCalled);
        Assert.False(store.WasLoaded);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ApproveAsync_WhenAnotherPatientOwnsTheAppointment_ReturnsPatientMismatchWithoutLoading()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var store = CreateStore(appointment, request, []);
        var scope = new FakeVisitAccessScopeService
        {
            PatientResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.PatientMismatch)
        };
        var service = new AccessRequestDecisionService(
            scope,
            store,
            new FakeAuditWriter(),
            new FixedTimeProvider(Now));

        var result = await service.ApproveAsync(request.Id, Guid.NewGuid(), IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.PatientMismatch, result.Failure);
        Assert.False(store.WasLoaded);
        Assert.Empty(store.AddedGrants);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ApproveAsync_WhenTheAppointmentDisappeared_ReturnsRequestNotFound()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var store = CreateStore(appointment, request, []);
        var scope = new FakeVisitAccessScopeService
        {
            PatientResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.AppointmentNotFound)
        };
        var service = new AccessRequestDecisionService(
            scope,
            store,
            new FakeAuditWriter(),
            new FixedTimeProvider(Now));

        var result = await service.ApproveAsync(request.Id, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.RequestNotFound, result.Failure);
        Assert.False(store.WasLoaded);
        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData(AccessRequestStatus.Approved)]
    [InlineData(AccessRequestStatus.Rejected)]
    public async Task ApproveAsync_WhenTheRequestWasAlreadyAnswered_ReturnsAlreadyAnswered(
        AccessRequestStatus status)
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        if (status == AccessRequestStatus.Approved)
        {
            request.Approve();
        }
        else
        {
            request.Reject();
        }

        var store = CreateStore(appointment, request, []);
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.ApproveAsync(request.Id, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.AlreadyAnswered, result.Failure);
        Assert.Empty(store.AddedGrants);
        Assert.Empty(auditWriter.AddedEntries);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ApproveAsync_WhenTheStoreReportsAConflict_ReturnsRequestConflict()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var store = CreateStore(appointment, request, []);
        store.ConflictOnSave = true;
        var (service, _) = CreateService(appointment, store);

        var result = await service.ApproveAsync(request.Id, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.RequestConflict, result.Failure);
        Assert.Single(store.AddedGrants);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task RejectAsync_WhenThePatientOwnsTheAppointment_RejectsAndRevokesEveryLiveCredential()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var live = CreateCredential(request, Now.UtcDateTime.AddMinutes(-1));
        var expired = CreateCredential(request, Now.UtcDateTime.AddMinutes(-5));
        var store = CreateStore(appointment, request, [live, expired]);
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.RejectAsync(request.Id, PatientId, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Grant);
        Assert.Empty(store.AddedGrants);
        Assert.Equal(AccessRequestStatus.Rejected, request.Status);
        Assert.Equal(Now.UtcDateTime, live.RevokedAtUtc);
        Assert.Equal(Now.UtcDateTime, expired.RevokedAtUtc);
        Assert.Null(live.ConsumedAtUtc);
        Assert.Equal(1, store.SaveCount);

        var audit = Assert.Single(auditWriter.AddedEntries);
        Assert.Equal(PatientId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Equal(AuditAction.AccessRequestRejected, audit.Action);
        Assert.Equal(AuditEntityType.AccessRequest, audit.EntityType);
        Assert.Equal(request.Id.ToString(), audit.EntityId);
        Assert.Equal(IpAddress, audit.IpAddress);
        Assert.Null(audit.AccessGrantId);
    }

    [Fact]
    public async Task RejectAsync_WhenTheRequestDoesNotExist_ReturnsRequestNotFoundWithoutCheckingScope()
    {
        var store = new FakeAccessRequestDecisionStore();
        var scope = new FakeVisitAccessScopeService();
        var service = new AccessRequestDecisionService(
            scope,
            store,
            new FakeAuditWriter(),
            new FixedTimeProvider(Now));

        var result = await service.RejectAsync(Guid.NewGuid(), PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.RequestNotFound, result.Failure);
        Assert.False(scope.WasPatientCalled);
        Assert.False(store.WasLoaded);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RejectAsync_WhenAnotherPatientOwnsTheAppointment_ReturnsPatientMismatchWithoutLoading()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var store = CreateStore(appointment, request, []);
        var scope = new FakeVisitAccessScopeService
        {
            PatientResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.PatientMismatch)
        };
        var service = new AccessRequestDecisionService(
            scope,
            store,
            new FakeAuditWriter(),
            new FixedTimeProvider(Now));

        var result = await service.RejectAsync(request.Id, Guid.NewGuid(), IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.PatientMismatch, result.Failure);
        Assert.False(store.WasLoaded);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData(AccessRequestStatus.Approved)]
    [InlineData(AccessRequestStatus.Rejected)]
    public async Task RejectAsync_WhenTheRequestWasAlreadyAnswered_ReturnsAlreadyAnswered(
        AccessRequestStatus status)
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        if (status == AccessRequestStatus.Approved)
        {
            request.Approve();
        }
        else
        {
            request.Reject();
        }

        var store = CreateStore(appointment, request, []);
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.RejectAsync(request.Id, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.AlreadyAnswered, result.Failure);
        Assert.Empty(auditWriter.AddedEntries);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RejectAsync_WhenTheStoreReportsAConflict_ReturnsRequestConflict()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var store = CreateStore(appointment, request, []);
        store.ConflictOnSave = true;
        var (service, _) = CreateService(appointment, store);

        var result = await service.RejectAsync(request.Id, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.RequestConflict, result.Failure);
        Assert.Equal(1, store.SaveCount);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ApproveAsync_WithoutAnIpAddress_Throws(string? ipAddress)
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var store = CreateStore(appointment, request, []);
        var (service, _) = CreateService(appointment, store);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.ApproveAsync(request.Id, PatientId, ipAddress!));
    }

    [Fact]
    public async Task ApproveByTokenAsync_WhenThePatientOwnsTheAppointment_CreatesTheGrantAndConsumesTheToken()
    {
        const string token = "known-qr-token";
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, token, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateTokenStore(appointment, request, credential);
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.ApproveByTokenAsync(token, PatientId, IpAddress);

        Assert.True(result.IsSuccess);
        var grant = Assert.IsType<AccessGrantResponse>(result.Grant);
        Assert.Equal(request.Id, grant.AccessRequestId);
        Assert.Equal(appointment.Id, grant.AppointmentId);
        Assert.Equal(Now.UtcDateTime, grant.GrantedAtUtc);

        Assert.Equal(AccessRequestStatus.Approved, request.Status);
        Assert.Equal(Now.UtcDateTime, credential.ConsumedAtUtc);
        Assert.Null(credential.RevokedAtUtc);
        Assert.Single(store.AddedGrants);
        Assert.Equal(1, store.SaveCount);

        // The lookup is by the stored hash, never by the plaintext token (ADR-0001).
        Assert.Equal(QrTokenGenerator.Hash(token), store.LastTokenHash);

        var audit = Assert.Single(auditWriter.AddedEntries);
        Assert.Equal(AuditAction.AccessGrantCreated, audit.Action);
        Assert.Equal(PatientId, audit.PatientId);
    }

    [Fact]
    public async Task ApproveByTokenAsync_WhenNoTokenMatches_ReturnsTokenNotFoundWithoutSaving()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessRequestDecisionStore();
        var (service, _) = CreateService(appointment, store);

        var result = await service.ApproveByTokenAsync("unknown-token", PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.TokenNotFound, result.Failure);
        Assert.Equal(QrTokenGenerator.Hash("unknown-token"), store.LastTokenHash);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ApproveByTokenAsync_WhenAnotherPatientOwnsTheAppointment_ReturnsTokenNotFound()
    {
        const string token = "another-patients-token";
        var otherPatientId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var appointment = CreateAppointment(patientId: otherPatientId);
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, token, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateTokenStore(appointment, request, credential);
        var (service, _) = CreateService(appointment, store);

        var result = await service.ApproveByTokenAsync(token, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.TokenNotFound, result.Failure);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.Null(credential.ConsumedAtUtc);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ApproveByTokenAsync_WhenTheTokenExpired_ReturnsTokenUnusable()
    {
        const string token = "expired-token";
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, token, Now.UtcDateTime.AddMinutes(-5));
        var store = CreateTokenStore(appointment, request, credential);
        var (service, _) = CreateService(appointment, store);

        var result = await service.ApproveByTokenAsync(token, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.TokenUnusable, result.Failure);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ApproveByTokenAsync_WhenTheTokenWasRevoked_ReturnsTokenUnusable()
    {
        const string token = "revoked-token";
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, token, Now.UtcDateTime.AddMinutes(-1));
        credential.Revoke(Now.UtcDateTime.AddSeconds(-30));
        var store = CreateTokenStore(appointment, request, credential);
        var (service, _) = CreateService(appointment, store);

        var result = await service.ApproveByTokenAsync(token, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.TokenUnusable, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ApproveByTokenAsync_WhenTheRequestWasAlreadyAnswered_ReturnsTokenUnusable()
    {
        const string token = "answered-token";
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        request.Reject();
        var credential = CreateTokenCredential(request, token, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateTokenStore(appointment, request, credential);
        var (service, _) = CreateService(appointment, store);

        var result = await service.ApproveByTokenAsync(token, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.TokenUnusable, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ApproveByTokenAsync_WhenTheVisitIsCompleted_ReturnsVisitNotOpen()
    {
        const string token = "completed-visit-token";
        var appointment = CreateAppointment(AppointmentStatus.Completed);
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, token, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateTokenStore(appointment, request, credential);
        var (service, _) = CreateService(appointment, store);

        var result = await service.ApproveByTokenAsync(token, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.VisitNotOpen, result.Failure);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RejectByTokenAsync_WhenTheVisitIsCompleted_StillRejectsAndRevokesTheToken()
    {
        const string token = "completed-visit-reject-token";
        var appointment = CreateAppointment(AppointmentStatus.Completed);
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, token, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateTokenStore(appointment, request, credential);
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.RejectByTokenAsync(token, PatientId, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Grant);
        Assert.Equal(AccessRequestStatus.Rejected, request.Status);
        Assert.Equal(Now.UtcDateTime, credential.RevokedAtUtc);
        Assert.Null(credential.ConsumedAtUtc);
        Assert.Equal(1, store.SaveCount);

        var audit = Assert.Single(auditWriter.AddedEntries);
        Assert.Equal(AuditAction.AccessRequestRejected, audit.Action);
    }

    [Fact]
    public async Task ApproveAsync_WhenTheVisitIsCompleted_ReturnsVisitNotOpen()
    {
        var appointment = CreateAppointment(AppointmentStatus.Completed);
        var request = CreatePendingRequest(appointment);
        var store = CreateStore(appointment, request, []);
        var (service, _) = CreateService(appointment, store);

        var result = await service.ApproveAsync(request.Id, PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.VisitNotOpen, result.Failure);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RejectAsync_WhenTheVisitIsCompleted_StillRejects()
    {
        var appointment = CreateAppointment(AppointmentStatus.Completed);
        var request = CreatePendingRequest(appointment);
        var credential = CreateCredential(request, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, request, [credential]);
        var (service, _) = CreateService(appointment, store);

        var result = await service.RejectAsync(request.Id, PatientId, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccessRequestStatus.Rejected, request.Status);
        Assert.Equal(Now.UtcDateTime, credential.RevokedAtUtc);
    }

    private static (AccessRequestDecisionService Service, FakeAuditWriter AuditWriter) CreateService(
        Appointment appointment,
        FakeAccessRequestDecisionStore store)
    {
        var auditWriter = new FakeAuditWriter();
        var scope = FakeVisitAccessScopeService.AllowingPatient(
            new VisitAccessScope(appointment.Id, appointment.DoctorId, appointment.PatientId));

        return (
            new AccessRequestDecisionService(
                scope,
                store,
                auditWriter,
                new FixedTimeProvider(Now)),
            auditWriter);
    }

    private static FakeAccessRequestDecisionStore CreateStore(
        Appointment appointment,
        AccessRequest request,
        IReadOnlyList<VisitAccessCredential> credentials) =>
        new()
        {
            Request = request,
            Context = new AccessRequestDecisionContext(request, appointment, credentials)
        };

    private static FakeAccessRequestDecisionStore CreateTokenStore(
        Appointment appointment,
        AccessRequest request,
        VisitAccessCredential credential,
        IReadOnlyList<VisitAccessCredential>? credentials = null) =>
        new()
        {
            Request = request,
            Context = new AccessRequestDecisionContext(
                request,
                appointment,
                credentials ?? [credential]),
            TokenContext = new AccessRequestTokenContext(
                credential,
                new AccessRequestDecisionContext(
                    request,
                    appointment,
                    credentials ?? [credential]))
        };

    private static VisitAccessCredential CreateTokenCredential(
        AccessRequest request,
        string token,
        DateTime createdAtUtc) =>
        VisitAccessCredential.Create(
            request.Id,
            VisitCredentialType.QrToken,
            QrTokenGenerator.Hash(token),
            createdAtUtc);

    private static AccessRequest CreatePendingRequest(Appointment appointment) =>
        AccessRequest.Create(appointment, CreateDoctor(appointment.DoctorId), Now.UtcDateTime.AddMinutes(-5));

    private static VisitAccessCredential CreateCredential(AccessRequest request, DateTime createdAtUtc) =>
        VisitAccessCredential.Create(
            request.Id,
            VisitCredentialType.QrToken,
            Guid.NewGuid().ToString("N"),
            createdAtUtc);

    private static Appointment CreateAppointment(
        AppointmentStatus status = AppointmentStatus.CheckedIn,
        Guid? patientId = null)
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
            PatientId = patientId ?? PatientId,
            DoctorSlotId = slot.Id,
            DoctorSlot = slot,
            Status = status,
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
