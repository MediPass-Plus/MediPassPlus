namespace MediPassPlus.Application.Records;

/// <summary>
/// The verified parent scope of one diagnosis. The identifier is resolved on the server from the stored row, never
/// from a caller-supplied patient or appointment identifier (FR-REC-03, ADR-0005).
/// </summary>
/// <param name="DiagnosisId">The diagnosis the request targets.</param>
/// <param name="AppointmentId">The appointment that produced the diagnosis.</param>
/// <param name="MedicalRecordId">The permanent record that holds the diagnosis.</param>
/// <param name="PatientId">The patient account that owns the record.</param>
public sealed record DiagnosisScope(
    Guid DiagnosisId,
    int AppointmentId,
    Guid MedicalRecordId,
    Guid PatientId);
