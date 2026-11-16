using System.Reflection;
using System.Security.Claims;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.Booking;
using MediPassPlus.Application.Common;
using MediPassPlus.Domain.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.UnitTests;

public class MyAppointmentsControllerTests
{
    private static readonly Guid PatientUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime StartUtc = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Book_WithoutAUserIdentifier_Returns401()
    {
        var service = new FakeBookingService();
        var controller = CreateController(service, authenticated: false);

        var result = await controller.Book(
            new BookAppointmentRequest { DoctorId = 7, SlotId = 1 },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.False(service.BookCalled);
    }

    [Fact]
    public async Task Book_WhenTheSlotIsBooked_Returns201WithTheAppointment()
    {
        var appointment = NewAppointment();
        var service = new FakeBookingService { BookResult = BookingResult.Success(appointment) };
        var controller = CreateController(service);

        var result = await controller.Book(
            new BookAppointmentRequest { DoctorId = 7, SlotId = 3 },
            CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal($"/api/me/appointments/{appointment.Id}", created.Location);
        Assert.Same(appointment, created.Value);
        Assert.Equal(7, service.LastDoctorId);
        Assert.Equal(3, service.LastSlotId);
        Assert.Equal(PatientUserId, service.LastPatientUserId);
    }

    [Fact]
    public async Task Get_WithoutAUserIdentifier_Returns401()
    {
        var service = new FakeBookingService();
        var controller = CreateController(service, authenticated: false);

        var result = await controller.Get(new MyAppointmentsQuery(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Null(service.LastPage);
    }

    [Fact]
    public async Task Get_ReturnsThePagedAppointmentsOfTheAuthenticatedPatient()
    {
        var page = new PagedResult<PatientAppointmentDto>
        {
            Items =
            [
                new(
                    42,
                    7,
                    "Doctor User",
                    1,
                    "Booking Clinic",
                    3,
                    StartUtc,
                    StartUtc.AddMinutes(30),
                    nameof(AppointmentStatus.Booked),
                    StartUtc.AddDays(-1))
            ],
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };
        var service = new FakeBookingService { GetResult = page };
        var controller = CreateController(service);

        var result = await controller.Get(
            new MyAppointmentsQuery { Page = 1, PageSize = 20 },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(page, ok.Value);
        Assert.Equal(PatientUserId, service.LastPatientUserId);
        Assert.NotNull(service.LastPage);
        Assert.Equal(1, service.LastPage!.Page);
        Assert.Equal(20, service.LastPage.PageSize);
    }

    [Fact]
    public async Task Cancel_WhenTheAppointmentIsCancelled_Returns200WithTheAppointment()
    {
        var appointment = NewAppointment() with { Status = "Cancelled" };
        var service = new FakeBookingService { CancelResult = BookingResult.Success(appointment) };
        var controller = CreateController(service);

        var result = await controller.Cancel(42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(appointment, ok.Value);
        Assert.Equal(42, service.LastAppointmentId);
        Assert.Equal(PatientUserId, service.LastPatientUserId);
    }

    [Fact]
    public async Task Reschedule_WhenTheAppointmentMoves_Returns200WithTheAppointment()
    {
        var appointment = NewAppointment() with { DoctorSlotId = 4, StartTimeUtc = StartUtc.AddHours(2) };
        var service = new FakeBookingService { RescheduleResult = BookingResult.Success(appointment) };
        var controller = CreateController(service);

        var result = await controller.Reschedule(
            42,
            new RescheduleAppointmentRequest { NewSlotId = 4 },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(appointment, ok.Value);
        Assert.Equal(42, service.LastAppointmentId);
        Assert.Equal(4, service.LastNewSlotId);
        Assert.Equal(PatientUserId, service.LastPatientUserId);
    }

    [Theory]
    [InlineData(BookingFailure.DoctorNotFound, StatusCodes.Status404NotFound)]
    [InlineData(BookingFailure.PatientNotFound, StatusCodes.Status404NotFound)]
    [InlineData(BookingFailure.SlotNotFound, StatusCodes.Status404NotFound)]
    [InlineData(BookingFailure.AppointmentNotFound, StatusCodes.Status404NotFound)]
    [InlineData(BookingFailure.DoctorInactive, StatusCodes.Status400BadRequest)]
    [InlineData(BookingFailure.PatientInactive, StatusCodes.Status400BadRequest)]
    [InlineData(BookingFailure.SlotInPast, StatusCodes.Status400BadRequest)]
    [InlineData(BookingFailure.SlotDoctorMismatch, StatusCodes.Status400BadRequest)]
    [InlineData(BookingFailure.AppointmentNotCancellable, StatusCodes.Status400BadRequest)]
    [InlineData(BookingFailure.AppointmentNotReschedulable, StatusCodes.Status400BadRequest)]
    [InlineData(BookingFailure.AppointmentAlreadyStarted, StatusCodes.Status400BadRequest)]
    [InlineData(BookingFailure.AppointmentAlreadyOnSlot, StatusCodes.Status400BadRequest)]
    [InlineData(BookingFailure.SlotAlreadyBooked, StatusCodes.Status409Conflict)]
    [InlineData(BookingFailure.AppointmentConflict, StatusCodes.Status409Conflict)]
    public async Task Failures_MapToTheExpectedProblemDetailsStatus(
        BookingFailure failure,
        int expectedStatus)
    {
        var service = new FakeBookingService
        {
            CancelResult = BookingResult.Denied(failure)
        };
        var controller = CreateController(service);

        var result = await controller.Cancel(42, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public void Controller_RequiresThePatientRole()
    {
        var authorize = typeof(MyAppointmentsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Patient", authorize.Roles);
    }

    [Fact]
    public void Controller_UsesThePatientScopedRoute()
    {
        var route = typeof(MyAppointmentsController).GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(route);
        Assert.Equal("api/me/appointments", route.Template);
    }

    [Theory]
    [InlineData(nameof(MyAppointmentsController.Book), StatusCodes.Status201Created, true)]
    [InlineData(nameof(MyAppointmentsController.Get), StatusCodes.Status200OK, false)]
    [InlineData(nameof(MyAppointmentsController.Cancel), StatusCodes.Status200OK, true)]
    [InlineData(nameof(MyAppointmentsController.Reschedule), StatusCodes.Status200OK, true)]
    public void EveryAction_DeclaresTheExpectedResponseCodes(
        string actionName,
        int successCode,
        bool hasScopeFailures)
    {
        var method = typeof(MyAppointmentsController).GetMethod(actionName)!;
        var codes = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToHashSet();

        Assert.Contains(successCode, codes);
        Assert.Contains(StatusCodes.Status400BadRequest, codes);
        Assert.Contains(StatusCodes.Status401Unauthorized, codes);
        Assert.Contains(StatusCodes.Status403Forbidden, codes);

        if (hasScopeFailures)
        {
            Assert.Contains(StatusCodes.Status404NotFound, codes);
            Assert.Contains(StatusCodes.Status409Conflict, codes);
        }
    }

    private static MyAppointmentsController CreateController(
        FakeBookingService service,
        bool authenticated = true)
    {
        var context = new DefaultHttpContext();
        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, PatientUserId.ToString())],
                "Test"));
        }

        return new MyAppointmentsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static AppointmentDto NewAppointment() =>
        new(
            42,
            PatientUserId,
            7,
            3,
            nameof(AppointmentStatus.Booked),
            StartUtc,
            StartUtc.AddMinutes(30),
            StartUtc.AddDays(-1));

    private sealed class FakeBookingService : IBookingService
    {
        public BookingResult BookResult { get; set; } =
            BookingResult.Denied(BookingFailure.SlotNotFound);

        public BookingResult CancelResult { get; set; } =
            BookingResult.Denied(BookingFailure.AppointmentNotFound);

        public BookingResult RescheduleResult { get; set; } =
            BookingResult.Denied(BookingFailure.AppointmentNotFound);

        public PagedResult<PatientAppointmentDto> GetResult { get; set; } = new();

        public bool BookCalled { get; private set; }

        public int? LastDoctorId { get; private set; }

        public int? LastSlotId { get; private set; }

        public int? LastAppointmentId { get; private set; }

        public int? LastNewSlotId { get; private set; }

        public Guid? LastPatientUserId { get; private set; }

        public PageRequest? LastPage { get; private set; }

        public Task<BookingResult> BookAsync(
            int doctorId,
            int slotId,
            Guid patientUserId,
            CancellationToken cancellationToken = default)
        {
            BookCalled = true;
            LastDoctorId = doctorId;
            LastSlotId = slotId;
            LastPatientUserId = patientUserId;

            return Task.FromResult(BookResult);
        }

        public Task<BookingResult> CancelAsync(
            int appointmentId,
            Guid patientUserId,
            CancellationToken cancellationToken = default)
        {
            LastAppointmentId = appointmentId;
            LastPatientUserId = patientUserId;

            return Task.FromResult(CancelResult);
        }

        public Task<BookingResult> RescheduleAsync(
            int appointmentId,
            int newSlotId,
            Guid patientUserId,
            CancellationToken cancellationToken = default)
        {
            LastAppointmentId = appointmentId;
            LastNewSlotId = newSlotId;
            LastPatientUserId = patientUserId;

            return Task.FromResult(RescheduleResult);
        }

        public Task<PagedResult<PatientAppointmentDto>> GetMyAppointmentsAsync(
            Guid patientUserId,
            PageRequest page,
            CancellationToken cancellationToken = default)
        {
            LastPatientUserId = patientUserId;
            LastPage = page;

            return Task.FromResult(GetResult);
        }
    }
}
