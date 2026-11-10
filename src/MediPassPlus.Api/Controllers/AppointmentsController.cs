using System.Security.Claims;
using MediPassPlus.Application.Booking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// Patient booking endpoints: book a free doctor slot, cancel a booked appointment and reschedule it to another
/// free slot of the same doctor (FR-BKG-03, FR-BKG-04, FR-BKG-05, FR-BKG-09). Every action is scoped to the authenticated patient, and every
/// refusal is returned as the shared <see cref="ProblemDetails"/> envelope (ADR-0010).
/// </summary>
[ApiController]
[Route("api/appointments")]
[Authorize(Roles = "Patient")]
public class AppointmentsController : ControllerBase
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private readonly IBookingService _bookingService;

    /// <summary>Initializes a new instance of the <see cref="AppointmentsController"/> class.</summary>
    /// <param name="bookingService">The booking application service.</param>
    public AppointmentsController(IBookingService bookingService)
    {
        ArgumentNullException.ThrowIfNull(bookingService);
        _bookingService = bookingService;
    }

    /// <summary>
    /// Books a free, future slot of the requested doctor for the authenticated patient. The slot and the new
    /// appointment are written in one unit of work, and a concurrent booking of the same slot is refused with
    /// <c>409</c> (FR-BKG-03).
    /// </summary>
    /// <param name="request">The doctor and slot to book.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created appointment.</returns>
    /// <response code="201">The appointment was created and the slot is no longer available.</response>
    /// <response code="400">
    /// The doctor or patient is inactive, the slot belongs to another doctor, the slot is in the past, or the
    /// request body is invalid.
    /// </response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not a patient.</response>
    /// <response code="404">The doctor or slot does not exist.</response>
    /// <response code="409">The slot is already booked, or a concurrent request booked it first.</response>
    [HttpPost]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Book(
        BookAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetPatientUserId(out var patientUserId))
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var result = await _bookingService.BookAsync(
            request.DoctorId,
            request.SlotId,
            patientUserId,
            cancellationToken);

        if (result.IsSuccess)
        {
            var appointment = result.Appointment
                ?? throw new InvalidOperationException("A successful booking must contain an appointment.");

            return Created($"/api/appointments/{appointment.Id}", appointment);
        }

        return MapFailure(result.Failure);
    }

    /// <summary>
    /// Cancels one of the authenticated patient's booked appointments before its slot starts and frees the slot.
    /// Cancelling does not require the doctor account to be active, so a patient can always release the slot
    /// (FR-BKG-04).
    /// </summary>
    /// <param name="appointmentId">The appointment to cancel.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cancelled appointment.</returns>
    /// <response code="200">The appointment was cancelled and its slot is available again.</response>
    /// <response code="400">The appointment is not booked, or its slot has already started.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not a patient.</response>
    /// <response code="404">No appointment with that identifier belongs to the authenticated patient.</response>
    /// <response code="409">A concurrent request changed the appointment or slot first.</response>
    [HttpPost("{appointmentId:int}/cancel")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(int appointmentId, CancellationToken cancellationToken)
    {
        if (!TryGetPatientUserId(out var patientUserId))
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var result = await _bookingService.CancelAsync(appointmentId, patientUserId, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Appointment)
            : MapFailure(result.Failure);
    }

    /// <summary>
    /// Moves one of the authenticated patient's booked appointments to another free slot of the same doctor,
    /// before the current slot starts. The appointment, doctor and patient stay in the same scope (FR-BKG-05).
    /// </summary>
    /// <param name="appointmentId">The appointment to reschedule.</param>
    /// <param name="request">The target slot.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rescheduled appointment.</returns>
    /// <response code="200">The appointment was moved and the old slot is available again.</response>
    /// <response code="400">
    /// The appointment is not booked, it has already started, the target is its current slot, the target belongs
    /// to another doctor, the target is in the past, the doctor or patient is inactive, or the body is invalid.
    /// </response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not a patient.</response>
    /// <response code="404">The appointment, doctor or target slot does not exist.</response>
    /// <response code="409">The target slot is already booked, or a concurrent request changed the state first.</response>
    [HttpPost("{appointmentId:int}/reschedule")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reschedule(
        int appointmentId,
        RescheduleAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetPatientUserId(out var patientUserId))
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var result = await _bookingService.RescheduleAsync(
            appointmentId,
            request.NewSlotId,
            patientUserId,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Appointment)
            : MapFailure(result.Failure);
    }

    private bool TryGetPatientUserId(out Guid patientUserId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out patientUserId) && patientUserId != Guid.Empty;
    }

    private IActionResult MapFailure(BookingFailure failure) =>
        failure switch
        {
            BookingFailure.DoctorNotFound or
            BookingFailure.PatientNotFound or
            BookingFailure.SlotNotFound or
            BookingFailure.AppointmentNotFound => CreateProblem(
                StatusCodes.Status404NotFound,
                NotFoundType,
                "Not found.",
                "The requested doctor, slot or appointment does not exist in your scope."),
            BookingFailure.DoctorInactive => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The doctor is not available.",
                "The doctor's account is inactive."),
            BookingFailure.PatientInactive => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The patient account is not active.",
                "An inactive patient account cannot book, cancel or reschedule."),
            BookingFailure.SlotInPast => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The slot has already started.",
                "A slot can be booked or used as a reschedule target only while it starts in the future."),
            BookingFailure.SlotDoctorMismatch => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The slot belongs to another doctor.",
                "A slot can be booked only with the doctor who owns it."),
            BookingFailure.AppointmentNotCancellable => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The appointment cannot be cancelled.",
                "Only a booked appointment can be cancelled."),
            BookingFailure.AppointmentNotReschedulable => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The appointment cannot be rescheduled.",
                "Only a booked appointment can be rescheduled."),
            BookingFailure.AppointmentAlreadyStarted => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The appointment has already started.",
                "A started appointment cannot be cancelled or rescheduled."),
            BookingFailure.AppointmentAlreadyOnSlot => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The appointment is already on that slot.",
                "Choose a different slot to reschedule the appointment."),
            BookingFailure.SlotAlreadyBooked => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The slot is already booked.",
                "This slot already has an active appointment."),
            BookingFailure.AppointmentConflict => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The appointment conflicted with another request.",
                "Another request changed the appointment or slot state first. Refresh and retry."),
            _ => throw new InvalidOperationException($"Unexpected booking failure: {failure}.")
        };

    private ObjectResult CreateProblem(int statusCode, string type, string title, string detail) =>
        Problem(statusCode: statusCode, type: type, title: title, detail: detail);
}
