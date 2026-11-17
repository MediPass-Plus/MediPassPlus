namespace MediPassPlus.Application.Records;

/// <summary>
/// One diagnosis as the history endpoints return it, after the request has been authorized and the stored ciphertext
/// has been decrypted. A corrected row stays visible and points at the row it supersedes (FR-REC-09, ADR-0005).
/// </summary>
public sealed class DiagnosisHistoryItemResponse
{
    /// <summary>The diagnosis identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The visit that produced the diagnosis.</summary>
    public int AppointmentId { get; init; }

    /// <summary>The decrypted diagnosis text.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>When the diagnosis was recorded, in UTC.</summary>
    public DateTime DiagnosedAtUtc { get; init; }

    /// <summary>The earlier diagnosis this row corrects, when it is a correction.</summary>
    public Guid? SupersedesDiagnosisId { get; init; }

    /// <summary>The prescriptions recorded for this diagnosis, ordered by identifier.</summary>
    public IReadOnlyList<PrescriptionHistoryItemResponse> Prescriptions { get; init; } =
        Array.Empty<PrescriptionHistoryItemResponse>();
}
