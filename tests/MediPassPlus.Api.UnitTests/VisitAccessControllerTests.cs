using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.VisitAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.UnitTests;

public class VisitAccessControllerTests
{
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccessRequestId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime ExpiresAtUtc = new(2026, 11, 8, 9, 2, 0, DateTimeKind.Utc);
    private static readonly IPAddress TestIpAddress = IPAddress.Parse("203.0.113.10");

    [Fact]
    public async Task StartVisit_WithoutAUserIdentifier_Returns401()
    {
        var service = new FakeStartVisitService();
        var controller = CreateController(service, authenticated: false);

        var result = await controller.StartVisit(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task StartVisit_WhenTheDoctorDoesNotOwnTheAppointment_Returns403()
    {
        var service = new FakeStartVisitService
        {
            Result = StartVisitResult.Denied(StartVisitFailure.DoctorMismatch)
        };
        var controller = CreateController(service);

        var result = await controller.StartVisit(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
    }

    [Fact]
    public async Task StartVisit_WhenTheAppointmentIsNotCheckedIn_Returns400()
    {
        var service = new FakeStartVisitService
        {
            Result = StartVisitResult.Denied(StartVisitFailure.AppointmentNotCheckedIn)
        };
        var controller = CreateController(service);

        var result = await controller.StartVisit(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task StartVisit_WhenTheAppointmentIsMissing_Returns404()
    {
        var service = new FakeStartVisitService
        {
            Result = StartVisitResult.Denied(StartVisitFailure.AppointmentNotFound)
        };
        var controller = CreateController(service);

        var result = await controller.StartVisit(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task StartVisit_WhenAConcurrentCallWonTheRequestRace_Returns409()
    {
        var service = new FakeStartVisitService
        {
            Result = StartVisitResult.Denied(StartVisitFailure.RequestConflict)
        };
        var controller = CreateController(service);

        var result = await controller.StartVisit(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task StartVisit_WhenTheVisitStarts_Returns200WithTheTokenButNotItsHash()
    {
        const string token = "a-test-qr-token";
        var response = new StartVisitResponse(42, AccessRequestId, token, ExpiresAtUtc);
        var service = new FakeStartVisitService { Result = StartVisitResult.Success(response) };
        var controller = CreateController(service, remoteIp: TestIpAddress);

        var result = await controller.StartVisit(42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(response, ok.Value);
        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"].ToString());
        Assert.Equal("203.0.113.10", service.LastIpAddress);

        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = QrTokenGenerator.Hash(token);
        Assert.True(json.Contains(token, StringComparison.Ordinal));
        Assert.False(json.Contains(hash, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartVisit_WithoutARemoteIp_PassesUnknownToTheAuditWriter()
    {
        var response = new StartVisitResponse(42, AccessRequestId, "a-test-qr-token", ExpiresAtUtc);
        var service = new FakeStartVisitService { Result = StartVisitResult.Success(response) };
        var controller = CreateController(service);

        await controller.StartVisit(42, CancellationToken.None);

        Assert.Equal("unknown", service.LastIpAddress);
    }

    [Fact]
    public void StartVisit_RequiresTheDoctorRole()
    {
        var method = typeof(VisitAccessController).GetMethod(nameof(VisitAccessController.StartVisit))!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Doctor", authorize.Roles);
    }

    [Fact]
    public void StartVisit_DeclaresEveryExpectedResponseCode()
    {
        var method = typeof(VisitAccessController).GetMethod(nameof(VisitAccessController.StartVisit))!;
        var codes = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToHashSet();

        Assert.Contains(StatusCodes.Status200OK, codes);
        Assert.Contains(StatusCodes.Status400BadRequest, codes);
        Assert.Contains(StatusCodes.Status401Unauthorized, codes);
        Assert.Contains(StatusCodes.Status403Forbidden, codes);
        Assert.Contains(StatusCodes.Status404NotFound, codes);
        Assert.Contains(StatusCodes.Status409Conflict, codes);
    }

    private static VisitAccessController CreateController(
        FakeStartVisitService service,
        bool authenticated = true,
        IPAddress? remoteIp = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp;
        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, DoctorUserId.ToString())],
                "Test"));
        }

        return new VisitAccessController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class FakeStartVisitService : IStartVisitService
    {
        public StartVisitResult Result { get; set; } =
            StartVisitResult.Denied(StartVisitFailure.AppointmentNotFound);

        public bool WasCalled { get; private set; }

        public string? LastIpAddress { get; private set; }

        public Task<StartVisitResult> StartAsync(
            int appointmentId,
            Guid doctorUserId,
            string ipAddress,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastIpAddress = ipAddress;
            return Task.FromResult(Result);
        }
    }
}
