using System.Reflection;
using System.Security.Claims;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.DoctorSchedule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.UnitTests;

public class DoctorScheduleControllerTests
{
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Date = new(2026, 11, 11);

    [Fact]
    public async Task GetSchedule_WithoutAUserIdentifier_Returns401()
    {
        var service = new FakeDoctorScheduleService();
        var controller = CreateController(service, authenticated: false);

        var result = await controller.GetSchedule(Date, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task GetSchedule_WhenTheDoctorExists_Returns200WithTheItems()
    {
        var items = new List<DoctorScheduleItemDto>
        {
            new(10, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30), "First Patient", "Booked")
        };
        var service = new FakeDoctorScheduleService
        {
            Result = DoctorScheduleResult.Success(items)
        };
        var controller = CreateController(service);

        var result = await controller.GetSchedule(Date, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(items, ok.Value);
        Assert.Equal(DoctorUserId, service.LastUserId);
        Assert.Equal(Date, service.LastDate);
    }

    [Fact]
    public async Task GetSchedule_WhenTheUserHasNoDoctorProfile_Returns403()
    {
        var service = new FakeDoctorScheduleService
        {
            Result = DoctorScheduleResult.Denied(DoctorScheduleFailure.DoctorNotFound)
        };
        var controller = CreateController(service);

        var result = await controller.GetSchedule(Date, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    [Fact]
    public void GetSchedule_RequiresTheDoctorRole()
    {
        var method = typeof(DoctorScheduleController).GetMethod(nameof(DoctorScheduleController.GetSchedule))!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Doctor", authorize.Roles);
    }

    private static DoctorScheduleController CreateController(
        FakeDoctorScheduleService service,
        bool authenticated = true)
    {
        var context = new DefaultHttpContext();
        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, DoctorUserId.ToString())],
                "Test"));
        }

        return new DoctorScheduleController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class FakeDoctorScheduleService : IDoctorScheduleService
    {
        public DoctorScheduleResult Result { get; set; } =
            DoctorScheduleResult.Denied(DoctorScheduleFailure.DoctorNotFound);

        public bool WasCalled { get; private set; }

        public Guid? LastUserId { get; private set; }

        public DateOnly? LastDate { get; private set; }

        public Task<DoctorScheduleResult> GetAsync(
            Guid doctorUserId,
            DateOnly date,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastUserId = doctorUserId;
            LastDate = date;
            return Task.FromResult(Result);
        }
    }
}
