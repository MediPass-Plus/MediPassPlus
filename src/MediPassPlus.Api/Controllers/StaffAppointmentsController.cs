using System.Security.Claims;
using MediPassPlus.Application.AppointmentStatusFlow;
using MediPassPlus.Application.Booking;
using MediPassPlus.Application.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>Clinic staff books and checks in appointments of their own clinic's doctors (MP-40).</summary>
[ApiController]
[Route("api/staff/appointments")]
[Authorize(Roles = "Staff")]
public class StaffAppointmentsController : ControllerBase
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private readonly IStaffBookingService _booking;
    private readonly IAppointmentStatusService _status;

    public StaffAppointmentsController(IStaffBookingService booking, IAppointmentStatusService status)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(status);
        _booking = booking;
        _status = status;
    }

    /// <summary>Books a free future slot of a doctor of the staff member's clinic for a patient.</summary>
    /// <response code="201">The appointment was created.</response>
    /// <response code="400">The slot is in the past, an account is inactive, or the body is invalid.</response>
    /// <response code="401">The access token does not identify a user.</response>
    /// <response code="403">The caller has no clinic staff profile.</response>
    /// <response code="404">The doctor or slot is not in the staff member's clinic, or the patient does not exist.</response>
    /// <response code="409">The slot is already booked.</response>
    [HttpPost]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Book(StaffBookAppointmentDto request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var staffUserId))
        {
            return Unauthorized401();
        }

        var result = await _booking.BookAsync(staffUserId, request, cancellationToken);

        if (result.IsSuccess)
        {
            return Created($"/api/appointments/{result.Appointment!.Id}", result.Appointment);
        }

        return result.Failure switch
        {
            StaffBookingFailure.StaffProfileNotFound => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                type: ForbiddenType,
                title: "No clinic staff profile.",
                detail: "This account is not linked to a clinic."),
            StaffBookingFailure.DoctorNotFound or StaffBookingFailure.PatientNotFound => NotFoundProblem(),
            StaffBookingFailure.Booking => MapBookingFailure(result.BookingFailure),
            _ => throw new InvalidOperationException($"Unexpected failure: {result.Failure}.")
        };
    }

    /// <summary>Marks a booked appointment of today in the staff member's own clinic as arrived.</summary>
    /// <param name="appointmentId">The appointment to check in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The appointment was checked in.</response>
    /// <response code="400">The appointment is not today or is not booked.</response>
    /// <response code="401">The access token does not identify a user.</response>
    /// <response code="403">The caller has no clinic staff profile.</response>
    /// <response code="404">The appointment does not exist in the staff member's clinic.</response>
    /// <response code="409">A concurrent request changed the status first.</response>
    [HttpPost("{appointmentId:int}/check-in")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(StaffAppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CheckIn(int appointmentId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var staffUserId))
        {
            return Unauthorized401();
        }

        var result = await _status.CheckInAsync(appointmentId, staffUserId, cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Appointment);
        }

        return result.Failure switch
        {
            CheckInFailure.AppointmentNotFound or
            CheckInFailure.PatientNotFound or
            CheckInFailure.ClinicMismatch => NotFoundProblem(),
            CheckInFailure.StaffNotFound => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                type: ForbiddenType,
                title: "No clinic staff profile.",
                detail: "This account is not linked to a clinic."),
            CheckInFailure.AppointmentNotToday => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                type: BadRequestType,
                title: "The appointment cannot be checked in yet.",
                detail: "Only an appointment scheduled for today can be checked in."),
            CheckInFailure.AppointmentNotBooked => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                type: BadRequestType,
                title: "The appointment cannot be checked in.",
                detail: "Only a booked appointment can be checked in."),
            CheckInFailure.AppointmentConflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: ConflictType,
                title: "The appointment conflicted with another request.",
                detail: "Another request changed the appointment status first. Refresh and retry."),
            _ => throw new InvalidOperationException($"Unexpected check-in failure: {result.Failure}.")
        };
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId != Guid.Empty;

    private IActionResult Unauthorized401() =>
        Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            type: UnauthorizedType,
            title: "Authentication required.",
            detail: "The access token does not identify a user.");

    private IActionResult NotFoundProblem() =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            type: NotFoundType,
            title: "Not found.",
            detail: "No such appointment, doctor, slot or patient in your clinic.");

    private IActionResult MapBookingFailure(BookingFailure failure) =>
        failure switch
        {
            // A slot that belongs to another doctor is hidden as not found, like any out-of-scope resource.
            BookingFailure.DoctorNotFound or
            BookingFailure.PatientNotFound or
            BookingFailure.SlotNotFound or
            BookingFailure.SlotDoctorMismatch => NotFoundProblem(),
            BookingFailure.SlotAlreadyBooked or BookingFailure.AppointmentConflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: ConflictType,
                title: "The slot is already booked.",
                detail: "This slot already has an active appointment."),
            _ => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                type: BadRequestType,
                title: "The appointment cannot be booked.",
                detail: "The slot is in the past or an account is inactive.")
        };
}