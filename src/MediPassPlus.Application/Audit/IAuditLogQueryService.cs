using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Audit;

/// <summary>Returns the authenticated patient's own audit rows, newest first (FR-PRV-03, US-32, UC-20).</summary>
public interface IAuditLogQueryService
{
    /// <summary>
    /// Returns one page of the audit rows that belong to the authenticated patient's own record. The page is validated
    /// and capped before it reaches the store (NFR-PRF-04), and reading the list is not itself audited
    /// (ADR-0004 decision 4).
    /// </summary>
    /// <param name="patientUserId">The authenticated patient, resolved from the token.</param>
    /// <param name="query">The page and page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page of the patient's audit rows.</returns>
    Task<PagedResult<AuditLogEntryResponse>> GetForPatientAsync(
        Guid patientUserId,
        MyAuditQuery query,
        CancellationToken cancellationToken = default);
}
