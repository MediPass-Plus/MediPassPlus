using MediPassPlus.Domain.Audit;

namespace MediPassPlus.Application.Audit;

/// <summary>
/// Writes append-only audit entries for medical reads/writes and access-control events (FR-PRV-02, ADR-0004).
/// Callers must pass identifiers and controlled values only, never medical content.
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Stages an audit row in the current unit of work so a medical write can commit it in the same transaction
    /// (ADR-0004 decision 7). The caller must call <c>SaveChanges</c> or <c>SaveChangesAsync</c>.
    /// </summary>
    /// <param name="entry">The audit entry to stage.</param>
    /// <returns>The staged audit row.</returns>
    AuditLog Add(AuditEntry entry);

    /// <summary>
    /// Stages and commits an audit row immediately. Use for reads and access-control events that are not part of a
    /// larger write transaction. The current <c>DbContext</c> must not contain unrelated pending changes.
    /// </summary>
    /// <param name="entry">The audit entry to write.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The persisted audit row.</returns>
    Task<AuditLog> WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
