using MediPassPlus.Application.Audit;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence.Audit;

/// <summary>
/// The EF Core read side of the patient's "who viewed my record" list. Rows are selected by the patient whose data was
/// affected, so a doctor's read appears in that patient's list, paging happens in SQL through the patient timeline index,
/// and no medical content is selected (FR-PRV-03, NFR-PRF-04, NFR-PRI-05, ADR-0004).
/// </summary>
public sealed class AuditLogReader : IAuditLogReader
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="AuditLogReader"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public AuditLogReader(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<AuditLogPage> ReadForPatientAsync(
        Guid patientUserId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var rows = _context.AuditLogs
            .AsNoTracking()
            .Where(audit => audit.PatientId == patientUserId);

        var totalCount = await rows.CountAsync(cancellationToken);

        // The identifier is a v7 GUID, so the tie-breaker keeps two rows with the same timestamp in a stable order.
        var items = await rows
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .ThenByDescending(audit => audit.Id)
            .Skip(skip)
            .Take(take)
            .Select(audit => new AuditLogRow(
                audit.Id,
                audit.UserId,
                audit.Action,
                audit.EntityType,
                audit.EntityId,
                audit.OccurredAtUtc,
                audit.IpAddress,
                audit.AccessGrantId))
            .ToListAsync(cancellationToken);

        return new AuditLogPage(items, totalCount);
    }
}
