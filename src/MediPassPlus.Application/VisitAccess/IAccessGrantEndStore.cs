using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The unit of work for the patient's immediate end. The read side resolves the grant and re-checks the verified
/// appointment scope; the write side commits the grant end and its audit row together (FR-ACS-09, ADR-0004,
/// ADR-0011).
/// </summary>
public interface IAccessGrantEndStore
{
    /// <summary>Finds a grant by its identifier without tracking it.</summary>
    /// <param name="grantId">The grant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The grant, or null when it does not exist.</returns>
    Task<AccessGrant?> FindGrantAsync(Guid grantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the tracked grant only when it still matches the verified appointment, doctor and patient scope.
    /// </summary>
    /// <param name="grantId">The grant identifier.</param>
    /// <param name="scope">The appointment scope verified for the authenticated patient.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked grant, or null when it no longer matches the scope.</returns>
    Task<AccessGrant?> LoadAsync(
        Guid grantId,
        VisitAccessScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>Commits the ended grant and its staged audit row.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
