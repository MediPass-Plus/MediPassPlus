using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using MediPassPlus.Api.Authorization;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediPassPlus.Api.UnitTests.Authorization;

public class DiagnosisAccessGrantFilterTests
{
    private static readonly DateTime Start = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherPatientId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid MedicalRecordId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DiagnosisId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherDiagnosisId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private const int AppointmentId = 42;
    private const int DoctorId = 7;
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
    public async Task OnActionExecutionAsync_WhenTheGrantIsActive_ContinuesAndStashesTheGrant()
    {
        var grant = CreateGrant();
        var checker = new FakeAccessGrantChecker { Result = AccessGrantCheckResult.Allowed(grant) };
        var reader = new FakeDiagnosisScopeReader
        {
            Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId)
        };
        var context = CreateContext(DoctorUserId, DiagnosisId);

        var nextCalled = false;
        await CreateFilter(reader, checker).OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        Assert.True(nextCalled);
        Assert.Null(context.Result);
        Assert.Same(grant, context.HttpContext.GetVerifiedAccessGrant());
        Assert.Equal(DiagnosisId, reader.LastDiagnosisId);
        Assert.Equal(AppointmentId, checker.LastAppointmentId);
    }

    [Theory]
    [MemberData(nameof(RefusalReasons))]
    public async Task OnActionExecutionAsync_WhenTheCheckIsRefused_ReturnsA403ProblemDetails(
        AccessGrantCheckFailure failure)
    {
        var checker = new FakeAccessGrantChecker { Result = AccessGrantCheckResult.Denied(failure) };
        var reader = new FakeDiagnosisScopeReader
        {
            Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId)
        };
        var context = CreateContext(DoctorUserId, DiagnosisId);

        var nextCalled = false;
        await CreateFilter(reader, checker).OnActionExecutionAsync(context, () =>
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
        Assert.Null(context.HttpContext.GetVerifiedAccessGrant());
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenTheDiagnosisDoesNotExist_ReturnsTheGeneric403()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var reader = new FakeDiagnosisScopeReader { Scope = null };
        var context = CreateContext(DoctorUserId, DiagnosisId);

        await CreateFilter(reader, checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.False(checker.WasCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenTheGrantBelongsToAnotherPatient_ReturnsTheGeneric403()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant(patientId: OtherPatientId))
        };
        var reader = new FakeDiagnosisScopeReader
        {
            Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId)
        };
        var context = CreateContext(DoctorUserId, DiagnosisId);

        await CreateFilter(reader, checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.Null(context.HttpContext.GetVerifiedAccessGrant());
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenTheGrantBelongsToAnotherAppointment_ReturnsTheGeneric403()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant(appointmentId: AppointmentId + 1))
        };
        var reader = new FakeDiagnosisScopeReader
        {
            Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId)
        };
        var context = CreateContext(DoctorUserId, DiagnosisId);

        await CreateFilter(reader, checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task OnActionExecutionAsync_ReturnsTheSameForbiddenBodyForEveryReason()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Denied(AccessGrantCheckFailure.GrantExpired)
        };
        var reader = new FakeDiagnosisScopeReader
        {
            Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId)
        };
        var context = CreateContext(DoctorUserId, DiagnosisId);

        await CreateFilter(reader, checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // NFR-PRI-01: the response must not reveal whether the diagnosis exists.
        Assert.Equal(
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"You cannot access this record.","status":403,"detail":"An active access grant is required for this appointment.","traceId":"test-trace"}""",
            json);
        Assert.DoesNotContain(DiagnosisId.ToString(), json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithoutAUserIdentifier_Returns401()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var reader = new FakeDiagnosisScopeReader
        {
            Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId)
        };
        var context = CreateContext(userId: null, DiagnosisId);

        await CreateFilter(reader, checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.False(reader.WasCalled);
        Assert.False(checker.WasCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithoutADiagnosisId_FailsClosedWith403()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var reader = new FakeDiagnosisScopeReader
        {
            Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId)
        };
        var context = CreateContext(DoctorUserId, diagnosisId: null);

        await CreateFilter(reader, checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.False(reader.WasCalled);
        Assert.False(checker.WasCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithAnInvalidRouteDiagnosisId_FailsClosedWith403()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var reader = new FakeDiagnosisScopeReader
        {
            Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId)
        };
        var context = CreateContextWithArguments(
            new Dictionary<string, object?>(),
            userId: DoctorUserId,
            routeDiagnosisId: "not-a-guid");

        await CreateFilter(reader, checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.False(reader.WasCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_UsesTheRouteDiagnosisIdAndIgnoresABodyProperty()
    {
        var checker = new FakeAccessGrantChecker
        {
            Result = AccessGrantCheckResult.Allowed(CreateGrant())
        };
        var reader = new FakeDiagnosisScopeReader
        {
            Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId)
        };
        var context = CreateContextWithArguments(
            new Dictionary<string, object?>
            {
                ["diagnosisId"] = DiagnosisId,
                ["request"] = new { diagnosisId = OtherDiagnosisId }
            },
            userId: DoctorUserId,
            routeDiagnosisId: DiagnosisId);

        await CreateFilter(reader, checker).OnActionExecutionAsync(
            context,
            () => Task.FromResult<ActionExecutedContext>(null!));

        Assert.Equal(DiagnosisId, reader.LastDiagnosisId);
    }

    [Fact]
    public void RequireActiveGrantForDiagnosisAttribute_ResolvesTheDiagnosisGrantFilter()
    {
        var attribute = new RequireActiveGrantForDiagnosisAttribute();

        Assert.IsAssignableFrom<TypeFilterAttribute>(attribute);
        Assert.Equal(typeof(DiagnosisAccessGrantFilter), attribute.ImplementationType);
    }

    [Fact]
    public void RequireActiveGrantForDiagnosisAttribute_RunsBeforeTheGlobalValidationFilter()
    {
        var attribute = new RequireActiveGrantForDiagnosisAttribute();

        Assert.True(attribute.Order < 0);
    }

    private static DiagnosisAccessGrantFilter CreateFilter(
        IDiagnosisScopeReader reader,
        IAccessGrantChecker checker) =>
        new(reader, checker, NullLogger<DiagnosisAccessGrantFilter>.Instance);

    private static ActionExecutingContext CreateContext(Guid? userId, Guid? diagnosisId)
    {
        var arguments = new Dictionary<string, object?>();
        if (diagnosisId is not null)
        {
            arguments["diagnosisId"] = diagnosisId.Value;
        }

        return CreateContextWithArguments(
            arguments,
            userId,
            routeDiagnosisId: diagnosisId?.ToString());
    }

    private static ActionExecutingContext CreateContextWithArguments(
        Dictionary<string, object?> arguments,
        Guid? userId = null,
        string? rawUserId = null,
        object? routeDiagnosisId = null)
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
        if (routeDiagnosisId is not null)
        {
            routeData.Values["diagnosisId"] = routeDiagnosisId;
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

    private static AccessGrant CreateGrant(
        int appointmentId = AppointmentId,
        Guid? patientId = null)
    {
        var appointment = new Appointment
        {
            Id = appointmentId,
            DoctorId = DoctorId,
            PatientId = patientId ?? PatientId,
            DoctorSlotId = 1
        };

        var doctor = new Doctor
        {
            Id = DoctorId,
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

        // The filter these tests exercise resolves access from an appointment route, so the record-scoped overload is
        // never used here. It fails loudly instead of quietly allowing a request.
        public Task<AccessGrantCheckResult> RequireActiveGrantForPatientAsync(
            Guid doctorUserId,
            Guid patientUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This harness only covers the appointment-scoped grant check.");
    }

    private sealed class FakeDiagnosisScopeReader : IDiagnosisScopeReader
    {
        public DiagnosisScope? Scope { get; set; }

        public bool WasCalled { get; private set; }

        public Guid LastDiagnosisId { get; private set; }

        public Task<DiagnosisScope?> FindAsync(
            Guid diagnosisId,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastDiagnosisId = diagnosisId;

            return Task.FromResult(Scope);
        }
    }
}
