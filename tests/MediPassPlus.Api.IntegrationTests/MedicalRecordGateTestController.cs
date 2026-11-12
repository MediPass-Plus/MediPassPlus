using MediPassPlus.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// A test-only medical endpoint. It carries the real <see cref="RequireActiveGrantAttribute"/> so the HTTP suite can
/// prove that the doctor's next gated request is refused after the patient ends the grant (FR-ACS-09, NFR-PRI-02).
/// It is excluded from the generated document because it is not part of the product API.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/test/medical-record-gate")]
public sealed class MedicalRecordGateTestController : ControllerBase
{
    /// <summary>Returns 200 only while the authenticated doctor holds an active grant for the appointment.</summary>
    /// <param name="appointmentId">The appointment whose grant must be active.</param>
    /// <returns>The appointment identifier the gate allowed.</returns>
    [Authorize(Roles = "Doctor")]
    [RequireActiveGrant]
    [HttpGet("{appointmentId:int}")]
    public IActionResult Get(int appointmentId) => Ok(new { AppointmentId = appointmentId });
}
