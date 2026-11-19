namespace MediPassPlus.Application.Audit;

/// <summary>
/// The read side of the audit log for the patient's own view. The patient identifier always comes from the
/// authenticated token, never from the request, and only append-only audit rows are returned (FR-PRV-03, ADR-0004).
/// </summary>
public interface IAuditLogReader
{
    /// <summary>
    /// Loads one page of a patient's audit rows, newest first, with the total count for their whole history. Rows are
    /// selected by the patient whose data was affected, so a doctor's read appears in that patient's list
    /// (FR-PRV-03, NFR-PRI-05).
    /// </summary>
    /// <param name="patientUserId">The patient account whose rows are read; it comes from the authenticated token.</param>
    /// <param name="skip">How many rows to skip.</param>
    /// <param name="take">The page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page and the total count for the patient.</returns>
    Task<AuditLogPage> ReadForPatientAsync(
        Guid patientUserId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
