using MediPassPlus.Application.AdminManagement;
using MediPassPlus.Application.Dtos.AdminManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>Admin-only clinic management (MP-29).</summary>
[ApiController]
[Route("api/admin/clinics")]
[Authorize(Roles = "Admin")]
public class AdminClinicsController : ControllerBase
{
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";

    private readonly IClinicAdminService _clinics;

    public AdminClinicsController(IClinicAdminService clinics)
    {
        ArgumentNullException.ThrowIfNull(clinics);
        _clinics = clinics;
    }

    /// <summary>Creates a clinic.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ClinicDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateClinicDto request, CancellationToken cancellationToken)
    {
        var clinic = await _clinics.CreateAsync(request, cancellationToken);
        return Created($"/api/admin/clinics/{clinic.Id}", clinic);
    }

    /// <summary>Lists all clinics.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ClinicDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        return Ok(await _clinics.ListAsync(cancellationToken));
    }

    /// <summary>Updates a clinic.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ClinicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(int id, UpdateClinicDto request, CancellationToken cancellationToken)
    {
        var clinic = await _clinics.UpdateAsync(id, request, cancellationToken);
        if (clinic is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                type: NotFoundType,
                title: "Clinic not found.",
                detail: "No clinic has the requested identifier.");
        }

        return Ok(clinic);
    }
}