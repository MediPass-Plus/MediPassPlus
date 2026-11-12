using System.Net;
using System.Reflection;
using System.Security.Claims;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.AppointmentStatusFlow;
using MediPassPlus.Domain.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.UnitTests;

public class AppointmentStatusControllerTests
{
    private static readonly Guid StaffUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime CheckedInAtUtc = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CheckIn_WithoutAUserIdentifier_Returns401()
    {
        var service = new FakeAppointmentStatusService();
        var controller = CreateController(service, authenticated: false);

        var result = await controller.CheckIn(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.False(service.CheckInCalled);
    }

    [Fact]
    public async Task CheckIn_WhenTheAppointmentIsCheckedIn_Returns200WithTheStaffView()
    {
        var appointment = new StaffAppointmentDto(
            42,
            "Patient Integration Test",
            "+20 100 000 0000",
            CheckedInAtUtc,
            CheckedInAtUtc.AddMinutes(30),
            nameof(AppointmentStatus.CheckedIn),
            CheckedInAtUtc);
        var service = new FakeAppointmentStatusService
        {
            CheckInResult = CheckInResult.Success(appointment)
        };
        var controller = CreateController(service);

        var result = await controller.CheckIn(42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(appointment, ok.Value);
        Assert.Equal(42, service.LastCheckInAppointmentId);
        Assert.Equal(StaffUserId, service.LastCheckInUserId);
    }

    [Theory]
    [InlineData(CheckInFailure.AppointmentNotFound, StatusCodes.Status404NotFound)]
    [InlineData(CheckInFailure.PatientNotFound, StatusCodes.Status404NotFound)]
    [InlineData(CheckInFailure.ClinicMismatch, StatusCodes.Status404NotFound)]
    [InlineData(CheckInFailure.StaffNotFound, StatusCodes.Status403Forbidden)]
    [InlineData(CheckInFailure.AppointmentNotToday, StatusCodes.Status400BadRequest)]
    [InlineData(CheckInFailure.AppointmentNotBooked, StatusCodes.Status400BadRequest)]
    [InlineData(CheckInFailure.AppointmentConflict, StatusCodes.Status409Conflict)]
    public async Task CheckIn_MapsEveryFailure(CheckInFailure failure, int expectedStatus)
    {
        var service = new FakeAppointmentStatusService
        {
            CheckInResult = CheckInResult.Denied(failure)
        };
        var controller = CreateController(service);

        var result = await controller.CheckIn(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatus, problem.Status);
    }

    [Fact]
    public async Task Complete_WithoutAUserIdentifier_Returns401()
    {
        var service = new FakeAppointmentStatusService();
        var controller = CreateController(service, authenticated: false);

        var result = await controller.Complete(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.False(service.CompleteCalled);
    }

    [Fact]
    public async Task Complete_WhenTheVisitIsCompleted_Returns200WithTheStatus()
    {
        var appointment = new DoctorAppointmentStatusDto(
            42,
            nameof(AppointmentStatus.Completed),
            CheckedInAtUtc);
        var service = new FakeAppointmentStatusService
        {
            CompleteResult = CompleteResult.Success(appointment)
        };
        var controller = CreateController(service, asDoctor: true);

        var result = await controller.Complete(42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(appointment, ok.Value);
        Assert.Equal(42, service.LastCompleteAppointmentId);
        Assert.Equal(DoctorUserId, service.LastCompleteUserId);
        Assert.Equal("unknown", service.LastCompleteIpAddress);
    }

    [Theory]
    [InlineData(CompleteFailure.AppointmentNotFound, StatusCodes.Status404NotFound)]
    [InlineData(CompleteFailure.DoctorNotFound, StatusCodes.Status403Forbidden)]
    [InlineData(CompleteFailure.DoctorMismatch, StatusCodes.Status403Forbidden)]
    [InlineData(CompleteFailure.AppointmentNotInVisit, StatusCodes.Status400BadRequest)]
    [InlineData(CompleteFailure.AppointmentConflict, StatusCodes.Status409Conflict)]
    public async Task Complete_MapsEveryFailure(CompleteFailure failure, int expectedStatus)
    {
        var service = new FakeAppointmentStatusService
        {
            CompleteResult = CompleteResult.Denied(failure)
        };
        var controller = CreateController(service, asDoctor: true);

        var result = await controller.Complete(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    [Fact]
    public void CheckIn_RequiresTheStaffRole()
    {
        var method = typeof(AppointmentStatusController).GetMethod(nameof(AppointmentStatusController.CheckIn))!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Staff", authorize.Roles);
    }

    [Fact]
    public void Complete_RequiresTheDoctorRole()
    {
        var method = typeof(AppointmentStatusController).GetMethod(nameof(AppointmentStatusController.Complete))!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Doctor", authorize.Roles);
    }

    private static AppointmentStatusController CreateController(
        FakeAppointmentStatusService service,
        bool authenticated = true,
        bool asDoctor = false)
    {
        var context = new DefaultHttpContext();
        if (authenticated)
        {
            var userId = asDoctor ? DoctorUserId : StaffUserId;
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                "Test"));
        }

        return new AppointmentStatusController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class FakeAppointmentStatusService : IAppointmentStatusService
    {
        public CheckInResult CheckInResult { get; set; } =
            CheckInResult.Denied(CheckInFailure.AppointmentNotFound);

        public CompleteResult CompleteResult { get; set; } =
            CompleteResult.Denied(CompleteFailure.AppointmentNotFound);

        public bool CheckInCalled { get; private set; }

        public bool CompleteCalled { get; private set; }

        public int? LastCheckInAppointmentId { get; private set; }

        public Guid? LastCheckInUserId { get; private set; }

        public int? LastCompleteAppointmentId { get; private set; }

        public Guid? LastCompleteUserId { get; private set; }

        public string? LastCompleteIpAddress { get; private set; }

        public Task<CheckInResult> CheckInAsync(
            int appointmentId,
            Guid staffUserId,
            CancellationToken cancellationToken = default)
        {
            CheckInCalled = true;
            LastCheckInAppointmentId = appointmentId;
            LastCheckInUserId = staffUserId;
            return Task.FromResult(CheckInResult);
        }

        public Task<CompleteResult> CompleteAsync(
            int appointmentId,
            Guid doctorUserId,
            string ipAddress,
            CancellationToken cancellationToken = default)
        {
            CompleteCalled = true;
            LastCompleteAppointmentId = appointmentId;
            LastCompleteUserId = doctorUserId;
            LastCompleteIpAddress = ipAddress;
            return Task.FromResult(CompleteResult);
        }
    }
}
