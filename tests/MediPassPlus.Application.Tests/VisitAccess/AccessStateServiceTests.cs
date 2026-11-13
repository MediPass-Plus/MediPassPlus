using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

public class AccessStateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task GetForDoctorAsync_WhenTheGrantIsActive_ReturnsActiveAndTheRemainingSeconds()
    {
        var appointment = CreateAppointment();
        var request = CreateApprovedRequest(appointment);
        var grant = AccessGrant.Create(request, appointment, Now.UtcDateTime.AddMinutes(-30));
        var store = new FakeAccessStateStore { Context = new AccessStateContext(request, grant) };
        var service = CreateService(
            FakeVisitAccessScopeService.Allowing(CreateScope(appointment)),
            store);

        var result = await service.GetForDoctorAsync(appointment.Id, DoctorUserId);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<AccessStateResponse>(result.Response);
        Assert.Equal(appointment.Id, response.AppointmentId);
        Assert.Equal(request.Id, response.AccessRequestId);
        Assert.Equal(nameof(AccessRequestStatus.Approved), response.RequestStatus);
        Assert.Equal(grant.Id, response.GrantId);
        Assert.Equal(nameof(AccessGrantStatus.Active), response.GrantStatus);
        Assert.Equal(grant.ExpiresAtUtc, response.ExpiresAtUtc);
        Assert.Null(response.EndedAtUtc);
        Assert.Equal(5400, response.RemainingSeconds);
    }

    [Fact]
    public async Task GetForPatientAsync_WhenTheGrantIsActive_ReturnsTheSameState()
    {
        var appointment = CreateAppointment();
        var request = CreateApprovedRequest(appointment);
        var grant = AccessGrant.Create(request, appointment, Now.UtcDateTime.AddMinutes(-30));
        var store = new FakeAccessStateStore { Context = new AccessStateContext(request, grant) };
        var scope = FakeVisitAccessScopeService.AllowingPatient(CreateScope(appointment));
        var service = CreateService(scope, store);

        var result = await service.GetForPatientAsync(appointment.Id, PatientId);

        Assert.True(result.IsSuccess);
        Assert.True(scope.WasPatientCalled);
        Assert.Equal(grant.Id, result.Response!.GrantId);
    }

    [Fact]
    public async Task GetForDoctorAsync_WhenTheVisitCompleted_UsesTheGraceEndForTheRemainingSeconds()
    {
        var appointment = CreateAppointment(AppointmentStatus.Completed);
        var request = CreateApprovedRequest(appointment);
        var grant = AccessGrant.Create(request, appointment, Now.UtcDateTime.AddMinutes(-30));
        grant.CompleteVisit(Now.UtcDateTime.AddMinutes(-10));
        var store = new FakeAccessStateStore { Context = new AccessStateContext(request, grant) };
        var service = CreateService(
            FakeVisitAccessScopeService.Allowing(CreateScope(appointment)),
            store);

        var result = await service.GetForDoctorAsync(appointment.Id, DoctorUserId);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<AccessStateResponse>(result.Response);
        Assert.Equal(nameof(AccessGrantStatus.Active), response.GrantStatus);
        Assert.Equal(Now.UtcDateTime.AddMinutes(5), response.EndedAtUtc);
        Assert.Equal(300, response.RemainingSeconds);
    }

    [Fact]
    public async Task GetForDoctorAsync_WhenThePatientEndedTheGrant_ReturnsEndedAndZeroRemaining()
    {
        var appointment = CreateAppointment();
        var request = CreateApprovedRequest(appointment);
        var grant = AccessGrant.Create(request, appointment, Now.UtcDateTime.AddMinutes(-30));
        grant.EndByPatient(Now.UtcDateTime.AddMinutes(-1));
        var store = new FakeAccessStateStore { Context = new AccessStateContext(request, grant) };
        var service = CreateService(
            FakeVisitAccessScopeService.Allowing(CreateScope(appointment)),
            store);

        var result = await service.GetForDoctorAsync(appointment.Id, DoctorUserId);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<AccessStateResponse>(result.Response);
        Assert.Equal(nameof(AccessGrantStatus.Ended), response.GrantStatus);
        Assert.Equal(Now.UtcDateTime.AddMinutes(-1), response.EndedAtUtc);
        Assert.Equal(0, response.RemainingSeconds);
    }

    [Fact]
    public async Task GetForDoctorAsync_WhenTheGrantReachedTheTwoHourLimit_ReturnsExpired()
    {
        var appointment = CreateAppointment();
        var request = CreateApprovedRequest(appointment);
        var grant = AccessGrant.Create(request, appointment, Now.UtcDateTime.AddHours(-3));
        var store = new FakeAccessStateStore { Context = new AccessStateContext(request, grant) };
        var service = CreateService(
            FakeVisitAccessScopeService.Allowing(CreateScope(appointment)),
            store);

        var result = await service.GetForDoctorAsync(appointment.Id, DoctorUserId);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<AccessStateResponse>(result.Response);
        Assert.Equal(nameof(AccessGrantStatus.Expired), response.GrantStatus);
        Assert.Equal(grant.ExpiresAtUtc, response.ExpiresAtUtc);
        Assert.Null(response.EndedAtUtc);
        Assert.Equal(0, response.RemainingSeconds);
    }

    [Fact]
    public async Task GetForDoctorAsync_WhenTheAppointmentHasNoRequestOrGrant_ReturnsNulls()
    {
        var appointment = CreateAppointment();
        var store = new FakeAccessStateStore { Context = new AccessStateContext(null, null) };
        var service = CreateService(
            FakeVisitAccessScopeService.Allowing(CreateScope(appointment)),
            store);

        var result = await service.GetForDoctorAsync(appointment.Id, DoctorUserId);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<AccessStateResponse>(result.Response);
        Assert.Equal(appointment.Id, response.AppointmentId);
        Assert.Null(response.AccessRequestId);
        Assert.Null(response.RequestStatus);
        Assert.Null(response.GrantId);
        Assert.Null(response.GrantStatus);
        Assert.Null(response.ExpiresAtUtc);
        Assert.Null(response.EndedAtUtc);
        Assert.Equal(0, response.RemainingSeconds);
    }

    [Fact]
    public async Task GetForDoctorAsync_WhenTheScopeDoesNotFindTheAppointment_ReturnsAppointmentNotFound()
    {
        var store = new FakeAccessStateStore();
        var service = CreateService(
            new FakeVisitAccessScopeService
            {
                DoctorResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.AppointmentNotFound)
            },
            store);

        var result = await service.GetForDoctorAsync(42, DoctorUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessStateFailure.AppointmentNotFound, result.Failure);
        Assert.False(store.WasLoaded);
    }

    [Fact]
    public async Task GetForDoctorAsync_WhenAnotherDoctorOwnsTheAppointment_ReturnsActorMismatch()
    {
        var service = CreateService(
            new FakeVisitAccessScopeService
            {
                DoctorResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.DoctorMismatch)
            },
            new FakeAccessStateStore());

        var result = await service.GetForDoctorAsync(42, DoctorUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessStateFailure.ActorMismatch, result.Failure);
    }

    [Fact]
    public async Task GetForPatientAsync_WhenAnotherPatientOwnsTheAppointment_ReturnsActorMismatch()
    {
        var service = CreateService(
            new FakeVisitAccessScopeService
            {
                PatientResult = VisitAccessScopeResult.Denied(VisitAccessScopeFailure.PatientMismatch)
            },
            new FakeAccessStateStore());

        var result = await service.GetForPatientAsync(42, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessStateFailure.ActorMismatch, result.Failure);
    }

    [Fact]
    public async Task GetForDoctorAsync_WhenTheStoreFindsNoAppointment_ReturnsAppointmentNotFound()
    {
        var appointment = CreateAppointment();
        var service = CreateService(
            FakeVisitAccessScopeService.Allowing(CreateScope(appointment)),
            new FakeAccessStateStore { Context = null });

        var result = await service.GetForDoctorAsync(appointment.Id, DoctorUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessStateFailure.AppointmentNotFound, result.Failure);
    }

    [Fact]
    public async Task GetForDoctorAsync_RoundsTheRemainingSecondsUp()
    {
        var appointment = CreateAppointment();
        var request = CreateApprovedRequest(appointment);
        var grant = AccessGrant.Create(
            request,
            appointment,
            Now.UtcDateTime.AddMinutes(-30).AddMilliseconds(-500));
        var store = new FakeAccessStateStore { Context = new AccessStateContext(request, grant) };
        var service = CreateService(
            FakeVisitAccessScopeService.Allowing(CreateScope(appointment)),
            store);

        var result = await service.GetForDoctorAsync(appointment.Id, DoctorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(5400, result.Response!.RemainingSeconds);
    }

    private static AccessStateService CreateService(
        FakeVisitAccessScopeService scope,
        FakeAccessStateStore store) =>
        new(scope, store, new FixedTimeProvider(Now));

    private static VisitAccessScope CreateScope(Appointment appointment) =>
        new(appointment.Id, appointment.DoctorId, appointment.PatientId);

    private static AccessRequest CreateApprovedRequest(Appointment appointment)
    {
        var request = AccessRequest.Create(
            appointment,
            CreateDoctor(appointment.DoctorId),
            Now.UtcDateTime.AddMinutes(-40));
        request.Approve();
        return request;
    }

    private static Appointment CreateAppointment(
        AppointmentStatus status = AppointmentStatus.CheckedIn)
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
