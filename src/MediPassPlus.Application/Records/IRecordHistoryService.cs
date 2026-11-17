using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Records;

/// <summary>
/// Reads a patient's permanent record through the two routes ADR-0005 defines: an appointment-scoped route for a
/// doctor who holds an active grant, and a self-scoped route for the patient. Both paths write a <c>RecordRead</c>
/// audit row, decrypt only after authorization, and return paged lists (FR-PRV-01, FR-PRV-02, NFR-PRF-04).
/// </summary>
public interface IRecordHistoryService
{
    /// <summary>
    /// Returns one page of the record of the patient the verified grant points at. The doctor and the patient come
    /// from the grant, never from the route or the request body (FR-PRV-01, FR-ACS-10, ADR-0005).
    /// </summary>
    /// <param name="appointmentId">The appointment the request is scoped to.</param>
    /// <param name="verifiedGrant">The grant a filter has already verified against the database.</param>
    /// <param name="doctorUserId">The authenticated doctor account, used for the audit row.</param>
    /// <param name="query">The paging values of both lists.</param>
    /// <param name="ipAddress">The connection IP address for the audit row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page, or a refusal reason.</returns>
    Task<RecordHistoryResult> GetForDoctorAsync(
        int appointmentId,
        AccessGrant verifiedGrant,
        Guid doctorUserId,
        RecordHistoryQuery query,
        string ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of the authenticated patient's own record. No access grant is required, and the record is
    /// resolved from the token's user identifier only (FR-REC-07, ADR-0005).
    /// </summary>
    /// <param name="patientUserId">The authenticated patient account.</param>
    /// <param name="query">The paging values of both lists.</param>
    /// <param name="ipAddress">The connection IP address for the audit row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page, or a refusal reason.</returns>
    Task<RecordHistoryResult> GetForPatientAsync(
        Guid patientUserId,
        RecordHistoryQuery query,
        string ipAddress,
        CancellationToken cancellationToken = default);
}
