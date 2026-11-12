using System.Net;
using System.Reflection;
using System.Security.Claims;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.UnitTests;

public class AccessGrantsControllerTests
{
    private static readonly Guid PatientUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GrantId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTime EndedAtUtc = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);
    private static readonly IPAddress TestIpAddress = IPAddress.Parse("203.0.113.10");

    [Fact]
    public async Task End_WithoutAUserIdentifier_Returns401()
    {
        var service = new FakeAccessGrantEndService();
        var controller = CreateController(service, authenticated: false);

        var result = await controller.End(GrantId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.False(service.WasCalled);
    }

    [Theory]
    [InlineData(AccessGrantEndFailure.GrantNotFound, StatusCodes.Status404NotFound)]
    [InlineData(AccessGrantEndFailure.PatientMismatch, StatusCodes.Status403Forbidden)]
    [InlineData(AccessGrantEndFailure.GrantNotActive, StatusCodes.Status409Conflict)]
    [InlineData(AccessGrantEndFailure.GrantConflict, StatusCodes.Status409Conflict)]
    public async Task End_WhenTheServiceRefuses_ReturnsTheMappedProblemDetails(
        AccessGrantEndFailure failure,
        int expectedStatusCode)
    {
        var service = new FakeAccessGrantEndService
        {
            Result = AccessGrantEndResult.Denied(failure)
        };
        var controller = CreateController(service);

        var result = await controller.End(GrantId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatusCode, problem.Status);
        Assert.NotNull(problem.Type);
        Assert.NotNull(problem.Title);
        Assert.NotNull(problem.Detail);
        Assert.Equal(GrantId, service.LastGrantId);
        Assert.Equal(PatientUserId, service.LastPatientUserId);
    }

    [Fact]
    public async Task End_WhenTheGrantEnded_Returns200WithTheEndedGrantAndNoStoreHeader()
    {
        var response = new AccessGrantEndedResponse(
            GrantId,
            EndedAtUtc,
            nameof(GrantEndReason.PatientEnded));
        var service = new FakeAccessGrantEndService
        {
            Result = AccessGrantEndResult.Ended(response)
        };
        var controller = CreateController(service, remoteIp: TestIpAddress);

        var result = await controller.End(GrantId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(response, ok.Value);
        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"].ToString());
        Assert.Equal("203.0.113.10", service.LastIpAddress);
        Assert.Equal(PatientUserId, service.LastPatientUserId);
    }

    [Fact]
    public async Task End_WithoutARemoteIp_PassesUnknownToTheService()
    {
        var service = new FakeAccessGrantEndService
        {
            Result = AccessGrantEndResult.Ended(new AccessGrantEndedResponse(
                GrantId,
                EndedAtUtc,
                nameof(Domain.VisitAccess.GrantEndReason.PatientEnded)))
        };
        var controller = CreateController(service);

        await controller.End(GrantId, CancellationToken.None);

        Assert.Equal("unknown", service.LastIpAddress);
    }

    [Fact]
    public void Controller_RequiresThePatientRole()
    {
        var authorize = typeof(AccessGrantsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Patient", authorize.Roles);

        var route = typeof(AccessGrantsController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("api/access-grants", route.Template);
    }

    [Fact]
    public void End_DeclaresEveryExpectedResponseCode()
    {
        var codes = typeof(AccessGrantsController)
            .GetMethod(nameof(AccessGrantsController.End))!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToHashSet();

        Assert.Contains(StatusCodes.Status200OK, codes);
        Assert.Contains(StatusCodes.Status401Unauthorized, codes);
        Assert.Contains(StatusCodes.Status403Forbidden, codes);
        Assert.Contains(StatusCodes.Status404NotFound, codes);
        Assert.Contains(StatusCodes.Status409Conflict, codes);
    }

    private static AccessGrantsController CreateController(
        FakeAccessGrantEndService service,
        bool authenticated = true,
        IPAddress? remoteIp = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp;
        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, PatientUserId.ToString())],
                "Test"));
        }

        return new AccessGrantsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class FakeAccessGrantEndService : IAccessGrantEndService
    {
        public AccessGrantEndResult Result { get; set; } =
            AccessGrantEndResult.Denied(AccessGrantEndFailure.GrantNotFound);

        public bool WasCalled { get; private set; }

        public Guid LastGrantId { get; private set; }

        public Guid LastPatientUserId { get; private set; }

        public string? LastIpAddress { get; private set; }

        public Task<AccessGrantEndResult> EndAsync(
            Guid grantId,
            Guid patientUserId,
            string ipAddress,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastGrantId = grantId;
            LastPatientUserId = patientUserId;
            LastIpAddress = ipAddress;

            return Task.FromResult(Result);
        }
    }
}
