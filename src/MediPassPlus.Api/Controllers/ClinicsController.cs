using MediPassPlus.Application.Clinical;
using MediPassPlus.Application.Common;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ClinicsController : ControllerBase
{
    private readonly MediPassDbContext _db;

    public ClinicsController(MediPassDbContext db)
    {
        _db = db;
    }

    /// <summary>Lists clinics, one page at a time. Optional search by clinic name (case does not matter).</summary>
    /// <response code="200">A page of clinics.</response>
    /// <response code="400">The page or page size is not valid.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ClinicDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ClinicDto>>> Get(
        [FromQuery] ClinicSearchQuery query, CancellationToken ct)
    {
        var clinics = _db.Clinics.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var name = query.Search.Trim().ToLower();
            clinics = clinics.Where(c => c.Name.ToLower().Contains(name));
        }

        var total = await clinics.CountAsync(ct);

        var items = await clinics
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip(query.GetSkip())
            .Take(query.GetPageSize())
            .Select(c => new ClinicDto
            {
                Id = c.Id,
                Name = c.Name,
                Address = c.Address,
                Phone = c.Phone
            })
            .ToListAsync(ct);

        return Ok(new PagedResult<ClinicDto>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.GetPageSize()
        });
    }
}


