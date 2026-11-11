using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The unit of work for the six-digit fallback: loading the tracked appointment, requests and credentials, staging a
/// new code or the grant a successful verification creates, and committing the changes together.
/// </summary>
public interface IAccessCodeStore
{
    /// <summary>
    /// Loads the tracked appointment, its slot, requests and credentials for a verified scope, or null when the
    /// appointment no longer matches.
    /// </summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="doctorId">The appointment's doctor identifier.</param>
    /// <param name="patientId">The appointment's patient user identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked access-code state, or null when it cannot be resolved.</returns>
    Task<AccessCodeContext?> LoadAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new numeric-code credential.</summary>
    /// <param name="credential">The credential to add.</param>
    void AddCredential(VisitAccessCredential credential);

    /// <summary>Stages the grant created by a successful code verification.</summary>
    /// <param name="grant">The grant to add.</param>
    void AddGrant(AccessGrant grant);

    /// <summary>Commits the staged credential, request, appointment, grant and audit changes.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="AccessCodeConflictException">
    /// The appointment state changed, another code or approval won a race, or the appointment already has a grant.
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
