namespace MediPassPlus.Application.Records;

/// <summary>
/// The result of adding a prescription. The encrypted medicine name, dosage and notes never leave the database layer
/// as part of this write response (ADR-0004, ADR-0005).
/// </summary>
/// <param name="Id">The new prescription identifier.</param>
/// <param name="DiagnosisId">The diagnosis the prescription belongs to.</param>
public sealed record PrescriptionResponse(Guid Id, Guid DiagnosisId);
