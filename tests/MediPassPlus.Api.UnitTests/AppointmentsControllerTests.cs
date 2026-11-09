using System.Reflection;
using System.Security.Claims;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.Booking;
using MediPassPlus.Domain.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.UnitTests;

public class AppointmentsControllerTests
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
        Assert.Equal($"/api/appointments/{appointment.Id}", created.Location);
        Assert.Same(appointment, created.Value);
        Assert.Equal(7, service.LastDoctorId);
        Assert.Equal(3, service.LastSlotId);
        Assert.Equal(PatientUserId, service.LastPatientUserId);
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
        var authorize = typeof(AppointmentsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Patient", authorize.Roles);
    }

    [Theory]
    [InlineData(nameof(AppointmentsController.Book))]
    [InlineData(nameof(AppointmentsController.Cancel))]
    [InlineData(nameof(AppointmentsController.Reschedule))]
    public void EveryAction_DeclaresTheExpectedResponseCodes(string actionName)
    {
        var method = typeof(AppointmentsController).GetMethod(actionName)!;
        var codes = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToHashSet();

        var successCode = actionName == nameof(AppointmentsController.Book)
            ? StatusCodes.Status201Created
            : StatusCodes.Status200OK;

        Assert.Contains(successCode, codes);
        Assert.Contains(StatusCodes.Status400BadRequest, codes);
        Assert.Contains(StatusCodes.Status401Unauthorized, codes);
        Assert.Contains(StatusCodes.Status403Forbidden, codes);
        Assert.Contains(StatusCodes.Status404NotFound, codes);
        Assert.Contains(StatusCodes.Status409Conflict, codes);
    }

    private static AppointmentsController CreateController(
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

        return new AppointmentsController(service)
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

        public bool BookCalled { get; private set; }

        public int? LastDoctorId { get; private set; }

        public int? LastSlotId { get; private set; }

        public int? LastAppointmentId { get; private set; }

        public int? LastNewSlotId { get; private set; }

        public Guid? LastPatientUserId { get; private set; }

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
    }
}
