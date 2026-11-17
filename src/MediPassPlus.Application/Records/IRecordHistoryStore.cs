namespace MediPassPlus.Application.Records;

/// <summary>
/// The read side of a patient's permanent record. It resolves the record from the patient account and returns
/// ciphertext rows only: decryption belongs to the service, after the request has been authorized (FR-PRV-01,
/// NFR-SEC-05, ADR-0005).
/// </summary>
public interface IRecordHistoryStore
{
    /// <summary>
    /// Resolves the single permanent record of one patient. The patient identifier always comes from the verified
    /// grant or from the authenticated token, never from the request body or query string (FR-REC-01, FR-REC-07).
    /// </summary>
    /// <param name="patientUserId">The patient account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record header, or null when the patient has no record.</returns>
    Task<MedicalRecordSummary?> FindRecordAsync(
        Guid patientUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads one page of a record's diagnoses, newest first, with the total count for the whole record
    /// (FR-REC-02, NFR-PRF-04).
    /// </summary>
    /// <param name="medicalRecordId">The record the page is read from.</param>
    /// <param name="skip">How many rows to skip.</param>
    /// <param name="take">The page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page of encrypted diagnoses and the total count.</returns>
    Task<PagedRows<DiagnosisHistoryRow>> ListDiagnosesAsync(
        Guid medicalRecordId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the prescriptions of the diagnoses on the current page. The join to the diagnosis and its record is
    /// part of the filter, so a prescription can never be attached to a diagnosis outside the requested record.
    /// </summary>
    /// <param name="medicalRecordId">The record the page belongs to.</param>
    /// <param name="diagnosisIds">The diagnoses on the current page.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The encrypted prescriptions of those diagnoses, ordered by identifier.</returns>
    Task<IReadOnlyList<PrescriptionHistoryRow>> ListPrescriptionsAsync(
        Guid medicalRecordId,
        IReadOnlyCollection<Guid> diagnosisIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads one page of a record's reports, newest upload first, with the total count for the whole record
    /// (NFR-PRF-04).
    /// </summary>
    /// <param name="medicalRecordId">The record the page is read from.</param>
    /// <param name="skip">How many rows to skip.</param>
    /// <param name="take">The page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page of report metadata and the total count.</returns>
    Task<PagedRows<ReportMetadataRow>> ListReportsAsync(
        Guid medicalRecordId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
