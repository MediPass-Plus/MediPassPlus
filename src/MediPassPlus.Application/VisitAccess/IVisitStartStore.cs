using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The unit of work for starting a visit. The read side resolves the tracked appointment and its access state; the
/// write side stages a request or credential and commits them together.
/// </summary>
public interface IVisitStartStore
{
    /// <summary>
    /// Loads the appointment, doctor, requests and live QR credentials for a verified scope, or null when the
    /// appointment is no longer there. The returned entities are tracked so credentials can be revoked.
    /// </summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="doctorId">The appointment's doctor identifier.</param>
    /// <param name="patientId">The appointment's patient user identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked start-visit state, or null when it cannot be resolved.</returns>
    Task<StartVisitContext?> LoadAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new access request.</summary>
    /// <param name="request">The pending request to add.</param>
    void AddRequest(AccessRequest request);

    /// <summary>Stages a new visit-access credential.</summary>
    /// <param name="credential">The credential to add.</param>
    void AddCredential(VisitAccessCredential credential);

    /// <summary>Commits the staged changes.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
