namespace MediPassPlus.Application.Records;

/// <summary>
/// One prescription as the history endpoints return it, after the request has been authorized and the stored
/// ciphertext has been decrypted (ADR-0005).
/// </summary>
public sealed class PrescriptionHistoryItemResponse
{
    /// <summary>The prescription identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The decrypted medicine name.</summary>
    public string MedicineName { get; init; } = string.Empty;

    /// <summary>The decrypted dosage.</summary>
    public string Dosage { get; init; } = string.Empty;

    /// <summary>The decrypted optional notes, or null when there are none.</summary>
    public string? Notes { get; init; }

    /// <summary>The earlier prescription this row corrects, when it is a correction.</summary>
    public Guid? SupersedesPrescriptionId { get; init; }
}
