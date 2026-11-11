using MediPassPlus.Application.Common;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

public class StartVisitServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string IpAddress = "203.0.113.10";

    [Fact]
    public async Task StartAsync_WhenAppointmentIsTodayAndCheckedIn_CreatesPendingRequestAndQrCredential()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var store = new FakeVisitStartStore { Context = CreateContext(appointment, doctor) };
        var (service, auditWriter) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<StartVisitResponse>(result.Response);
        Assert.Equal(appointment.Id, response.AppointmentId);
        Assert.False(string.IsNullOrWhiteSpace(response.QrToken));
        Assert.Equal(Now.UtcDateTime.AddMinutes(2), response.ExpiresAtUtc);
        Assert.Equal(AppointmentStatus.InVisit, appointment.Status);
        Assert.Equal(Now.UtcDateTime, appointment.InVisitAtUtc);

        var request = Assert.Single(store.AddedRequests);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.Equal(appointment.Id, request.AppointmentId);
        Assert.Equal(doctor.Id, request.DoctorId);
        Assert.Equal(PatientId, request.PatientId);
        Assert.Equal(request.Id, response.AccessRequestId);

        var credential = Assert.Single(store.AddedCredentials);
        Assert.Equal(request.Id, credential.AccessRequestId);
        Assert.Equal(VisitCredentialType.QrToken, credential.Type);
        Assert.Equal(Now.UtcDateTime, credential.CreatedAtUtc);
        Assert.Equal(Now.UtcDateTime.AddMinutes(2), credential.ExpiresAtUtc);
        Assert.Equal(QrTokenGenerator.Hash(response.QrToken), credential.SecretHash);
        Assert.NotEqual(response.QrToken, credential.SecretHash);
        Assert.Null(credential.ConsumedAtUtc);
        Assert.True(credential.IsUsableAt(Now.UtcDateTime));
        Assert.Equal(1, store.SaveCount);

        var audit = Assert.Single(auditWriter.AddedEntries);
        Assert.Equal(doctor.UserId, audit.UserId);
        Assert.Equal(appointment.PatientId, audit.PatientId);
        Assert.Equal(AuditAction.AccessRequestCreated, audit.Action);
        Assert.Equal(AuditEntityType.AccessRequest, audit.EntityType);
        Assert.Equal(request.Id.ToString(), audit.EntityId);
        Assert.Equal(IpAddress, audit.IpAddress);
        Assert.Null(audit.AccessGrantId);
    }

    [Fact]
    public async Task StartAsync_WhenAPendingRequestExists_ReusesItAndRevokesTheOldQr()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var pending = AccessRequest.Create(appointment, doctor, Now.UtcDateTime.AddMinutes(-5));
        var oldCredential = VisitAccessCredential.Create(
            pending.Id,
            VisitCredentialType.QrToken,
            "old-hash",
            Now.UtcDateTime.AddMinutes(-1));
        var context = CreateContext(appointment, doctor, [pending], [oldCredential]);
        var store = new FakeVisitStartStore { Context = context };
        var (service, auditWriter) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Empty(store.AddedRequests);
        Assert.Empty(auditWriter.AddedEntries);
        var replacement = Assert.Single(store.AddedCredentials);
        Assert.Equal(pending.Id, replacement.AccessRequestId);
        Assert.Equal(pending.Id, result.Response!.AccessRequestId);
        Assert.Equal(Now.UtcDateTime, oldCredential.RevokedAtUtc);
        Assert.False(oldCredential.IsUsableAt(Now.UtcDateTime));
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task StartAsync_WhenTheOldQrIsExpiredButNotRevoked_RevokesItBeforeCreatingTheNewOne()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var pending = AccessRequest.Create(appointment, doctor, Now.UtcDateTime.AddMinutes(-10));
        var expired = VisitAccessCredential.Create(
            pending.Id,
            VisitCredentialType.QrToken,
            "expired-hash",
            Now.UtcDateTime.AddMinutes(-5));
        var context = CreateContext(appointment, doctor, [pending], [expired]);
        var store = new FakeVisitStartStore { Context = context };
        var (service, _) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.UtcDateTime, expired.RevokedAtUtc);
        Assert.Single(store.AddedCredentials);
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task StartAsync_WhenTheAppointmentIsNotCheckedIn_ReturnsFailureWithoutWriting(
        AppointmentStatus status)
    {
        var appointment = CreateAppointment(status: status);
        var doctor = CreateDoctor(appointment.DoctorId);
        var store = new FakeVisitStartStore { Context = CreateContext(appointment, doctor) };
        var (service, _) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(StartVisitFailure.AppointmentNotCheckedIn, result.Failure);
        Assert.Empty(store.AddedRequests);
        Assert.Empty(store.AddedCredentials);
        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task StartAsync_WhenTheAppointmentIsNotToday_ReturnsFailureWithoutWriting(int dayOffset)
    {
        var appointment = CreateAppointment(slotStartUtc: Now.UtcDateTime.AddDays(dayOffset));
        var doctor = CreateDoctor(appointment.DoctorId);
        var store = new FakeVisitStartStore { Context = CreateContext(appointment, doctor) };
        var (service, _) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(StartVisitFailure.AppointmentNotToday, result.Failure);
        Assert.Empty(store.AddedRequests);
        Assert.Empty(store.AddedCredentials);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task StartAsync_WhenTheAppointmentIsAlreadyInVisit_ReissuesTheQrWithoutChangingTheState()
    {
        var appointment = CreateAppointment(status: AppointmentStatus.InVisit);
        appointment.InVisitAtUtc = Now.UtcDateTime.AddMinutes(-5);
        var doctor = CreateDoctor(appointment.DoctorId);
        var pending = AccessRequest.Create(appointment, doctor, Now.UtcDateTime.AddMinutes(-10));
        var store = new FakeVisitStartStore { Context = CreateContext(appointment, doctor, [pending]) };
        var (service, _) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.InVisit, appointment.Status);
        Assert.Equal(Now.UtcDateTime.AddMinutes(-5), appointment.InVisitAtUtc);
        Assert.Equal(pending.Id, result.Response!.AccessRequestId);
        Assert.Single(store.AddedCredentials);
    }

    [Fact]
    public async Task StartAsync_WhenTheAppointmentIsNotToday_DoesNotChangeTheStatus()
    {
        var appointment = CreateAppointment(slotStartUtc: Now.UtcDateTime.AddDays(1));
        var doctor = CreateDoctor(appointment.DoctorId);
        var store = new FakeVisitStartStore { Context = CreateContext(appointment, doctor) };
        var (service, _) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(StartVisitFailure.AppointmentNotToday, result.Failure);
        Assert.Equal(AppointmentStatus.CheckedIn, appointment.Status);
        Assert.Null(appointment.InVisitAtUtc);
    }

    [Fact]
    public async Task StartAsync_WhenTheLastRequestWasApproved_ReturnsAccessAlreadyGrantedWithoutWriting()
    {
        var appointment = CreateAppointment(status: AppointmentStatus.InVisit);
        appointment.InVisitAtUtc = Now.UtcDateTime.AddMinutes(-5);
        var doctor = CreateDoctor(appointment.DoctorId);
        var approved = AccessRequest.Create(appointment, doctor, Now.UtcDateTime.AddMinutes(-10));
        approved.Approve();
        var store = new FakeVisitStartStore { Context = CreateContext(appointment, doctor, [approved]) };
        var (service, auditWriter) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(StartVisitFailure.AccessAlreadyGranted, result.Failure);
        Assert.Equal(AppointmentStatus.InVisit, appointment.Status);
        Assert.Equal(Now.UtcDateTime.AddMinutes(-5), appointment.InVisitAtUtc);
        Assert.Empty(store.AddedRequests);
        Assert.Empty(store.AddedCredentials);
        Assert.Empty(auditWriter.AddedEntries);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task StartAsync_WhenTheLastRequestWasRejected_CreatesANewPendingRequest()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var rejected = AccessRequest.Create(appointment, doctor, Now.UtcDateTime.AddMinutes(-5));
        rejected.Reject();
        var store = new FakeVisitStartStore { Context = CreateContext(appointment, doctor, [rejected]) };
        var (service, auditWriter) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(store.AddedRequests);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.NotEqual(rejected.Id, request.Id);
        Assert.Equal(request.Id, result.Response!.AccessRequestId);
        var audit = Assert.Single(auditWriter.AddedEntries);
        Assert.Equal(request.Id.ToString(), audit.EntityId);
    }

    [Fact]
    public async Task StartAsync_WhenAConcurrentCallCreatedThePendingRequest_ReturnsRequestConflict()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var store = new FakeVisitStartStore
        {
            Context = CreateContext(appointment, doctor),
            ConflictOnSaveNumber = 1
        };
        var (service, _) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(StartVisitFailure.RequestConflict, result.Failure);
        Assert.Single(store.AddedRequests);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task StartAsync_WhenAConcurrentCallReplacedTheQrCredential_ReturnsRequestConflict()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var pending = AccessRequest.Create(appointment, doctor, Now.UtcDateTime.AddMinutes(-5));
        var live = VisitAccessCredential.Create(
            pending.Id,
            VisitCredentialType.QrToken,
            "live-hash",
            Now.UtcDateTime.AddMinutes(-1));
        var store = new FakeVisitStartStore
        {
            Context = CreateContext(appointment, doctor, [pending], [live]),
            ConflictOnSaveNumber = 2
        };
        var (service, _) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(StartVisitFailure.RequestConflict, result.Failure);
        Assert.Empty(store.AddedRequests);
        Assert.Equal(Now.UtcDateTime, live.RevokedAtUtc);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task StartAsync_IssuedCredential_CannotBeConsumedTwice()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var store = new FakeVisitStartStore { Context = CreateContext(appointment, doctor) };
        var (service, _) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.True(result.IsSuccess);

        // Proves the credential the endpoint hands out enforces single use (FR-ACS-02, ADR-0001). The QR-redeeming
        // endpoint that loads and consumes it is a later ticket, so the test exercises the same domain operation.
        var credential = Assert.Single(store.AddedCredentials);
        credential.Consume(Now.UtcDateTime.AddSeconds(30));

        Assert.Throws<InvalidOperationException>(
            () => credential.Consume(Now.UtcDateTime.AddSeconds(31)));
    }

    [Fact]
    public async Task StartAsync_WhenScopeDeniesTheAppointment_ReturnsAppointmentNotFoundWithoutLoading()
    {
        var store = new FakeVisitStartStore();
        var scope = new FakeVisitAccessScopeService
        {
            DoctorResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.AppointmentNotFound)
        };
        var service = new StartVisitService(
            scope,
            store,
            new FakeAuditWriter(),
            new CairoClinicTimeZone(),
            new FixedTimeProvider(Now));

        var result = await service.StartAsync(42, Guid.NewGuid(), IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(StartVisitFailure.AppointmentNotFound, result.Failure);
        Assert.False(store.WasLoaded);
    }

    [Fact]
    public async Task StartAsync_WhenScopeDeniesAnotherDoctorsAppointment_ReturnsDoctorMismatch()
    {
        var store = new FakeVisitStartStore();
        var scope = new FakeVisitAccessScopeService
        {
            DoctorResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.DoctorMismatch)
        };
        var service = new StartVisitService(
            scope,
            store,
            new FakeAuditWriter(),
            new CairoClinicTimeZone(),
            new FixedTimeProvider(Now));

        var result = await service.StartAsync(42, Guid.NewGuid(), IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(StartVisitFailure.DoctorMismatch, result.Failure);
        Assert.False(store.WasLoaded);
    }

    [Fact]
    public async Task StartAsync_WhenTheContextDisappears_ReturnsAppointmentNotFound()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var store = new FakeVisitStartStore { Context = null };
        var (service, _) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(StartVisitFailure.AppointmentNotFound, result.Failure);
        Assert.Empty(store.AddedRequests);
        Assert.Empty(store.AddedCredentials);
    }

    [Fact]
    public async Task StartAsync_WhenSeveralPendingRequestsExist_ReusesTheNewestAndRevokesEveryLiveQr()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var older = AccessRequest.Create(appointment, doctor, Now.UtcDateTime.AddMinutes(-10));
        var newer = AccessRequest.Create(appointment, doctor, Now.UtcDateTime.AddMinutes(-5));
        var olderCredential = VisitAccessCredential.Create(
            older.Id,
            VisitCredentialType.QrToken,
            "older-hash",
            Now.UtcDateTime.AddMinutes(-1));
        var newerCredential = VisitAccessCredential.Create(
            newer.Id,
            VisitCredentialType.QrToken,
            "newer-hash",
            Now.UtcDateTime.AddMinutes(-1));
        var context = CreateContext(
            appointment,
            doctor,
            [newer, older],
            [olderCredential, newerCredential]);
        var store = new FakeVisitStartStore { Context = context };
        var (service, _) = CreateService(appointment, doctor, store);

        var result = await service.StartAsync(appointment.Id, doctor.UserId, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal(newer.Id, result.Response!.AccessRequestId);
        Assert.Empty(store.AddedRequests);
        Assert.Equal(Now.UtcDateTime, olderCredential.RevokedAtUtc);
        Assert.Equal(Now.UtcDateTime, newerCredential.RevokedAtUtc);
        var replacement = Assert.Single(store.AddedCredentials);
        Assert.Equal(newer.Id, replacement.AccessRequestId);
    }

    private static (StartVisitService Service, FakeAuditWriter AuditWriter) CreateService(
        Appointment appointment,
        Doctor doctor,
        FakeVisitStartStore store)
    {
        var auditWriter = new FakeAuditWriter();

        return (
            new StartVisitService(
                FakeVisitAccessScopeService.Allowing(
                    new VisitAccessScope(appointment.Id, doctor.Id, appointment.PatientId)),
                store,
                auditWriter,
                new CairoClinicTimeZone(),
                new FixedTimeProvider(Now)),
            auditWriter);
    }

    private static StartVisitContext CreateContext(
        Appointment appointment,
        Doctor doctor,
        IReadOnlyList<AccessRequest>? requests = null,
        IReadOnlyList<VisitAccessCredential>? liveQrCredentials = null) =>
        new(
            appointment,
            doctor,
            requests ?? Array.Empty<AccessRequest>(),
            liveQrCredentials ?? Array.Empty<VisitAccessCredential>());

    private static Appointment CreateAppointment(
        AppointmentStatus status = AppointmentStatus.CheckedIn,
        DateTime? slotStartUtc = null,
        int doctorId = 7)
    {
        var start = slotStartUtc ?? Now.UtcDateTime.Date.AddHours(9);
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
            Id = 42,
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
            UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Specialty = "General",
            ClinicId = 1
        };
}
