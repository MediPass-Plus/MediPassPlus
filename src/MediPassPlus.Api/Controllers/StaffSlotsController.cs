using System.Security.Claims;
using MediPassPlus.Application.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>Clinic staff slot management for the doctors of their own clinic (MP-40).</summary>
[ApiController]
[Route("api/staff/doctors/{doctorId:int}/slots")]
[Authorize(Roles = "Staff")]
public class StaffSlotsController : ControllerBase
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private readonly IStaffSlotService _slots;

    public StaffSlotsController(IStaffSlotService slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        _slots = slots;
    }

    /// <summary>Adds a slot for a doctor of the staff member's clinic. Overlapping slots are refused.</summary>
    /// <response code="201">The slot was created.</response>
    /// <response code="400">The slot is invalid or starts in the past.</response>
    /// <response code="403">The caller has no clinic staff profile.</response>
    /// <response code="404">The doctor does not exist in the staff member's clinic.</response>
    /// <response code="409">The slot overlaps another slot of the doctor.</response>
    [HttpPost]
    [Produces("application/json")]
    [ProducesResponseType(typeof(StaffSlotDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        int doctorId, CreateStaffSlotDto request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var staffUserId))
        {
            return Unauthorized401();
        }

        var result = await _slots.CreateAsync(staffUserId, doctorId, request, cancellationToken);

        return result.IsSuccess
            ? Created($"/api/staff/doctors/{doctorId}/slots/{result.Slot!.Id}", result.Slot)
            : MapFailure(result.Failure);
    }

    /// <summary>Deletes an unbooked slot of a doctor of the staff member's clinic.</summary>
    /// <response code="204">The slot was deleted.</response>
    /// <response code="403">The caller has no clinic staff profile.</response>
    /// <response code="404">The slot does not exist in the staff member's clinic.</response>
    /// <response code="409">The slot has an active appointment.</response>
    [HttpDelete("{slotId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int doctorId, int slotId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var staffUserId))
        {
            return Unauthorized401();
        }

        var result = await _slots.DeleteAsync(staffUserId, doctorId, slotId, cancellationToken);

        return result.IsSuccess ? NoContent() : MapFailure(result.Failure);
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId != Guid.Empty;

    private IActionResult Unauthorized401() =>
        Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            type: UnauthorizedType,
            title: "Authentication required.",
            detail: "The access token does not identify a user.");

    private IActionResult MapFailure(StaffSlotFailure failure) =>
        failure switch
        {
            StaffSlotFailure.StaffProfileNotFound => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                type: ForbiddenType,
                title: "No clinic staff profile.",
                detail: "This account is not linked to a clinic."),
            StaffSlotFailure.DoctorNotFound or StaffSlotFailure.SlotNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                type: NotFoundType,
                title: "Not found.",
                detail: "No such doctor or slot exists in your clinic."),
            StaffSlotFailure.SlotInPast => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                type: BadRequestType,
                title: "The slot starts in the past.",
                detail: "A slot must start in the future."),
            StaffSlotFailure.SlotOverlaps => Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: ConflictType,
                title: "The slot overlaps another slot.",
                detail: "The doctor already has a slot in that time range."),
            StaffSlotFailure.SlotAlreadyBooked => Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: ConflictType,
                title: "The slot is booked.",
                detail: "A slot with an active appointment cannot be deleted."),
            _ => throw new InvalidOperationException($"Unexpected slot failure: {failure}.")
        };
}