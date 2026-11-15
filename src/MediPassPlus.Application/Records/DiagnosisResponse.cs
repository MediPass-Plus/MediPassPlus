namespace MediPassPlus.Application.Records;

/// <summary>
/// The result of recording a diagnosis. It exposes only identifiers and the server timestamp; the encrypted
/// description never leaves the database layer as part of this write response (ADR-0004, ADR-0005).
/// </summary>
/// <param name="Id">The new diagnosis identifier.</param>
/// <param name="AppointmentId">The appointment the diagnosis is scoped to.</param>
/// <param name="DiagnosedAtUtc">When the server recorded the diagnosis, in UTC.</param>
public sealed record DiagnosisResponse(Guid Id, int AppointmentId, DateTime DiagnosedAtUtc);
