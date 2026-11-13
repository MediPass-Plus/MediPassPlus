using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.VisitAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    public async Task StartVisit_WhenTheVisitAlreadyHasAGrant_Returns409()
    {
        var service = new FakeStartVisitService
        {
            Result = StartVisitResult.Denied(StartVisitFailure.AccessAlreadyGranted)
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
        var response = new StartVisitResponse(42, AccessRequestId, token, "data:image/png;base64,AAAA", ExpiresAtUtc);
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
        var response = new StartVisitResponse(42, AccessRequestId, "a-test-qr-token", "data:image/png;base64,AAAA", ExpiresAtUtc);
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

    [Fact]
    public async Task GenerateAccessCode_WithoutAUserIdentifier_Returns401()
    {
        var controller = CreateController(new FakeStartVisitService(), authenticated: false);

        var result = await controller.GenerateAccessCode(42, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
    }

    [Fact]
    public async Task GenerateAccessCode_WhenSuccessful_ReturnsTheCodeWithoutCaching()
    {
        var response = new AccessCodeResponse(42, AccessRequestId, "042915", ExpiresAtUtc);
        var service = new FakeAccessCodeGenerationService
        {
            Result = AccessCodeGenerationResult.Success(response)
        };
        var controller = CreateController(new FakeStartVisitService(), generationService: service);

        var result = await controller.GenerateAccessCode(42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"].ToString());
        Assert.True(service.WasCalled);
    }

    [Fact]
    public async Task GenerateAccessCode_WhenThePatientDoesNotOwnTheAppointment_Returns403()
    {
        var service = new FakeAccessCodeGenerationService
        {
            Result = AccessCodeGenerationResult.Denied(AccessCodeGenerationFailure.PatientMismatch)
        };
        var controller = CreateController(new FakeStartVisitService(), generationService: service);

        var result = await controller.GenerateAccessCode(42, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task GenerateAccessCode_WhenTheVisitHasNoPendingRequest_Returns409()
    {
        var service = new FakeAccessCodeGenerationService
        {
            Result = AccessCodeGenerationResult.Denied(AccessCodeGenerationFailure.RequestNotPending)
        };
        var controller = CreateController(new FakeStartVisitService(), generationService: service);

        var result = await controller.GenerateAccessCode(42, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public void GenerateAccessCode_RequiresThePatientRoleAndTheGenerationLimit()
    {
        var method = typeof(VisitAccessController).GetMethod(nameof(VisitAccessController.GenerateAccessCode))!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();
        var rateLimit = method.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Patient", authorize.Roles);
        Assert.NotNull(rateLimit);
        Assert.Equal("CodeGeneration", rateLimit.PolicyName);
    }

    [Fact]
    public void GenerateAccessCode_DeclaresEveryExpectedResponseCode()
    {
        var method = typeof(VisitAccessController).GetMethod(nameof(VisitAccessController.GenerateAccessCode))!;
        var codes = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToHashSet();

        Assert.Contains(StatusCodes.Status200OK, codes);
        Assert.Contains(StatusCodes.Status400BadRequest, codes);
        Assert.Contains(StatusCodes.Status401Unauthorized, codes);
        Assert.Contains(StatusCodes.Status403Forbidden, codes);
        Assert.Contains(StatusCodes.Status404NotFound, codes);
        Assert.Contains(StatusCodes.Status409Conflict, codes);
        Assert.Contains(StatusCodes.Status429TooManyRequests, codes);
    }

    [Fact]
    public async Task VerifyAccessCode_WithoutAUserIdentifier_Returns401()
    {
        var controller = CreateController(new FakeStartVisitService(), authenticated: false);

        var result = await controller.VerifyAccessCode(
            42,
            new VerifyAccessCodeRequest("042915"),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
    }

    [Fact]
    public async Task VerifyAccessCode_WhenSuccessful_ReturnsTheGrantAndPassesTheCodeAndIp()
    {
        var grant = new AccessGrantResponse(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            AccessRequestId,
            42,
            7,
            ExpiresAtUtc.AddMinutes(-120),
            ExpiresAtUtc);
        var service = new FakeAccessCodeVerificationService
        {
            Result = AccessCodeVerificationResult.Granted(grant)
        };
        var controller = CreateController(
            new FakeStartVisitService(),
            remoteIp: TestIpAddress,
            verificationService: service);

        var result = await controller.VerifyAccessCode(
            42,
            new VerifyAccessCodeRequest("042915"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(grant, ok.Value);
        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"].ToString());
        Assert.Equal("042915", service.LastCode);
        Assert.Equal("203.0.113.10", service.LastIpAddress);
    }

    [Fact]
    public async Task VerifyAccessCode_WhenTheCodeIsInvalid_Returns400()
    {
        var service = new FakeAccessCodeVerificationService
        {
            Result = AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.InvalidCode)
        };
        var controller = CreateController(new FakeStartVisitService(), verificationService: service);

        var result = await controller.VerifyAccessCode(
            42,
            new VerifyAccessCodeRequest("999999"),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task VerifyAccessCode_WhenTheCodeIsLocked_Returns409()
    {
        var service = new FakeAccessCodeVerificationService
        {
            Result = AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.CodeLocked)
        };
        var controller = CreateController(new FakeStartVisitService(), verificationService: service);

        var result = await controller.VerifyAccessCode(
            42,
            new VerifyAccessCodeRequest("042915"),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public void VerifyAccessCode_RequiresTheDoctorRoleAndTheVerificationLimit()
    {
        var method = typeof(VisitAccessController).GetMethod(nameof(VisitAccessController.VerifyAccessCode))!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();
        var rateLimit = method.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Doctor", authorize.Roles);
        Assert.NotNull(rateLimit);
        Assert.Equal("CodeVerification", rateLimit.PolicyName);
    }

    [Fact]
    public void VerifyAccessCode_DeclaresEveryExpectedResponseCode()
    {
        var method = typeof(VisitAccessController).GetMethod(nameof(VisitAccessController.VerifyAccessCode))!;
        var codes = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToHashSet();

        Assert.Contains(StatusCodes.Status200OK, codes);
        Assert.Contains(StatusCodes.Status400BadRequest, codes);
        Assert.Contains(StatusCodes.Status401Unauthorized, codes);
        Assert.Contains(StatusCodes.Status403Forbidden, codes);
        Assert.Contains(StatusCodes.Status404NotFound, codes);
        Assert.Contains(StatusCodes.Status409Conflict, codes);
        Assert.Contains(StatusCodes.Status429TooManyRequests, codes);
    }

    [Fact]
    public async Task GetAccessState_WithoutAUserIdentifier_Returns401()
    {
        var controller = CreateController(
            new FakeStartVisitService(),
            authenticated: false,
            role: "Doctor");

        var result = await controller.GetAccessState(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task GetAccessState_WhenTheDoctorReads_Returns200WithTheStateAndNoStoreHeader()
    {
        var response = new AccessStateResponse(
            42,
            AccessRequestId,
            "Approved",
            Guid.NewGuid(),
            "Active",
            ExpiresAtUtc,
            null,
            5400);
        var service = new FakeAccessStateService
        {
            Result = AccessStateResult.Success(response)
        };
        var controller = CreateController(
            new FakeStartVisitService(),
            accessStateService: service,
            role: "Doctor");

        var result = await controller.GetAccessState(42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"].ToString());
        Assert.True(service.DoctorWasCalled);
        Assert.False(service.PatientWasCalled);
    }

    [Fact]
    public async Task GetAccessState_WhenThePatientReads_UsesThePatientScope()
    {
        var response = new AccessStateResponse(42, null, null, null, null, null, null, 0);
        var service = new FakeAccessStateService
        {
            Result = AccessStateResult.Success(response)
        };
        var controller = CreateController(
            new FakeStartVisitService(),
            accessStateService: service,
            role: "Patient");

        var result = await controller.GetAccessState(42, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.True(service.PatientWasCalled);
        Assert.False(service.DoctorWasCalled);
    }

    [Fact]
    public async Task GetAccessState_WhenTheCallerHasNoSupportedRole_Returns403()
    {
        var controller = CreateController(
            new FakeStartVisitService(),
            role: "Staff");

        var result = await controller.GetAccessState(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Theory]
    [InlineData(AccessStateFailure.AppointmentNotFound, StatusCodes.Status404NotFound)]
    [InlineData(AccessStateFailure.ActorMismatch, StatusCodes.Status403Forbidden)]
    public async Task GetAccessState_WhenTheServiceRefuses_ReturnsTheMappedProblemDetails(
        AccessStateFailure failure,
        int expectedStatusCode)
    {
        var service = new FakeAccessStateService
        {
            Result = AccessStateResult.Denied(failure)
        };
        var controller = CreateController(
            new FakeStartVisitService(),
            accessStateService: service,
            role: "Doctor");

        var result = await controller.GetAccessState(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatusCode, problem.Status);
    }

    [Fact]
    public void GetAccessState_RequiresTheDoctorOrPatientRole()
    {
        var method = typeof(VisitAccessController)
            .GetMethod(nameof(VisitAccessController.GetAccessState))!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Doctor,Patient", authorize.Roles);
    }

    [Fact]
    public void GetAccessState_DeclaresEveryExpectedResponseCode()
    {
        var method = typeof(VisitAccessController)
            .GetMethod(nameof(VisitAccessController.GetAccessState))!;
        var codes = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToHashSet();

        Assert.Contains(StatusCodes.Status200OK, codes);
        Assert.Contains(StatusCodes.Status401Unauthorized, codes);
        Assert.Contains(StatusCodes.Status403Forbidden, codes);
        Assert.Contains(StatusCodes.Status404NotFound, codes);
    }

    private static VisitAccessController CreateController(
        FakeStartVisitService service,
        bool authenticated = true,
        IPAddress? remoteIp = null,
        FakeAccessCodeGenerationService? generationService = null,
        FakeAccessCodeVerificationService? verificationService = null,
        FakeAccessStateService? accessStateService = null,
        string? role = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp;
        if (authenticated)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, DoctorUserId.ToString())
            };

            if (role is not null)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        return new VisitAccessController(
            service,
            generationService ?? new FakeAccessCodeGenerationService(),
            verificationService ?? new FakeAccessCodeVerificationService(),
            accessStateService ?? new FakeAccessStateService())
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

    private sealed class FakeAccessCodeGenerationService : IAccessCodeGenerationService
    {
        public AccessCodeGenerationResult Result { get; set; } =
            AccessCodeGenerationResult.Denied(AccessCodeGenerationFailure.AppointmentNotFound);

        public bool WasCalled { get; private set; }

        public Task<AccessCodeGenerationResult> GenerateAsync(
            int appointmentId,
            Guid patientUserId,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeAccessCodeVerificationService : IAccessCodeVerificationService
    {
        public AccessCodeVerificationResult Result { get; set; } =
            AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.AppointmentNotFound);

        public string? LastCode { get; private set; }

        public string? LastIpAddress { get; private set; }

        public Task<AccessCodeVerificationResult> VerifyAsync(
            int appointmentId,
            Guid doctorUserId,
            string code,
            string ipAddress,
            CancellationToken cancellationToken = default)
        {
            LastCode = code;
            LastIpAddress = ipAddress;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeAccessStateService : IAccessStateService
    {
        public AccessStateResult Result { get; set; } =
            AccessStateResult.Denied(AccessStateFailure.AppointmentNotFound);

        public bool DoctorWasCalled { get; private set; }

        public bool PatientWasCalled { get; private set; }

        public Task<AccessStateResult> GetForDoctorAsync(
            int appointmentId,
            Guid doctorUserId,
            CancellationToken cancellationToken = default)
        {
            DoctorWasCalled = true;
            return Task.FromResult(Result);
        }

        public Task<AccessStateResult> GetForPatientAsync(
            int appointmentId,
            Guid patientUserId,
            CancellationToken cancellationToken = default)
        {
            PatientWasCalled = true;
            return Task.FromResult(Result);
        }
    }
}
