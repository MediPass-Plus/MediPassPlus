using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Authorization;

/// <summary>
/// Marks a medical endpoint that may only run while the authenticated doctor holds an active access grant for the
/// route's <c>appointmentId</c> (FR-PRV-01, FR-ACS-06, ADR-0011). The check runs against the database on every
/// request through <see cref="AccessGrantFilter"/>; it never trusts a role, a patient id or an appointment id from
/// the request body.
/// </summary>
/// <remarks>
/// Combine it with the role check and expose an integer <c>appointmentId</c> parameter. The filter fails closed with
/// <c>403</c> when that parameter is missing.
/// <code>
/// [Authorize(Roles = "Doctor")]
/// [RequireActiveGrant]
/// [HttpGet("api/appointments/{appointmentId:int}/record")]
/// public IActionResult GetRecord(int appointmentId) => Ok();
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireActiveGrantAttribute : TypeFilterAttribute
{
    /// <summary>Initializes a new instance of the <see cref="RequireActiveGrantAttribute"/> class.</summary>
    public RequireActiveGrantAttribute()
        : base(typeof(AccessGrantFilter))
    {
        // Run before the global validation filter (order 0): a caller without a grant gets 403, not validation
        // feedback about a record it may not touch.
        Order = -100;
    }
}
