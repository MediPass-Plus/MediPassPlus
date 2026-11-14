using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// A test-only probe endpoint with no business rule behind it. It separates "is the real bearer authentication
/// wired correctly" from every authorization rule, so the harness smoke test can assert 401 without a token and 200
/// for each application role (NFR-SEC-10, ADR-0006). It is excluded from the generated document because it is not
/// part of the product API.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/test/probe")]
public sealed class ProbeTestController : ControllerBase
{
    /// <summary>Returns the authenticated caller's identifier and role claims.</summary>
    /// <returns>The caller's user id and roles.</returns>
    [Authorize]
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Get() => Ok(new
    {
        UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
        Roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray()
    });
}
