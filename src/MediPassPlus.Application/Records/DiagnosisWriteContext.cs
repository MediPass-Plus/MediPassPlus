using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.Records;

/// <summary>
/// The stored state a diagnosis write needs: the appointment status and the patient's permanent record. Both are
/// resolved on the server from the verified grant's patient, never from the request body (FR-REC-03, ADR-0005).
/// </summary>
/// <param name="Status">The appointment status, which must be <c>InVisit</c> or <c>Completed</c>.</param>
/// <param name="MedicalRecordId">
/// The patient's permanent record, or null when the registration invariant is broken and no record exists.
/// </param>
public sealed record DiagnosisWriteContext(AppointmentStatus Status, Guid? MedicalRecordId);
