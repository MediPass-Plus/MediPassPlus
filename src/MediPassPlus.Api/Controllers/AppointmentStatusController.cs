using System.Security.Claims;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.AppointmentStatusFlow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// Appointment status endpoints. Clinic staff check a booked appointment in (<c>Booked</c> -> <c>CheckedIn</c>) and
/// the doctor completes a started visit (<c>InVisit</c> -> <c>Completed</c>). The domain entity owns the transition
/// rules, so a skipped or backward state is refused. This controller owns the single completion route; visit access
/// ends the grant when it calls this transition (FR-BKG-08, FR-STF-03, FR-ACS-08).
/// </summary>
[ApiController]
[Route("api/appointments")]
public class AppointmentStatusController : ControllerBase
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private readonly IAppointmentStatusService _appointmentStatusService;

    /// <summary>Initializes a new instance of the <see cref="AppointmentStatusController"/> class.</summary>
    /// <param name="appointmentStatusService">The appointment status application service.</param>
    public AppointmentStatusController(IAppointmentStatusService appointmentStatusService)
    {
        ArgumentNullException.ThrowIfNull(appointmentStatusService);
        _appointmentStatusService = appointmentStatusService;
    }

    /// <summary>
    /// Marks an appointment of today in the staff member's own clinic as arrived. Only a <c>Booked</c> appointment
    /// can be checked in, and the transition records its UTC timestamp (FR-STF-03, FR-BKG-08).
    /// </summary>
    /// <param name="appointmentId">The appointment to check in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The staff-facing appointment view.</returns>
    /// <response code="200">The appointment was checked in.</response>
    /// <response code="400">The appointment is not today or is not in the <c>Booked</c> state.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not staff or has no clinic-staff profile.</response>
    /// <response code="404">
    /// No appointment has the requested identifier, it belongs to another clinic, or its patient account no longer
    /// exists.
    /// </response>
    /// <response code="409">A concurrent request changed the appointment status first.</response>
    [Authorize(Roles = "Staff")]
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
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var result = await _appointmentStatusService.CheckInAsync(
            appointmentId,
            staffUserId,
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Appointment);
        }

        return result.Failure switch
        {
            CheckInFailure.AppointmentNotFound or
            CheckInFailure.PatientNotFound or
            CheckInFailure.ClinicMismatch => CreateProblem(
                StatusCodes.Status404NotFound,
                NotFoundType,
                "Appointment not found.",
                "No appointment with that identifier is in the staff member's clinic."),
            CheckInFailure.StaffNotFound => CreateProblem(
                StatusCodes.Status403Forbidden,
                ForbiddenType,
                "You cannot check in this appointment.",
                "The authenticated user has no clinic-staff profile."),
            CheckInFailure.AppointmentNotToday => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The appointment cannot be checked in yet.",
                "Only an appointment scheduled for today can be checked in."),
            CheckInFailure.AppointmentNotBooked => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The appointment cannot be checked in.",
                "Only a booked appointment can be checked in."),
            CheckInFailure.AppointmentConflict => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The appointment conflicted with another request.",
                "Another request changed the appointment status first. Refresh and retry."),
            _ => throw new InvalidOperationException($"Unexpected check-in failure: {result.Failure}.")
        };
    }

    /// <summary>
    /// Completes the doctor's own appointment when the visit is in progress. Only an <c>InVisit</c> appointment can
    /// be completed, and the transition records its UTC timestamp (FR-ACS-08, FR-BKG-08). Completion also schedules
    /// the visit's active access grant to end 15 minutes later, capped by its two-hour limit (FR-ACS-07, UC-18), and
    /// writes the <c>AccessEnded</c> audit row in the same unit of work (ADR-0004 decision 7).
    /// </summary>
    /// <param name="appointmentId">The appointment to complete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The appointment identifier, its new status and the completion timestamp.</returns>
    /// <response code="200">
    /// The visit was completed. When the visit had an active grant, it now ends 15 minutes after completion or at its
    /// two-hour limit, whichever is first.
    /// </response>
    /// <response code="400">The appointment is not in the <c>InVisit</c> state.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not a doctor, has no doctor profile, or does not own the appointment.</response>
    /// <response code="404">No appointment has the requested identifier.</response>
    /// <response code="409">A concurrent request changed the appointment status first.</response>
    [Authorize(Roles = "Doctor")]
    [HttpPost("{appointmentId:int}/complete")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(DoctorAppointmentStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(int appointmentId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var doctorUserId))
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var result = await _appointmentStatusService.CompleteAsync(
            appointmentId,
            doctorUserId,
            HttpContext.GetAuditIpAddress(),
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Appointment);
        }

        return result.Failure switch
        {
            CompleteFailure.AppointmentNotFound => CreateProblem(
                StatusCodes.Status404NotFound,
                NotFoundType,
                "Appointment not found.",
                "No appointment has the requested identifier."),
            CompleteFailure.DoctorNotFound or CompleteFailure.DoctorMismatch => CreateProblem(
                StatusCodes.Status403Forbidden,
                ForbiddenType,
                "You cannot complete this visit.",
                "You can complete only your own appointment."),
            CompleteFailure.AppointmentNotInVisit => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The visit cannot be completed.",
                "Only an appointment that is in the visit can be completed."),
            CompleteFailure.AppointmentConflict => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The appointment conflicted with another request.",
                "Another request changed the appointment status first. Refresh and retry."),
            _ => throw new InvalidOperationException($"Unexpected completion failure: {result.Failure}.")
        };
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out userId) && userId != Guid.Empty;
    }

    private ObjectResult CreateProblem(int statusCode, string type, string title, string detail) =>
        Problem(statusCode: statusCode, type: type, title: title, detail: detail);
}
