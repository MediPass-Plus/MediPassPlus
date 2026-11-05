using MediPassPlus.Application.Clinical;
using MediPassPlus.Application.Common;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class DoctorsController : ControllerBase
{
    private readonly MediPassDbContext _db;

    public DoctorsController(MediPassDbContext db)
    {
        _db = db;
    }

    /// <summary>Lists doctors, one page at a time. Optional specialty filter and name search (case does not matter).</summary>
    /// <response code="200">A page of doctors.</response>
    /// <response code="400">The page or page size is not valid.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<DoctorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<DoctorDto>>> Get(
        [FromQuery] DoctorSearchQuery query, CancellationToken ct)
    {
        // The name is stored on the user account, so we join the doctor with the user.
        var doctors =
            from d in _db.Doctors.AsNoTracking()
            join u in _db.Users.AsNoTracking() on d.UserId equals u.Id
            where u.IsActive
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

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var name = query.Name.Trim().ToLower();
            doctors = doctors.Where(d => d.FullName.ToLower().Contains(name));
        }

        var total = await doctors.CountAsync(ct);

        var items = await doctors
            .OrderBy(d => d.FullName).ThenBy(d => d.Id)
            .Skip(query.Skip)
            .Take(query.EffectivePageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<DoctorDto>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.EffectivePageSize
        });
    }
}
