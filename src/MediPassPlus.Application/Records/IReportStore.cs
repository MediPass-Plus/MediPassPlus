using MediPassPlus.Domain.Records;

namespace MediPassPlus.Application.Records;

/// <summary>
/// The unit of work for a report upload: the read side resolves the record and its owner, the write side stages the
/// append-only row so it commits in the same transaction as its audit entry (FR-REC-05, ADR-0004 decision 7, ADR-0005).
/// </summary>
public interface IReportStore
{
    /// <summary>
    /// Finds the permanent record with its owner. The owner is read from the row, never taken from the request.
    /// </summary>
    /// <param name="recordId">The record identifier from the route.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record scope, or null when no record has that identifier.</returns>
    Task<ReportRecordScope?> FindRecordAsync(Guid recordId, CancellationToken cancellationToken = default);

    /// <summary>Stages a new report row in the current unit of work.</summary>
    /// <param name="report">The report to add.</param>
    void Add(Report report);

    /// <summary>Commits the staged report row and its audit entry in one transaction.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the transaction is committed.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
