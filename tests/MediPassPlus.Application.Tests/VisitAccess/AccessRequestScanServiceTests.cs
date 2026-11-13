using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

public class AccessRequestScanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Token = "a-scanned-qr-token";

    [Fact]
    public async Task ScanAsync_WhenThePatientOwnsTheToken_ReturnsTheDoctorAndClinicDetails()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, Token, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, request, credential);
        var service = CreateService(store, new FakeQrImageDecoder());

        var result = await service.ScanAsync(Token, PatientId);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<AccessRequestScanResponse>(result.Response);
        Assert.Equal(request.Id, response.AccessRequestId);
        Assert.Equal(appointment.Id, response.AppointmentId);
        Assert.Equal("Doctor One", response.DoctorName);
        Assert.Equal("General", response.DoctorSpecialty);
        Assert.Equal("Clinic One", response.ClinicName);
        Assert.Equal("1 Main Street", response.ClinicAddress);
        Assert.Equal(appointment.DoctorSlot.StartTimeUtc, response.AppointmentStartsAtUtc);
        Assert.Equal(credential.ExpiresAtUtc, response.ExpiresAtUtc);

        // The lookup is by the stored hash, never by the plaintext token (ADR-0001).
        Assert.Equal(QrTokenGenerator.Hash(Token), store.LastTokenHash);
    }

    [Fact]
    public async Task ScanAsync_DoesNotConsumeOrRevokeTheToken()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, Token, Now.UtcDateTime.AddMinutes(-1));
        var service = CreateService(CreateStore(appointment, request, credential), new FakeQrImageDecoder());

        var result = await service.ScanAsync(Token, PatientId);

        Assert.True(result.IsSuccess);
        Assert.Null(credential.ConsumedAtUtc);
        Assert.Null(credential.RevokedAtUtc);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
    }

    [Fact]
    public async Task ScanAsync_WhenNoTokenMatches_ReturnsTokenNotFound()
    {
        var store = new FakeAccessRequestScanStore();
        var service = CreateService(store, new FakeQrImageDecoder());

        var result = await service.ScanAsync("unknown-token", PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestScanFailure.TokenNotFound, result.Failure);
        Assert.Equal(QrTokenGenerator.Hash("unknown-token"), store.LastTokenHash);
    }

    [Fact]
    public async Task ScanAsync_WhenAnotherPatientOwnsTheAppointment_ReturnsTokenNotFound()
    {
        var otherPatientId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var appointment = CreateAppointment(patientId: otherPatientId);
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, Token, Now.UtcDateTime.AddMinutes(-1));
        var service = CreateService(
            CreateStore(appointment, request, credential),
            new FakeQrImageDecoder());

        var result = await service.ScanAsync(Token, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestScanFailure.TokenNotFound, result.Failure);
    }

    [Fact]
    public async Task ScanAsync_WhenTheTokenExpired_ReturnsTokenUnusable()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, Token, Now.UtcDateTime.AddMinutes(-5));
        var service = CreateService(
            CreateStore(appointment, request, credential),
            new FakeQrImageDecoder());

        var result = await service.ScanAsync(Token, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestScanFailure.TokenUnusable, result.Failure);
    }

    [Fact]
    public async Task ScanAsync_WhenTheTokenWasRevoked_ReturnsTokenUnusable()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, Token, Now.UtcDateTime.AddMinutes(-1));
        credential.Revoke(Now.UtcDateTime.AddSeconds(-30));
        var service = CreateService(
            CreateStore(appointment, request, credential),
            new FakeQrImageDecoder());

        var result = await service.ScanAsync(Token, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestScanFailure.TokenUnusable, result.Failure);
    }

    [Fact]
    public async Task ScanAsync_WhenTheTokenWasAlreadyConsumed_ReturnsTokenUnusable()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, Token, Now.UtcDateTime.AddMinutes(-1));
        credential.Consume(Now.UtcDateTime.AddSeconds(-30));
        var service = CreateService(
            CreateStore(appointment, request, credential),
            new FakeQrImageDecoder());

        var result = await service.ScanAsync(Token, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestScanFailure.TokenUnusable, result.Failure);
    }

    [Fact]
    public async Task ScanAsync_WhenTheRequestWasAlreadyAnswered_ReturnsTokenUnusable()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        request.Reject();
        var credential = CreateTokenCredential(request, Token, Now.UtcDateTime.AddMinutes(-1));
        var service = CreateService(
            CreateStore(appointment, request, credential),
            new FakeQrImageDecoder());

        var result = await service.ScanAsync(Token, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestScanFailure.TokenUnusable, result.Failure);
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task ScanAsync_WhenTheVisitIsNotInProgress_ReturnsVisitNotOpen(
        AppointmentStatus status)
    {
        var appointment = CreateAppointment(status);
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, Token, Now.UtcDateTime.AddMinutes(-1));
        var service = CreateService(
            CreateStore(appointment, request, credential),
            new FakeQrImageDecoder());

        var result = await service.ScanAsync(Token, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestScanFailure.VisitNotOpen, result.Failure);
    }

    [Fact]
    public async Task ScanAsync_WhenTheVisitIsInVisit_ReturnsTheDetails()
    {
        var appointment = CreateAppointment(AppointmentStatus.InVisit);
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, Token, Now.UtcDateTime.AddMinutes(-1));
        var service = CreateService(
            CreateStore(appointment, request, credential),
            new FakeQrImageDecoder());

        var result = await service.ScanAsync(Token, PatientId);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ScanAsync_WithoutAToken_Throws(string? token)
    {
        var service = CreateService(new FakeAccessRequestScanStore(), new FakeQrImageDecoder());

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.ScanAsync(token!, PatientId));
    }

    [Fact]
    public async Task ScanImageAsync_WhenTheImageDecodes_ScansTheDecodedToken()
    {
        var appointment = CreateAppointment();
        var request = CreatePendingRequest(appointment);
        var credential = CreateTokenCredential(request, Token, Now.UtcDateTime.AddMinutes(-1));
        var store = CreateStore(appointment, request, credential);
        var decoder = new FakeQrImageDecoder { DecodedToken = Token };
        var service = CreateService(store, decoder);
        var image = new byte[] { 1, 2, 3, 4 };

        var result = await service.ScanImageAsync(image, PatientId);

        Assert.True(result.IsSuccess);
        Assert.Equal(image.Length, decoder.LastImageLength);
        Assert.Equal(QrTokenGenerator.Hash(Token), store.LastTokenHash);
    }

    [Fact]
    public async Task ScanImageAsync_WhenTheImageIsEmpty_ReturnsImageNotReadable()
    {
        var decoder = new FakeQrImageDecoder { DecodedToken = Token };
        var service = CreateService(new FakeAccessRequestScanStore(), decoder);

        var result = await service.ScanImageAsync([], PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestScanFailure.ImageNotReadable, result.Failure);
        Assert.Null(decoder.LastImageLength);
    }

    [Fact]
    public async Task ScanImageAsync_WhenTheImageIsTooLarge_ReturnsImageNotReadable()
    {
        var decoder = new FakeQrImageDecoder { DecodedToken = Token };
        var service = CreateService(new FakeAccessRequestScanStore(), decoder);
        var image = new byte[AccessRequestScanService.MaxImageBytes + 1];

        var result = await service.ScanImageAsync(image, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestScanFailure.ImageNotReadable, result.Failure);
        Assert.Null(decoder.LastImageLength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ScanImageAsync_WhenNoQrIsFound_ReturnsImageNotReadable(string? decoded)
    {
        var decoder = new FakeQrImageDecoder { DecodedToken = decoded };
        var service = CreateService(new FakeAccessRequestScanStore(), decoder);

        var result = await service.ScanImageAsync([1, 2, 3], PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestScanFailure.ImageNotReadable, result.Failure);
    }

    [Fact]
    public async Task ScanImageAsync_WhenTheImageIsNull_Throws()
    {
        var service = CreateService(new FakeAccessRequestScanStore(), new FakeQrImageDecoder());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.ScanImageAsync(null!, PatientId));
    }

    private static AccessRequestScanService CreateService(
        FakeAccessRequestScanStore store,
        FakeQrImageDecoder decoder) =>
        new(store, decoder, new FixedTimeProvider(Now));

    private static FakeAccessRequestScanStore CreateStore(
        Appointment appointment,
        AccessRequest request,
        VisitAccessCredential credential) =>
        new()
        {
            Context = new AccessRequestScanContext(
                credential,
                request,
                appointment,
                "Doctor One",
                "General",
                "Clinic One",
                "1 Main Street",
                appointment.DoctorSlot.StartTimeUtc)
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
