using System.Security.Claims;
using MediPassPlus.Application.Booking;
using MediPassPlus.Application.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>Clinic staff books an appointment for a patient of their own clinic's doctors (MP-40).</summary>
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

    public StaffAppointmentsController(IStaffBookingService booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        _booking = booking;
    }

    /// <summary>Books a free future slot of a doctor of the staff member's clinic for a patient.</summary>
    /// <response code="201">The appointment was created.</response>
    /// <response code="400">The slot is in the past, an account is inactive, or the body is invalid.</response>
    /// <response code="403">The caller has no clinic staff profile.</response>
    /// <response code="404">The doctor is not in the staff member's clinic, or the patient or slot does not exist.</response>
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
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var staffUserId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                type: UnauthorizedType,
                title: "Authentication required.",
                detail: "The access token does not identify a user.");
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
            StaffBookingFailure.DoctorNotFound or StaffBookingFailure.PatientNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                type: NotFoundType,
                title: "Not found.",
                detail: "No such doctor in your clinic or no such patient."),
            StaffBookingFailure.Booking => MapBookingFailure(result.BookingFailure),
            _ => throw new InvalidOperationException($"Unexpected failure: {result.Failure}.")
        };
    }

    private IActionResult MapBookingFailure(BookingFailure failure) =>
        failure switch
        {
            BookingFailure.DoctorNotFound or
            BookingFailure.PatientNotFound or
            BookingFailure.SlotNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                type: NotFoundType,
                title: "Not found.",
                detail: "The doctor, patient or slot does not exist."),
            BookingFailure.SlotAlreadyBooked or BookingFailure.AppointmentConflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: ConflictType,
                title: "The slot is already booked.",
                detail: "This slot already has an active appointment."),
            _ => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                type: BadRequestType,
                title: "The appointment cannot be booked.",
                detail: "The slot is in the past, belongs to another doctor, or an account is inactive.")
        };
}