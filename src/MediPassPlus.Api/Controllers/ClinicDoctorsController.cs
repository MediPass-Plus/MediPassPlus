using MediPassPlus.Application.Clinical;
using MediPassPlus.Application.Common;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.Controllers;

[Route("api/clinics/{clinicId:int}/doctors")]
[ApiController]
public class ClinicDoctorsController : ControllerBase
{
    private readonly MediPassDbContext _db;

    public ClinicDoctorsController(MediPassDbContext db)
    {
        _db = db;
    }

    /// <summary>Lists the active doctors of one clinic, one page at a time. Optional specialty filter and name search (case does not matter).</summary>
    /// <param name="clinicId">The clinic id.</param>
    /// <param name="query">The specialty filter, the name search and the paging options.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">A page of doctors.</response>
    /// <response code="400">The page or page size is not valid.</response>
    /// <response code="404">The clinic does not exist.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<DoctorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<DoctorDto>>> Get(
        int clinicId, [FromQuery] DoctorSearchQuery query, CancellationToken ct)
    {
        var clinicExists = await _db.Clinics.AnyAsync(c => c.Id == clinicId, ct);
        if (!clinicExists)
            return NotFound();

        var doctors =
            from d in _db.Doctors.AsNoTracking()
            join u in _db.Users.AsNoTracking() on d.UserId equals u.Id
            where d.ClinicId == clinicId && u.IsActive
            select new DoctorDto
            {
                Id = d.Id,
                FullName = u.FullName,
                Specialty = d.Specialty,
                ClinicId = d.ClinicId,
                ClinicName = d.Clinic.Name
            };

        if (!string.IsNullOrWhiteSpace(query.Specialty))
        {
            var specialty = query.Specialty.Trim().ToLower();
            doctors = doctors.Where(d => d.Specialty.ToLower() == specialty);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            doctors = doctors.Where(d => d.FullName.ToLower().Contains(search));
        }

        var total = await doctors.CountAsync(ct);

        var items = await doctors
            .OrderBy(d => d.FullName).ThenBy(d => d.Id)
            .Skip(query.GetSkip())
            .Take(query.GetPageSize())
            .ToListAsync(ct);

        return Ok(new PagedResult<DoctorDto>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.GetPageSize()
        });
    }
}