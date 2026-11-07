using MediPassPlus.Application.Audit;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence.Audit;

/// <summary>
/// Writes append-only audit entries through the application's <see cref="MediPassDbContext"/>.
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly MediPassDbContext _db;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AuditWriter"/> class.</summary>
    /// <param name="db">The current unit of work.</param>
    /// <param name="timeProvider">The clock used to timestamp entries.</param>
    public AuditWriter(MediPassDbContext db, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _db = db;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public AuditLog Add(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var log = AuditLog.Create(
            entry.UserId,
            entry.PatientId,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.IpAddress,
            _timeProvider.GetUtcNow(),
            entry.AccessGrantId);

        _db.AuditLogs.Add(log);
        return log;
    }

    /// <inheritdoc />
    public async Task<AuditLog> WriteAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        var log = Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return log;
    }
}
