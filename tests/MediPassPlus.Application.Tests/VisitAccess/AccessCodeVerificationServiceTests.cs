using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

public class AccessCodeVerificationServiceTests
{
    private const string Code = "042915";
    private const string IpAddress = "203.0.113.10";
    private const int AppointmentId = 42;

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly byte[] HmacKey =
        Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public async Task VerifyAsync_WhenTheCodeMatches_CreatesTheGrantAndMovesTheVisitToInVisit()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-1));
        var qr = CreateQrCredential(request, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential, qr]);
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<AccessGrantResponse>(result.Grant);
        Assert.Equal(request.Id, response.AccessRequestId);
        Assert.Equal(appointment.Id, response.AppointmentId);
        Assert.Equal(appointment.DoctorId, response.DoctorId);
        Assert.Equal(Now.UtcDateTime, response.GrantedAtUtc);
        Assert.Equal(Now.UtcDateTime.AddHours(2), response.ExpiresAtUtc);

        var grant = Assert.Single(store.AddedGrants);
        Assert.Equal(response.GrantId, grant.Id);
        Assert.Equal(AccessRequestStatus.Approved, request.Status);
        Assert.Equal(AppointmentStatus.InVisit, appointment.Status);
        Assert.Equal(Now.UtcDateTime, appointment.InVisitAtUtc);
        Assert.Equal(Now.UtcDateTime, credential.ConsumedAtUtc);
        Assert.Null(credential.RevokedAtUtc);
        Assert.Equal(Now.UtcDateTime, qr.RevokedAtUtc);
        Assert.Equal(1, store.SaveCount);

        var audit = Assert.Single(auditWriter.AddedEntries);
        Assert.Equal(doctor.UserId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Equal(AuditAction.AccessGrantCreated, audit.Action);
        Assert.Equal(AuditEntityType.AccessGrant, audit.EntityType);
        Assert.Equal(grant.Id.ToString(), audit.EntityId);
        Assert.Equal(IpAddress, audit.IpAddress);
        Assert.Equal(grant.Id, audit.AccessGrantId);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheVisitIsAlreadyInVisit_DoesNotChangeTheStatusAgain()
    {
        var appointment = CreateAppointment(status: AppointmentStatus.InVisit);
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential]);
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.InVisit, appointment.Status);
        Assert.Null(appointment.InVisitAtUtc);
        Assert.Single(store.AddedGrants);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheCodeIsWrong_RecordsTheAttemptAndChangesNothingElse()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-1));
        var qr = CreateQrCredential(request, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential, qr]);
        var (service, auditWriter) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, "999999", IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.InvalidCode, result.Failure);
        Assert.Equal(1, credential.FailedAttempts);
        Assert.Null(credential.LockedAtUtc);
        Assert.Null(credential.ConsumedAtUtc);
        Assert.Null(qr.RevokedAtUtc);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.Empty(store.AddedGrants);
        Assert.Empty(auditWriter.AddedEntries);
        Assert.Equal(1, store.FailedAttemptCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task VerifyAsync_WhenAConcurrentEntryLockedTheCode_ReturnsCodeLocked()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential]);
        store.ForcedFailedAttemptOutcome = AccessCodeFailedAttemptOutcome.AlreadyLocked;
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, "999999", IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.CodeLocked, result.Failure);
        Assert.Equal(1, store.FailedAttemptCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task VerifyAsync_WhenAConcurrentEntryConsumedTheCode_ReturnsCodeUnavailable()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential]);
        store.ForcedFailedAttemptOutcome = AccessCodeFailedAttemptOutcome.Unavailable;
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, "999999", IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.CodeUnavailable, result.Failure);
        Assert.Equal(1, store.FailedAttemptCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task VerifyAsync_AfterFiveWrongEntries_LocksTheCodeSoTheCorrectCodeFails()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential]);
        var (service, auditWriter) = CreateService(appointment, store);

        for (var attempt = 0; attempt < VisitAccessCredential.MaxFailedAttempts; attempt++)
        {
            var wrong = await service.VerifyAsync(appointment.Id, doctor.UserId, "999999", IpAddress);
            Assert.Equal(AccessCodeVerificationFailure.InvalidCode, wrong.Failure);
        }

        var correct = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);

        Assert.False(correct.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.CodeLocked, correct.Failure);
        Assert.Equal(VisitAccessCredential.MaxFailedAttempts, credential.FailedAttempts);
        Assert.NotNull(credential.LockedAtUtc);
        Assert.Empty(store.AddedGrants);
        Assert.Empty(auditWriter.AddedEntries);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheCodeIsExpired_ReturnsCodeUnavailable()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-5));
        var store = CreateStore(appointment, [request], [credential]);
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.CodeUnavailable, result.Failure);
        Assert.Empty(store.AddedGrants);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheCodeWasConsumed_ReturnsCodeUnavailable()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-2));
        credential.Consume(Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential]);
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.CodeUnavailable, result.Failure);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheCodeWasRevoked_ReturnsCodeUnavailable()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-1));
        credential.Revoke(Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential]);
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.CodeUnavailable, result.Failure);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheRequestHasNoNumericCode_ReturnsCodeUnavailable()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var qr = CreateQrCredential(request, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [qr]);
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.CodeUnavailable, result.Failure);
    }

    [Fact]
    public async Task VerifyAsync_WhenAnotherDoctorOwnsTheAppointment_ReturnsDoctorMismatchWithoutLoading()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessCodeStore();
        var scope = new FakeVisitAccessScopeService
        {
            DoctorResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.DoctorMismatch)
        };
        var service = CreateService(scope, store);

        var result = await service.VerifyAsync(appointment.Id, Guid.NewGuid(), Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.DoctorMismatch, result.Failure);
        Assert.False(store.WasLoaded);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheUserHasNoDoctorProfile_ReturnsDoctorNotFound()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessCodeStore();
        var scope = new FakeVisitAccessScopeService
        {
            DoctorResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.DoctorNotFound)
        };
        var service = CreateService(scope, store);

        var result = await service.VerifyAsync(appointment.Id, Guid.NewGuid(), Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.DoctorNotFound, result.Failure);
        Assert.False(store.WasLoaded);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheAppointmentIsGone_ReturnsAppointmentNotFound()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessCodeStore();
        var scope = new FakeVisitAccessScopeService
        {
            DoctorResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.AppointmentNotFound)
        };
        var service = CreateService(scope, store);

        var result = await service.VerifyAsync(appointment.Id, DoctorUserId, Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.AppointmentNotFound, result.Failure);
        Assert.False(store.WasLoaded);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheContextDisappears_ReturnsAppointmentNotFound()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessCodeStore { Context = null };
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, DoctorUserId, Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.AppointmentNotFound, result.Failure);
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task VerifyAsync_WhenTheAppointmentIsNotInAVerifiableState_ReturnsFailure(
        AppointmentStatus status)
    {
        var appointment = CreateAppointment(status: status);
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential]);
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.AppointmentNotCheckedIn, result.Failure);
        Assert.Empty(store.AddedGrants);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheRequestIsAlreadyApproved_ReturnsAccessAlreadyGranted()
    {
        var appointment = CreateAppointment(status: AppointmentStatus.InVisit);
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        request.Approve();
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential]);
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.AccessAlreadyGranted, result.Failure);
        Assert.Empty(store.AddedGrants);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheVisitHasNoRequest_ReturnsRequestNotPending()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessCodeStore
        {
            Context = new AccessCodeContext(appointment, [], [])
        };
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, DoctorUserId, Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.RequestNotPending, result.Failure);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheStoreReportsAConflict_ReturnsRequestConflict()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential]);
        store.ConflictOnSaveNumber = 1;
        var (service, _) = CreateService(appointment, store);

        var result = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.RequestConflict, result.Failure);
        Assert.Single(store.AddedGrants);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheSameCodeIsVerifiedTwice_RefusesTheReplay()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var credential = CreateCodeCredential(request, Code, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, [request], [credential]);
        var (service, _) = CreateService(appointment, store);

        var first = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);
        var replay = await service.VerifyAsync(appointment.Id, doctor.UserId, Code, IpAddress);

        Assert.True(first.IsSuccess);
        Assert.False(replay.IsSuccess);
        Assert.Equal(AccessCodeVerificationFailure.AccessAlreadyGranted, replay.Failure);
        Assert.Single(store.AddedGrants);
    }

    private static (AccessCodeVerificationService Service, FakeAuditWriter AuditWriter) CreateService(
        Appointment appointment,
        FakeAccessCodeStore store)
    {
        var auditWriter = new FakeAuditWriter();
        var service = new AccessCodeVerificationService(
            FakeVisitAccessScopeService.Allowing(
                new VisitAccessScope(appointment.Id, appointment.DoctorId, appointment.PatientId)),
            store,
            new HmacAccessCodeHasher(HmacKey),
            auditWriter,
            new FixedTimeProvider(Now));

        return (service, auditWriter);
    }

    private static AccessCodeVerificationService CreateService(
        FakeVisitAccessScopeService scope,
        FakeAccessCodeStore store) =>
        new(
            scope,
            store,
            new HmacAccessCodeHasher(HmacKey),
            new FakeAuditWriter(),
            new FixedTimeProvider(Now));

    private static FakeAccessCodeStore CreateStore(
        Appointment appointment,
        IReadOnlyList<AccessRequest> requests,
        IReadOnlyList<VisitAccessCredential> credentials) =>
        new()
        {
            Context = new AccessCodeContext(appointment, requests, credentials)
        };

    private static AccessRequest CreatePendingRequest(Appointment appointment, Doctor doctor) =>
        AccessRequest.Create(appointment, doctor, Now.UtcDateTime.AddMinutes(-1));

    private static VisitAccessCredential CreateCodeCredential(
        AccessRequest request,
        string code,
        DateTime createdAtUtc) =>
        VisitAccessCredential.Create(
            request.Id,
            VisitCredentialType.NumericCode,
            new HmacAccessCodeHasher(HmacKey).Hash(code),
            createdAtUtc);

    private static VisitAccessCredential CreateQrCredential(AccessRequest request, DateTime createdAtUtc) =>
        VisitAccessCredential.Create(
            request.Id,
            VisitCredentialType.QrToken,
            QrTokenGenerator.Hash("test-qr-token"),
            createdAtUtc);

    private static Appointment CreateAppointment(
        AppointmentStatus status = AppointmentStatus.CheckedIn,
        int doctorId = 7)
    {
        var start = Now.UtcDateTime.Date.AddHours(9);
        var slot = new DoctorSlot
        {
            Id = 1,
            DoctorId = doctorId,
            StartTimeUtc = start,
            EndTimeUtc = start.AddMinutes(30),
            IsAvailable = true
        };

        return new Appointment
        {
            Id = AppointmentId,
            DoctorId = doctorId,
            PatientId = PatientId,
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
            UserId = DoctorUserId,
            Specialty = "General",
            ClinicId = 1
        };
}
