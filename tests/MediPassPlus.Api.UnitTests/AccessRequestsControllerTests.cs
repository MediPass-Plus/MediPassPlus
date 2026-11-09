using System.Net;
using System.Reflection;
using System.Security.Claims;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.VisitAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.UnitTests;

public class AccessRequestsControllerTests
{
    private static readonly Guid PatientUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RequestId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime GrantedAtUtc = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiresAtUtc = GrantedAtUtc.AddHours(2);
    private static readonly IPAddress TestIpAddress = IPAddress.Parse("203.0.113.10");

    [Fact]
    public async Task Approve_WithoutAUserIdentifier_Returns401()
    {
        var service = new FakeAccessRequestDecisionService();
        var controller = CreateController(service, authenticated: false);

        var result = await controller.Approve(RequestId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.False(service.ApproveWasCalled);
    }

    [Theory]
    [InlineData(AccessRequestDecisionFailure.PatientMismatch, StatusCodes.Status403Forbidden)]
    [InlineData(AccessRequestDecisionFailure.RequestNotFound, StatusCodes.Status404NotFound)]
    [InlineData(AccessRequestDecisionFailure.AlreadyAnswered, StatusCodes.Status409Conflict)]
    [InlineData(AccessRequestDecisionFailure.RequestConflict, StatusCodes.Status409Conflict)]
    public async Task Approve_WhenTheServiceRefuses_ReturnsTheMappedProblemDetails(
        AccessRequestDecisionFailure failure,
        int expectedStatusCode)
    {
        var service = new FakeAccessRequestDecisionService
        {
            ApproveResult = AccessRequestDecisionResult.Denied(failure)
        };
        var controller = CreateController(service);

        var result = await controller.Approve(RequestId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatusCode, problem.Status);
        Assert.NotNull(problem.Type);
        Assert.NotNull(problem.Title);
        Assert.NotNull(problem.Detail);
        Assert.Equal(RequestId, service.LastRequestId);
        Assert.Equal(PatientUserId, service.LastPatientUserId);
    }

    [Fact]
    public async Task Approve_WhenApproved_Returns200WithTheGrantAndNoStoreHeader()
    {
        var response = new AccessGrantResponse(
            Guid.NewGuid(),
            RequestId,
            42,
            7,
            GrantedAtUtc,
            ExpiresAtUtc);
        var service = new FakeAccessRequestDecisionService
        {
            ApproveResult = AccessRequestDecisionResult.Approved(response)
        };
        var controller = CreateController(service, remoteIp: TestIpAddress);

        var result = await controller.Approve(RequestId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(response, ok.Value);
        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"].ToString());
        Assert.Equal("203.0.113.10", service.LastIpAddress);
        Assert.Equal(PatientUserId, service.LastPatientUserId);
    }

    [Fact]
    public async Task Approve_WithoutARemoteIp_PassesUnknownToTheService()
    {
        var response = new AccessGrantResponse(
            Guid.NewGuid(),
            RequestId,
            42,
            7,
            GrantedAtUtc,
            ExpiresAtUtc);
        var service = new FakeAccessRequestDecisionService
        {
            ApproveResult = AccessRequestDecisionResult.Approved(response)
        };
        var controller = CreateController(service);

        await controller.Approve(RequestId, CancellationToken.None);

        Assert.Equal("unknown", service.LastIpAddress);
    }

    [Fact]
    public async Task Reject_WithoutAUserIdentifier_Returns401()
    {
        var service = new FakeAccessRequestDecisionService();
        var controller = CreateController(service, authenticated: false);

        var result = await controller.Reject(RequestId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.False(service.RejectWasCalled);
    }

    [Fact]
    public async Task Reject_WhenTheServiceRefuses_ReturnsTheMappedProblemDetails()
    {
        var service = new FakeAccessRequestDecisionService
        {
            RejectResult = AccessRequestDecisionResult.Denied(AccessRequestDecisionFailure.PatientMismatch)
        };
        var controller = CreateController(service);

        var result = await controller.Reject(RequestId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal(RequestId, service.LastRequestId);
        Assert.Equal(PatientUserId, service.LastPatientUserId);
    }

    [Fact]
    public async Task Reject_WhenRejected_Returns204WithoutABody()
    {
        var service = new FakeAccessRequestDecisionService
        {
            RejectResult = AccessRequestDecisionResult.Rejected()
        };
        var controller = CreateController(service, remoteIp: TestIpAddress);

        var result = await controller.Reject(RequestId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("203.0.113.10", service.LastIpAddress);
        Assert.Equal(RequestId, service.LastRequestId);
        Assert.Equal(PatientUserId, service.LastPatientUserId);
    }

    [Fact]
    public void Controller_RequiresThePatientRole()
    {
        var authorize = typeof(AccessRequestsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Patient", authorize.Roles);

        var route = typeof(AccessRequestsController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("api/access-requests", route.Template);
    }

    [Fact]
    public void Approve_DeclaresEveryExpectedResponseCode()
    {
        var codes = GetResponseCodes(nameof(AccessRequestsController.Approve));

        Assert.Contains(StatusCodes.Status200OK, codes);
        Assert.Contains(StatusCodes.Status401Unauthorized, codes);
        Assert.Contains(StatusCodes.Status403Forbidden, codes);
        Assert.Contains(StatusCodes.Status404NotFound, codes);
        Assert.Contains(StatusCodes.Status409Conflict, codes);
    }

    [Fact]
    public void Reject_DeclaresEveryExpectedResponseCode()
    {
        var codes = GetResponseCodes(nameof(AccessRequestsController.Reject));

        Assert.Contains(StatusCodes.Status204NoContent, codes);
        Assert.Contains(StatusCodes.Status401Unauthorized, codes);
        Assert.Contains(StatusCodes.Status403Forbidden, codes);
        Assert.Contains(StatusCodes.Status404NotFound, codes);
        Assert.Contains(StatusCodes.Status409Conflict, codes);
    }

    private static HashSet<int> GetResponseCodes(string actionName) =>
        typeof(AccessRequestsController)
            .GetMethod(actionName)!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToHashSet();

    private static AccessRequestsController CreateController(
        FakeAccessRequestDecisionService service,
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

        return new AccessRequestsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class FakeAccessRequestDecisionService : IAccessRequestDecisionService
    {
        public AccessRequestDecisionResult ApproveResult { get; set; } =
            AccessRequestDecisionResult.Denied(AccessRequestDecisionFailure.RequestNotFound);

        public AccessRequestDecisionResult RejectResult { get; set; } =
            AccessRequestDecisionResult.Denied(AccessRequestDecisionFailure.RequestNotFound);

        public bool ApproveWasCalled { get; private set; }

        public bool RejectWasCalled { get; private set; }

        public Guid LastRequestId { get; private set; }

        public Guid LastPatientUserId { get; private set; }

        public string? LastIpAddress { get; private set; }

        public Task<AccessRequestDecisionResult> ApproveAsync(
            Guid requestId,
            Guid patientUserId,
            string ipAddress,
            CancellationToken cancellationToken = default)
        {
            ApproveWasCalled = true;
            LastRequestId = requestId;
            LastPatientUserId = patientUserId;
            LastIpAddress = ipAddress;

            return Task.FromResult(ApproveResult);
        }

        public Task<AccessRequestDecisionResult> RejectAsync(
            Guid requestId,
            Guid patientUserId,
            string ipAddress,
            CancellationToken cancellationToken = default)
        {
            RejectWasCalled = true;
            LastRequestId = requestId;
            LastPatientUserId = patientUserId;
            LastIpAddress = ipAddress;

            return Task.FromResult(RejectResult);
        }
    }
}
