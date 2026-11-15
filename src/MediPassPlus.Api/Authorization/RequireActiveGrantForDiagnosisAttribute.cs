using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Authorization;

/// <summary>
/// Marks a medical endpoint that may only run while the authenticated doctor holds an active access grant for the
/// visit that produced the route's <c>diagnosisId</c> (FR-PRV-01, FR-ACS-06, ADR-0005, ADR-0011). The check runs
/// against the database on every request through <see cref="DiagnosisAccessGrantFilter"/>; it never trusts a patient,
/// appointment or grant identifier from the request body.
/// </summary>
/// <remarks>
/// Combine it with the role check and expose a GUID <c>diagnosisId</c> parameter. The filter fails closed with
/// <c>403</c> when that parameter is missing, invalid, or does not resolve to a diagnosis.
/// <code>
/// [Authorize(Roles = "Doctor")]
/// [RequireActiveGrantForDiagnosis]
/// [HttpPost("api/diagnoses/{diagnosisId:guid}/prescriptions")]
/// public IActionResult AddPrescription(Guid diagnosisId) => Ok();
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireActiveGrantForDiagnosisAttribute : TypeFilterAttribute
{
    /// <summary>Initializes a new instance of the <see cref="RequireActiveGrantForDiagnosisAttribute"/> class.</summary>
    public RequireActiveGrantForDiagnosisAttribute()
        : base(typeof(DiagnosisAccessGrantFilter))
    {
        // Run before the global validation filter (order 0): a caller without a grant gets 403, not validation
        // feedback about a record it may not touch.
        Order = -100;
    }
}
