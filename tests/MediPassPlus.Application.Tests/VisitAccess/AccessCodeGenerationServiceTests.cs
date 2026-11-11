using MediPassPlus.Application.Common;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

public class AccessCodeGenerationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly byte[] HmacKey =
        Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public async Task GenerateAsync_WhenThePatientOwnsAPendingRequest_ReturnsASixDigitCodeAndStoresOnlyTheHmac()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var store = new FakeAccessCodeStore
        {
            Context = new AccessCodeContext(appointment, [request], [])
        };
        var hasher = new HmacAccessCodeHasher(HmacKey);
        var service = CreateService(appointment, store, hasher);

        var result = await service.GenerateAsync(appointment.Id, PatientId);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<AccessCodeResponse>(result.Response);
        Assert.Equal(appointment.Id, response.AppointmentId);
        Assert.Equal(request.Id, response.AccessRequestId);
        Assert.Matches("^[0-9]{6}$", response.Code);
        Assert.Equal(Now.UtcDateTime.AddMinutes(5), response.ExpiresAtUtc);

        var credential = Assert.Single(store.AddedCredentials);
        Assert.Equal(request.Id, credential.AccessRequestId);
        Assert.Equal(VisitCredentialType.NumericCode, credential.Type);
        Assert.Equal(hasher.Hash(response.Code), credential.SecretHash);
        Assert.NotEqual(response.Code, credential.SecretHash);
        Assert.Equal(Now.UtcDateTime, credential.CreatedAtUtc);
        Assert.Equal(Now.UtcDateTime.AddMinutes(5), credential.ExpiresAtUtc);
        Assert.True(credential.IsUsableAt(Now.UtcDateTime));
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task GenerateAsync_WhenALiveCodeExists_RevokesItBeforeAddingTheReplacement()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var previous = VisitAccessCredential.Create(
            request.Id,
            VisitCredentialType.NumericCode,
            "previous-hash",
            Now.UtcDateTime.AddMinutes(-1));
        var store = new FakeAccessCodeStore
        {
            Context = new AccessCodeContext(appointment, [request], [previous])
        };
        var service = CreateService(appointment, store, new HmacAccessCodeHasher(HmacKey));

        var result = await service.GenerateAsync(appointment.Id, PatientId);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.UtcDateTime, previous.RevokedAtUtc);
        Assert.Single(store.AddedCredentials);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task GenerateAsync_WhenThePreviousCodeIsLocked_ReplacesItAndLeavesTheQrTokenLive()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var locked = VisitAccessCredential.Create(
            request.Id,
            VisitCredentialType.NumericCode,
            "locked-hash",
            Now.UtcDateTime.AddMinutes(-1));
        for (var attempt = 0; attempt < VisitAccessCredential.MaxFailedAttempts; attempt++)
        {
            locked.RegisterFailedAttempt(Now.UtcDateTime);
        }

        var qr = VisitAccessCredential.Create(
            request.Id,
            VisitCredentialType.QrToken,
            "qr-hash",
            Now.UtcDateTime.AddMinutes(-1));
        var store = new FakeAccessCodeStore
        {
            Context = new AccessCodeContext(appointment, [request], [locked, qr])
        };
        var service = CreateService(appointment, store, new HmacAccessCodeHasher(HmacKey));

        var result = await service.GenerateAsync(appointment.Id, PatientId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(locked.LockedAtUtc);
        Assert.Equal(Now.UtcDateTime, locked.RevokedAtUtc);
        Assert.Null(qr.RevokedAtUtc);
        Assert.Single(store.AddedCredentials);
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task GenerateAsync_WhenTheAppointmentIsNotCheckedIn_ReturnsFailureWithoutWriting(
        AppointmentStatus status)
    {
        var appointment = CreateAppointment(status: status);
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var store = new FakeAccessCodeStore
        {
            Context = new AccessCodeContext(appointment, [request], [])
        };
        var service = CreateService(appointment, store, new HmacAccessCodeHasher(HmacKey));

        var result = await service.GenerateAsync(appointment.Id, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeGenerationFailure.AppointmentNotCheckedIn, result.Failure);
        Assert.Empty(store.AddedCredentials);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task GenerateAsync_WhenTheAppointmentIsNotToday_ReturnsAppointmentNotToday()
    {
        var appointment = CreateAppointment(
            slotStartUtc: Now.UtcDateTime.Date.AddDays(1).AddHours(9));
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var store = new FakeAccessCodeStore
        {
            Context = new AccessCodeContext(appointment, [request], [])
        };
        var service = CreateService(appointment, store, new HmacAccessCodeHasher(HmacKey));

        var result = await service.GenerateAsync(appointment.Id, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeGenerationFailure.AppointmentNotToday, result.Failure);
        Assert.Empty(store.AddedCredentials);
    }

    [Fact]
    public async Task GenerateAsync_WhenNoPendingRequestExists_ReturnsRequestNotPending()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessCodeStore
        {
            Context = new AccessCodeContext(appointment, [], [])
        };
        var service = CreateService(appointment, store, new HmacAccessCodeHasher(HmacKey));

        var result = await service.GenerateAsync(appointment.Id, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeGenerationFailure.RequestNotPending, result.Failure);
        Assert.Empty(store.AddedCredentials);
    }

    [Fact]
    public async Task GenerateAsync_WhenTheRequestIsAlreadyApproved_ReturnsAccessAlreadyGranted()
    {
        var appointment = CreateAppointment(status: AppointmentStatus.InVisit);
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        request.Approve();
        var store = new FakeAccessCodeStore
        {
            Context = new AccessCodeContext(appointment, [request], [])
        };
        var service = CreateService(appointment, store, new HmacAccessCodeHasher(HmacKey));

        var result = await service.GenerateAsync(appointment.Id, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeGenerationFailure.AccessAlreadyGranted, result.Failure);
        Assert.Empty(store.AddedCredentials);
    }

    [Fact]
    public async Task GenerateAsync_WhenAnotherPatientOwnsTheAppointment_ReturnsPatientMismatchWithoutLoading()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessCodeStore();
        var scope = new FakeVisitAccessScopeService
        {
            PatientResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.PatientMismatch)
        };
        var service = new AccessCodeGenerationService(
            scope,
            store,
            new HmacAccessCodeHasher(HmacKey),
            new CairoClinicTimeZone(),
            new FixedTimeProvider(Now));

        var result = await service.GenerateAsync(appointment.Id, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeGenerationFailure.PatientMismatch, result.Failure);
        Assert.False(store.WasLoaded);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task GenerateAsync_WhenTheAppointmentIsGone_ReturnsAppointmentNotFound()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessCodeStore();
        var scope = new FakeVisitAccessScopeService
        {
            PatientResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.AppointmentNotFound)
        };
        var service = new AccessCodeGenerationService(
            scope,
            store,
            new HmacAccessCodeHasher(HmacKey),
            new CairoClinicTimeZone(),
            new FixedTimeProvider(Now));

        var result = await service.GenerateAsync(appointment.Id, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeGenerationFailure.AppointmentNotFound, result.Failure);
        Assert.False(store.WasLoaded);
    }

    [Fact]
    public async Task GenerateAsync_WhenTheContextDisappears_ReturnsAppointmentNotFound()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessCodeStore { Context = null };
        var service = CreateService(appointment, store, new HmacAccessCodeHasher(HmacKey));

        var result = await service.GenerateAsync(appointment.Id, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeGenerationFailure.AppointmentNotFound, result.Failure);
        Assert.Empty(store.AddedCredentials);
    }

    [Fact]
    public async Task GenerateAsync_WhenTheReplacementSaveConflicts_ReturnsRequestConflict()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var previous = VisitAccessCredential.Create(
            request.Id,
            VisitCredentialType.NumericCode,
            "previous-hash",
            Now.UtcDateTime.AddMinutes(-1));
        var store = new FakeAccessCodeStore
        {
            Context = new AccessCodeContext(appointment, [request], [previous]),
            ConflictOnSaveNumber = 1
        };
        var service = CreateService(appointment, store, new HmacAccessCodeHasher(HmacKey));

        var result = await service.GenerateAsync(appointment.Id, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeGenerationFailure.RequestConflict, result.Failure);
        Assert.Empty(store.AddedCredentials);
    }

    [Fact]
    public async Task GenerateAsync_WhenTheInsertSaveConflicts_ReturnsRequestConflict()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var request = CreatePendingRequest(appointment, doctor);
        var store = new FakeAccessCodeStore
        {
            Context = new AccessCodeContext(appointment, [request], []),
            ConflictOnSaveNumber = 1
        };
        var service = CreateService(appointment, store, new HmacAccessCodeHasher(HmacKey));

        var result = await service.GenerateAsync(appointment.Id, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessCodeGenerationFailure.RequestConflict, result.Failure);
        Assert.Single(store.AddedCredentials);
    }

    private static AccessCodeGenerationService CreateService(
        Appointment appointment,
        FakeAccessCodeStore store,
        IAccessCodeHasher hasher) =>
        new(
            FakeVisitAccessScopeService.AllowingPatient(
                new VisitAccessScope(appointment.Id, appointment.DoctorId, appointment.PatientId)),
            store,
            hasher,
            new CairoClinicTimeZone(),
            new FixedTimeProvider(Now));

    private static AccessRequest CreatePendingRequest(Appointment appointment, Doctor doctor) =>
        AccessRequest.Create(appointment, doctor, Now.UtcDateTime.AddMinutes(-1));

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
