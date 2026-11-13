namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Read-only lookup for the scan path. The presented token is hashed before it reaches the store, so the store
/// never sees the plaintext secret (ADR-0001, MP-97).
/// </summary>
public interface IAccessRequestScanStore
{
    /// <summary>
    /// Resolves the QR credential, its request, appointment, doctor and clinic from the stored token hash.
    /// </summary>
    /// <param name="tokenHash">The SHA-256 hash of the presented token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The scan context, or null when no QR credential has that hash.</returns>
    Task<AccessRequestScanContext?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);
}
