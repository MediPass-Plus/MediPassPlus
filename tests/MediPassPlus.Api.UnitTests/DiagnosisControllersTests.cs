using System.Net;
using System.Reflection;
using System.Security.Claims;
using MediPassPlus.Api.Authorization;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.Records;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.UnitTests;

/// <summary>
/// The HTTP contract of the doctor write endpoints: the 201 response, the failure mapping, the no-store header and
/// the role plus grant attributes that protect every medical route (FR-REC-03, FR-REC-04, NFR-PRI-01).
/// </summary>
public class DiagnosisControllersTests
{
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DiagnosisId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const int AppointmentId = 42;
    private const string IpAddress = "203.0.113.10";

    [Fact]
    public async Task CreateDiagnosis_WithoutAUserIdentifier_Returns401()
    {
        var service = new FakeDiagnosisWriteService();
        var controller = CreateDiagnosesController(service, authenticated: false);

        var result = await controller.Create(
            AppointmentId,
            new CreateDiagnosisRequest { Description = "Acute bronchitis" },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.False(service.DiagnosisCalled);
    }

    [Fact]
    public async Task CreateDiagnosis_WhenTheServiceSucceeds_Returns201WithTheDiagnosis()
    {
        var diagnosis = new DiagnosisResponse(Guid.NewGuid(), AppointmentId, DateTime.UtcNow);
        var service = new FakeDiagnosisWriteService
        {
            DiagnosisResult = DiagnosisWriteResult.Success(diagnosis)
        };
        var controller = CreateDiagnosesController(service);
        var request = new CreateDiagnosisRequest { Description = "Acute bronchitis" };

        var result = await controller.Create(AppointmentId, request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        Assert.Same(diagnosis, objectResult.Value);
        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"].ToString());
        Assert.Equal(AppointmentId, service.LastAppointmentId);
        Assert.Equal(DoctorUserId, service.LastDoctorUserId);
        Assert.Same(request, service.LastDiagnosisRequest);
        Assert.Equal(IpAddress, service.LastIpAddress);
    }

    [Theory]
    [InlineData(DiagnosisWriteFailure.GrantNotActive, StatusCodes.Status403Forbidden)]
    [InlineData(DiagnosisWriteFailure.AppointmentNotFound, StatusCodes.Status403Forbidden)]
    [InlineData(DiagnosisWriteFailure.ScopeMismatch, StatusCodes.Status403Forbidden)]
    [InlineData(DiagnosisWriteFailure.AppointmentNotInVisit, StatusCodes.Status400BadRequest)]
    [InlineData(DiagnosisWriteFailure.MedicalRecordNotFound, StatusCodes.Status409Conflict)]
    public async Task CreateDiagnosis_MapsEveryFailure(
        DiagnosisWriteFailure failure,
        int expectedStatus)
    {
        var service = new FakeDiagnosisWriteService
        {
            DiagnosisResult = DiagnosisWriteResult.Denied(failure)
        };
        var controller = CreateDiagnosesController(service);

        var result = await controller.Create(
            AppointmentId,
            new CreateDiagnosisRequest { Description = "Acute bronchitis" },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatus, problem.Status);
    }

    [Fact]
    public async Task CreatePrescription_WhenTheServiceSucceeds_Returns201WithThePrescription()
    {
        var prescription = new PrescriptionResponse(Guid.NewGuid(), DiagnosisId);
        var service = new FakeDiagnosisWriteService
        {
            PrescriptionResult = PrescriptionWriteResult.Success(prescription)
        };
        var controller = CreatePrescriptionsController(service);
        var request = new CreatePrescriptionRequest
        {
            MedicineName = "Amoxicillin",
            Dosage = "500 mg twice a day"
        };

        var result = await controller.Create(DiagnosisId, request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        Assert.Same(prescription, objectResult.Value);
        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"].ToString());
        Assert.Equal(DiagnosisId, service.LastDiagnosisId);
        Assert.Equal(DoctorUserId, service.LastDoctorUserId);
        Assert.Same(request, service.LastPrescriptionRequest);
        Assert.Equal(IpAddress, service.LastIpAddress);
    }

    [Theory]
    [InlineData(DiagnosisWriteFailure.GrantNotActive, StatusCodes.Status403Forbidden)]
    [InlineData(DiagnosisWriteFailure.AppointmentNotFound, StatusCodes.Status403Forbidden)]
    [InlineData(DiagnosisWriteFailure.ScopeMismatch, StatusCodes.Status403Forbidden)]
    [InlineData(DiagnosisWriteFailure.AppointmentNotInVisit, StatusCodes.Status400BadRequest)]
    [InlineData(DiagnosisWriteFailure.MedicalRecordNotFound, StatusCodes.Status409Conflict)]
    public async Task CreatePrescription_MapsEveryFailure(
        DiagnosisWriteFailure failure,
        int expectedStatus)
    {
        var service = new FakeDiagnosisWriteService
        {
            PrescriptionResult = PrescriptionWriteResult.Denied(failure)
        };
        var controller = CreatePrescriptionsController(service);

        var result = await controller.Create(
            DiagnosisId,
            new CreatePrescriptionRequest
            {
                MedicineName = "Amoxicillin",
                Dosage = "500 mg twice a day"
            },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatus, problem.Status);
    }

    [Fact]
    public void CreateDiagnosis_RequiresTheDoctorRoleAndTheActiveGrantFilter()
    {
        var method = typeof(DiagnosesController).GetMethod(nameof(DiagnosesController.Create))!;
        var authorize = typeof(DiagnosesController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Doctor", authorize.Roles);
        Assert.NotNull(method.GetCustomAttribute<RequireActiveGrantAttribute>());
    }

    [Fact]
    public void CreatePrescription_RequiresTheDoctorRoleAndTheDiagnosisGrantFilter()
    {
        var method = typeof(PrescriptionsController).GetMethod(nameof(PrescriptionsController.Create))!;
        var authorize = typeof(PrescriptionsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Doctor", authorize.Roles);
        Assert.NotNull(method.GetCustomAttribute<RequireActiveGrantForDiagnosisAttribute>());
    }

    private static DiagnosesController CreateDiagnosesController(
        FakeDiagnosisWriteService service,
        bool authenticated = true)
    {
        var context = CreateHttpContext(authenticated);

        return new DiagnosesController(
            service,
            new FakeCurrentUser(authenticated ? DoctorUserId : null))
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static PrescriptionsController CreatePrescriptionsController(
        FakeDiagnosisWriteService service,
        bool authenticated = true)
    {
        var context = CreateHttpContext(authenticated);

        return new PrescriptionsController(
            service,
            new FakeCurrentUser(authenticated ? DoctorUserId : null))
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static DefaultHttpContext CreateHttpContext(bool authenticated)
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "test-trace"
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse(IpAddress);

        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, DoctorUserId.ToString())],
                "Test"));
        }

        return context;
    }

    private sealed class FakeDiagnosisWriteService : IDiagnosisWriteService
    {
        public DiagnosisWriteResult DiagnosisResult { get; set; } =
            DiagnosisWriteResult.Denied(DiagnosisWriteFailure.AppointmentNotFound);

        public PrescriptionWriteResult PrescriptionResult { get; set; } =
            PrescriptionWriteResult.Denied(DiagnosisWriteFailure.AppointmentNotFound);

        public bool DiagnosisCalled { get; private set; }

        public int? LastAppointmentId { get; private set; }

        public Guid? LastDiagnosisId { get; private set; }

        public Guid? LastDoctorUserId { get; private set; }

        public string? LastIpAddress { get; private set; }

        public CreateDiagnosisRequest? LastDiagnosisRequest { get; private set; }

        public CreatePrescriptionRequest? LastPrescriptionRequest { get; private set; }

        public Task<DiagnosisWriteResult> CreateDiagnosisAsync(
            int appointmentId,
            Guid doctorUserId,
            CreateDiagnosisRequest request,
            string ipAddress,
            CancellationToken cancellationToken = default)
        {
            DiagnosisCalled = true;
            LastAppointmentId = appointmentId;
            LastDoctorUserId = doctorUserId;
            LastDiagnosisRequest = request;
            LastIpAddress = ipAddress;

            return Task.FromResult(DiagnosisResult);
        }

        public Task<PrescriptionWriteResult> CreatePrescriptionAsync(
            Guid diagnosisId,
            Guid doctorUserId,
            CreatePrescriptionRequest request,
            string ipAddress,
            CancellationToken cancellationToken = default)
        {
            LastDiagnosisId = diagnosisId;
            LastDoctorUserId = doctorUserId;
            LastPrescriptionRequest = request;
            LastIpAddress = ipAddress;

            return Task.FromResult(PrescriptionResult);
        }
    }
}
