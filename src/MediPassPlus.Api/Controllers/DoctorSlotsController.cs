using MediPassPlus.Application.Clinical;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.Controllers;

[Route("api/doctors/{doctorId:int}/slots")]
[ApiController]
public class DoctorSlotsController : ControllerBase
{
    private readonly MediPassDbContext _db;

    public DoctorSlotsController(MediPassDbContext db)
    {
        _db = db;
    }

    /// <summary>Lists the future, unbooked slots of one active doctor. Optional time window.</summary>
    /// <param name="doctorId">The doctor id.</param>
    /// <param name="query">The optional time window (From and To).</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The available slots, oldest first.</response>
    /// <response code="400">The time window is not valid.</response>
    /// <response code="404">The doctor does not exist or is not active.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SlotDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SlotDto>>> Get(
        int doctorId, [FromQuery] SlotSearchQuery query, CancellationToken ct)
    {
        var doctorIsActive = await (
            from d in _db.Doctors
            join u in _db.Users on d.UserId equals u.Id
            where d.Id == doctorId && u.IsActive
            select d.Id).AnyAsync(ct);

        if (!doctorIsActive)
            return NotFound();

        // Past slots are never returned: the window starts no earlier than now.
        var now = DateTime.UtcNow;
        var from = query.From?.UtcDateTime ?? now;
        if (from < now)
            from = now;

        var slots = _db.DoctorSlots.AsNoTracking()
            .Where(s => s.DoctorId == doctorId
                && s.IsAvailable
                && s.StartTimeUtc >= from
                && !_db.Appointments.Any(a => a.DoctorSlotId == s.Id && a.Status != AppointmentStatus.Cancelled));

        if (query.To is not null)
        {
            var to = query.To.Value.UtcDateTime;
            slots = slots.Where(s => s.StartTimeUtc < to);
        }

        var items = await slots
            .OrderBy(s => s.StartTimeUtc).ThenBy(s => s.Id)
            .Select(s => new SlotDto
            {
                Id = s.Id,
                DoctorId = s.DoctorId,
                StartTimeUtc = s.StartTimeUtc,
                EndTimeUtc = s.EndTimeUtc
            })
            .ToListAsync(ct);

        return Ok(items);
    }
}
