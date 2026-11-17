namespace MediPassPlus.Application.Records;

/// <summary>
/// One stored diagnosis as the history reader loads it: still encrypted, because decryption happens only after the
/// request has been authorized (FR-PRV-01, NFR-SEC-05, ADR-0005).
/// </summary>
/// <param name="Id">The diagnosis identifier.</param>
/// <param name="AppointmentId">The visit that produced the diagnosis.</param>
/// <param name="DescriptionCiphertext">The stored ciphertext under <c>MediPass.Diagnosis.v1</c>; never plain text.</param>
/// <param name="DiagnosedAtUtc">When the diagnosis was recorded, in UTC. History is ordered by this column (FR-REC-02).</param>
/// <param name="SupersedesDiagnosisId">The earlier diagnosis this row corrects, when it is a correction.</param>
public sealed record DiagnosisHistoryRow(
    Guid Id,
    int AppointmentId,
    string DescriptionCiphertext,
    DateTime DiagnosedAtUtc,
    Guid? SupersedesDiagnosisId);
