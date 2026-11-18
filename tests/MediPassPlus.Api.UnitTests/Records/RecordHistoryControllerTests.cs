using System.Security.Claims;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediPassPlus.Api.UnitTests.Records;

/// <summary>
/// The controller contract of both history endpoints: a missing user identifier is <c>401</c>, a doctor request
/// without a grant the filter verified fails closed with <c>403</c>, a refused scope and a missing record map to
/// <c>403</c> and <c>409</c>, and a successful page is marked non-cacheable (FR-PRV-01, NFR-PRI-01, ADR-0005).
/// </summary>
public class RecordHistoryControllerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MedicalRecordId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const int AppointmentId = 42;
    private const int DoctorId = 7;

    [Fact]
    public async Task MyHistory_WithoutAUserIdentifier_Returns401()
    {
        var service = new FakeRecordHistoryService();
        var controller = CreateMyHistoryController(service, authenticated: false);

        var result = await controller.Get(new RecordHistoryQuery(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task MyHistory_WithAUserIdentifier_Returns200AndMarksTheResponseNonCacheable()
    {
        var service = new FakeRecordHistoryService { Result = RecordHistoryResult.Success(NewHistory()) };
        var controller = CreateMyHistoryController(service);

        var result = await controller.Get(new RecordHistoryQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(service.Result.History, ok.Value);
        Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal(UserId, service.LastPatientUserId);
    }

    [Fact]
    public async Task MyHistory_WhenTheRecordIsMissing_Returns409()
    {
        var service = new FakeRecordHistoryService
        {
            Result = RecordHistoryResult.Denied(RecordHistoryFailure.RecordNotFound)
        };
        var controller = CreateMyHistoryController(service);

        var result = await controller.Get(new RecordHistoryQuery(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
    }

    [Fact]
    public async Task AppointmentHistory_WhenNoVerifiedGrantWasStored_Returns403AndDoesNotCallTheService()
    {
        var service = new FakeRecordHistoryService();
        var controller = CreateAppointmentHistoryController(service, grant: null);

        var result = await controller.Get(AppointmentId, new RecordHistoryQuery(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task AppointmentHistory_WithAVerifiedGrant_Returns200AndPassesTheGrantScope()
    {
        var grant = CreateGrant();
        var service = new FakeRecordHistoryService { Result = RecordHistoryResult.Success(NewHistory()) };
        var controller = CreateAppointmentHistoryController(service, grant);

        var result = await controller.Get(AppointmentId, new RecordHistoryQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(grant, service.LastGrant);
        Assert.Equal(AppointmentId, service.LastAppointmentId);
        Assert.Equal(UserId, service.LastDoctorUserId);
        Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task AppointmentHistory_WhenTheServiceRefusesTheScope_Returns403()
    {
        var service = new FakeRecordHistoryService
        {
            Result = RecordHistoryResult.Denied(RecordHistoryFailure.ScopeMismatch)
        };
        var controller = CreateAppointmentHistoryController(service, CreateGrant());

        var result = await controller.Get(AppointmentId, new RecordHistoryQuery(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task AppointmentHistory_WhenTheRecordIsMissing_Returns409()
    {
        var service = new FakeRecordHistoryService
        {
            Result = RecordHistoryResult.Denied(RecordHistoryFailure.RecordNotFound)
        };
        var controller = CreateAppointmentHistoryController(service, CreateGrant());

        var result = await controller.Get(AppointmentId, new RecordHistoryQuery(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
    }

    private static RecordHistoryResponse NewHistory() =>
        new()
        {
            MedicalRecordId = MedicalRecordId,
            RecordCreatedAtUtc = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc)
        };

    private static AccessGrant CreateGrant()
    {
        var appointment = new Appointment
        {
            Id = AppointmentId,
            DoctorId = DoctorId,
            PatientId = PatientId,
            DoctorSlotId = 1
        };

        var doctor = new Doctor
        {
            Id = DoctorId,
            UserId = UserId,
            Specialty = "General",
            ClinicId = 1
        };

        var request = AccessRequest.Create(
            appointment,
            doctor,
            new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc));
        request.Approve();

        return AccessGrant.Create(
            request,
            appointment,
            new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc));
    }

    private static MyHistoryController CreateMyHistoryController(
        FakeRecordHistoryService service,
        bool authenticated = true) =>
        new(service, new FakeCurrentUser(authenticated ? UserId : null))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(authenticated)
            }
        };

    private static AppointmentHistoryController CreateAppointmentHistoryController(
        FakeRecordHistoryService service,
        AccessGrant? grant)
    {
        var context = CreateHttpContext(authenticated: true);

        if (grant is not null)
        {
            context.SetVerifiedAccessGrant(grant);
        }

        return new AppointmentHistoryController(
            service,
            NullLogger<AppointmentHistoryController>.Instance,
            new FakeCurrentUser(UserId))
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static DefaultHttpContext CreateHttpContext(bool authenticated)
    {
        var context = new DefaultHttpContext();

        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())],
                "Test"));
        }

        return context;
    }

    private sealed class FakeRecordHistoryService : IRecordHistoryService
    {
        public RecordHistoryResult Result { get; set; } =
            RecordHistoryResult.Denied(RecordHistoryFailure.RecordNotFound);

        public bool WasCalled { get; private set; }

        public Guid LastPatientUserId { get; private set; }

        public Guid LastDoctorUserId { get; private set; }

        public int LastAppointmentId { get; private set; }

        public AccessGrant? LastGrant { get; private set; }

        public Task<RecordHistoryResult> GetForDoctorAsync(
            int appointmentId,
            AccessGrant verifiedGrant,
            Guid doctorUserId,
            RecordHistoryQuery query,
            string ipAddress,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastAppointmentId = appointmentId;
            LastGrant = verifiedGrant;
            LastDoctorUserId = doctorUserId;

            return Task.FromResult(Result);
        }

        public Task<RecordHistoryResult> GetForPatientAsync(
            Guid patientUserId,
            RecordHistoryQuery query,
            string ipAddress,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastPatientUserId = patientUserId;

            return Task.FromResult(Result);
        }
    }
}
