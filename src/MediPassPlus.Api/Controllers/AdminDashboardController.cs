using MediPassPlus.Application.AdminManagement;
using MediPassPlus.Application.Dtos.AdminManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>Admin-only dashboard of aggregate counts (FR-STF-08, FR-STF-09).</summary>
[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Roles = "Admin")]
public class AdminDashboardController : ControllerBase
{
    private readonly IAdminDashboardService _dashboard;

    public AdminDashboardController(IAdminDashboardService dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        _dashboard = dashboard;
    }

    /// <summary>
    /// Returns the number of active users per role, the number of clinics and the number of appointments per
    /// status. The response holds counts only: no patient identifier and no medical data (FR-STF-09, FR-PRV-06).
    /// </summary>
    /// <response code="200">The counts were returned.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="403">The caller is not an admin.</response>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AdminDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        return Ok(await _dashboard.GetAsync(cancellationToken));
    }
}