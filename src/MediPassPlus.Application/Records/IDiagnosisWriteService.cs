namespace MediPassPlus.Application.Records;

/// <summary>
/// The doctor write path for a patient's medical record: recording a diagnosis and adding a prescription to it
/// (FR-REC-03, FR-REC-04, ADR-0005). Every operation re-checks the active grant against the database, encrypts the
/// clinical text before it is staged, and writes its audit row in the same unit of work (FR-PRV-01, NFR-SEC-05).
/// </summary>
public interface IDiagnosisWriteService
{
    /// <summary>
    /// Records a diagnosis for the appointment the verified grant is scoped to. The medical record, appointment and
    /// patient are derived on the server; the request carries only the diagnosis text (ADR-0005).
    /// </summary>
    /// <param name="appointmentId">The appointment from the route.</param>
    /// <param name="doctorUserId">The authenticated doctor's user account identifier.</param>
    /// <param name="request">The plaintext diagnosis text.</param>
    /// <param name="ipAddress">The connection IP address recorded on the audit row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new diagnosis or a refusal reason.</returns>
    Task<DiagnosisWriteResult> CreateDiagnosisAsync(
        int appointmentId,
        Guid doctorUserId,
        CreateDiagnosisRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds one prescription to a diagnosis that belongs to the verified grant's appointment and patient. The grant is
    /// scoped through the stored diagnosis, never through a caller-supplied appointment or patient identifier.
    /// </summary>
    /// <param name="diagnosisId">The diagnosis from the route.</param>
    /// <param name="doctorUserId">The authenticated doctor's user account identifier.</param>
    /// <param name="request">The plaintext prescription text.</param>
    /// <param name="ipAddress">The connection IP address recorded on the audit row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new prescription or a refusal reason.</returns>
    Task<PrescriptionWriteResult> CreatePrescriptionAsync(
        Guid diagnosisId,
        Guid doctorUserId,
        CreatePrescriptionRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default);
}
