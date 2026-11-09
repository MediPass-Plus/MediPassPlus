using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using MediPassPlus.Api.Authorization;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediPassPlus.Api.UnitTests.Authorization;

public class AccessGrantFilterTests
{
    private static readonly DateTime Start = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const int AppointmentId = 42;
    private const string TraceIdentifier = "test-trace";

    public static TheoryData<AccessGrantCheckFailure> RefusalReasons =>
        new()
        {
            AccessGrantCheckFailure.DoctorNotFound,
            AccessGrantCheckFailure.AppointmentNotFound,
            AccessGrantCheckFailure.DoctorMismatch,
            AccessGrantCheckFailure.GrantNotFound,
            AccessGrantCheckFailure.GrantEnded,
            AccessGrantCheckFailure.GrantExpired,
            AccessGrantCheckFailure.ScopeMismatch
        };

    [Fact]
    public async Task OnActionExecutionAsync_WhenTheGrantIsActive_ContinuesThePipeline()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var context = CreateContext(DoctorUserId, AppointmentId);

        var nextCalled = false;
        await CreateFilter(checker).OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Theory]
    [MemberData(nameof(RefusalReasons))]
    public async Task OnActionExecutionAsync_WhenTheCheckIsRefused_ReturnsA403ProblemDetails(
        AccessGrantCheckFailure failure)
    {
        var checker = new FakeAccessGrantChecker { Result = AccessGrantCheckResult.Denied(failure) };
        var context = CreateContext(DoctorUserId, AppointmentId);

        var nextCalled = false;
        await CreateFilter(checker).OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.4", problem.Type);
        Assert.Equal("You cannot access this record.", problem.Title);
        Assert.Equal("An active access grant is required for this appointment.", problem.Detail);
        Assert.Equal(TraceIdentifier, problem.Extensions["traceId"]);
        Assert.False(nextCalled);
    }

    [Theory]
    [MemberData(nameof(RefusalReasons))]
    public async Task OnActionExecutionAsync_ReturnsTheSameForbiddenBodyForEveryReason(
        AccessGrantCheckFailure failure)
    {
        var checker = new FakeAccessGrantChecker { Result = AccessGrantCheckResult.Denied(failure) };
        var context = CreateContext(DoctorUserId, AppointmentId);

        await CreateFilter(checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // NFR-PRI-01: the response must not reveal whether the appointment exists.
        Assert.Equal(
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"You cannot access this record.","status":403,"detail":"An active access grant is required for this appointment.","traceId":"test-trace"}""",
            json);
        Assert.DoesNotContain(AppointmentId.ToString(), json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithoutAUserIdentifier_Returns401()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var context = CreateContext(userId: null, appointmentId: AppointmentId);

        await CreateFilter(checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.Status);
        Assert.False(checker.WasCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithAnInvalidUserIdentifier_Returns401()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var context = CreateContextWithRawUserId("not-a-guid", AppointmentId);

        await CreateFilter(checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.False(checker.WasCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithoutAnAppointmentId_FailsClosedWith403()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var context = CreateContext(DoctorUserId, appointmentId: null);

        await CreateFilter(checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.False(checker.WasCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithANonIntegerRouteAppointmentId_FailsClosedWith403()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var context = CreateContextWithArguments(
            new Dictionary<string, object?>(),
            userId: DoctorUserId,
            routeAppointmentId: "not-an-integer");

        await CreateFilter(checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.False(checker.WasCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithAnAppointmentIdOnlyInTheActionArguments_FailsClosedWith403()
    {
        // A query-string or request-body-bound 'appointmentId' must never choose the appointment scope.
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var context = CreateContextWithArguments(
            new Dictionary<string, object?>
            {
                ["appointmentId"] = AppointmentId
            },
            userId: DoctorUserId);

        await CreateFilter(checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.False(checker.WasCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_UsesTheRouteAppointmentIdAndIgnoresABodyProperty()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var context = CreateContextWithArguments(
            new Dictionary<string, object?>
            {
                ["appointmentId"] = AppointmentId,
                ["request"] = new { appointmentId = 999 }
            },
            userId: DoctorUserId,
            routeAppointmentId: AppointmentId);

        await CreateFilter(checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        Assert.True(checker.WasCalled);
        Assert.Equal(AppointmentId, checker.LastAppointmentId);
    }

    [Fact]
    public async Task OnActionExecutionAsync_PassesTheAuthenticatedDoctorToTheChecker()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var context = CreateContext(DoctorUserId, AppointmentId);

        await CreateFilter(checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        Assert.Equal(DoctorUserId, checker.LastDoctorUserId);
    }

    [Fact]
    public void RequireActiveGrantAttribute_ResolvesTheGrantFilter()
    {
        var attribute = new RequireActiveGrantAttribute();

        Assert.IsAssignableFrom<TypeFilterAttribute>(attribute);
        Assert.Equal(typeof(AccessGrantFilter), attribute.ImplementationType);
    }

    [Fact]
    public void RequireActiveGrantAttribute_RunsBeforeTheGlobalValidationFilter()
    {
        var attribute = new RequireActiveGrantAttribute();

        Assert.True(attribute.Order < 0);
    }

    private static AccessGrantFilter CreateFilter(IAccessGrantChecker checker) =>
        new(checker, NullLogger<AccessGrantFilter>.Instance);

    private static ActionExecutingContext CreateContext(Guid? userId, int? appointmentId)
    {
        var arguments = new Dictionary<string, object?>();
        if (appointmentId is not null)
        {
            arguments["appointmentId"] = appointmentId.Value;
        }

        return CreateContextWithArguments(
            arguments,
            userId,
            routeAppointmentId: appointmentId?.ToString(CultureInfo.InvariantCulture));
    }

    private static ActionExecutingContext CreateContextWithRawUserId(string? userId, int appointmentId) =>
        CreateContextWithArguments(
            new Dictionary<string, object?> { ["appointmentId"] = appointmentId },
            rawUserId: userId,
            routeAppointmentId: appointmentId.ToString(CultureInfo.InvariantCulture));

    private static ActionExecutingContext CreateContextWithArguments(
        Dictionary<string, object?> arguments,
        Guid? userId = null,
        string? rawUserId = null,
        object? routeAppointmentId = null)
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = TraceIdentifier };

        var identifier = rawUserId ?? userId?.ToString();
        if (identifier is not null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, identifier)],
                "Test"));
        }

        var routeData = new RouteData();
        if (routeAppointmentId is not null)
        {
            routeData.Values["appointmentId"] = routeAppointmentId;
        }

        var actionContext = new ActionContext(
            httpContext,
            routeData,
            new ActionDescriptor { DisplayName = "TestAction" });

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            arguments,
            controller: new object());
    }

    private static AccessGrant CreateGrant()
    {
        var appointment = new Appointment
        {
            Id = AppointmentId,
            DoctorId = 7,
            PatientId = PatientId,
            DoctorSlotId = 1
        };

        var doctor = new Doctor
        {
            Id = 7,
            UserId = DoctorUserId,
            Specialty = "General",
            ClinicId = 1
        };

        var request = AccessRequest.Create(appointment, doctor, Start);
        request.Approve();

        return AccessGrant.Create(request, appointment, Start);
    }

    private sealed class FakeAccessGrantChecker : IAccessGrantChecker
    {
        public AccessGrantCheckResult Result { get; set; } =
            AccessGrantCheckResult.Denied(AccessGrantCheckFailure.GrantNotFound);

        public bool WasCalled { get; private set; }

        public Guid LastDoctorUserId { get; private set; }

        public int LastAppointmentId { get; private set; }

        public Task<AccessGrantCheckResult> RequireActiveGrantAsync(
            Guid doctorUserId,
            int appointmentId,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastDoctorUserId = doctorUserId;
            LastAppointmentId = appointmentId;

            return Task.FromResult(Result);
        }
    }
}
