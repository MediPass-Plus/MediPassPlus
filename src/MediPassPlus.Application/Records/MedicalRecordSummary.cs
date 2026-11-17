namespace MediPassPlus.Application.Records;

/// <summary>
/// The header of a patient's permanent record, resolved from the record row itself and never from caller input
/// (FR-REC-01, ADR-0005).
/// </summary>
/// <param name="Id">The record identifier.</param>
/// <param name="CreatedAtUtc">When the record was created, in UTC.</param>
public sealed record MedicalRecordSummary(Guid Id, DateTime CreatedAtUtc);
