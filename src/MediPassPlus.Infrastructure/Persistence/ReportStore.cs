using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Records;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>
/// The EF Core unit of work for a report upload. The read side resolves the record and its owner from the record row
/// itself, so the caller never chooses the patient; the write side stages the append-only report row so it commits in
/// the same <c>SaveChanges</c> as its audit entry (FR-REC-05, ADR-0004 decision 7, ADR-0005).
/// </summary>
public sealed class ReportStore : IReportStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="ReportStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public ReportStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<ReportRecordScope?> FindRecordAsync(
        Guid recordId,
        CancellationToken cancellationToken = default) =>
        _context.MedicalRecords
            .AsNoTracking()
            .Where(record => record.Id == recordId)
            .Select(record => new ReportRecordScope(record.Id, record.PatientId))
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(Report report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _context.Reports.Add(report);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
