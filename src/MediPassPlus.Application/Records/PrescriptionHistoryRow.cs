namespace MediPassPlus.Application.Records;

/// <summary>
/// One stored prescription as the history reader loads it: every text field is still encrypted, because decryption
/// happens only after the request has been authorized (FR-PRV-01, ADR-0005).
/// </summary>
/// <param name="Id">The prescription identifier.</param>
/// <param name="DiagnosisId">The diagnosis the prescription belongs to.</param>
/// <param name="MedicineNameCiphertext">The stored ciphertext under <c>MediPass.Prescription.v1</c>.</param>
/// <param name="DosageCiphertext">The stored ciphertext under <c>MediPass.Prescription.v1</c>.</param>
/// <param name="NotesCiphertext">The stored optional notes ciphertext, or null when there are none.</param>
/// <param name="SupersedesPrescriptionId">The earlier prescription this row corrects, when it is a correction.</param>
public sealed record PrescriptionHistoryRow(
    Guid Id,
    Guid DiagnosisId,
    string MedicineNameCiphertext,
    string DosageCiphertext,
    string? NotesCiphertext,
    Guid? SupersedesPrescriptionId);
