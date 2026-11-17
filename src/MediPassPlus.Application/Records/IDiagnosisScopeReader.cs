namespace MediPassPlus.Application.Records;

/// <summary>
/// Resolves a diagnosis to its appointment and the patient who owns it, so a medical endpoint can scope the access
/// grant to the stored row instead of trusting the request body (FR-REC-03, FR-REC-10, ADR-0005).
/// </summary>
public interface IDiagnosisScopeReader
{
    /// <summary>Finds the verified scope of one diagnosis.</summary>
    /// <param name="diagnosisId">The diagnosis identifier from the route.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored scope, or null when no diagnosis has that identifier.</returns>
    Task<DiagnosisScope?> FindAsync(Guid diagnosisId, CancellationToken cancellationToken = default);
}
