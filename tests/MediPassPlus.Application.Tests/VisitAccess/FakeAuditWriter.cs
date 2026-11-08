using MediPassPlus.Application.Audit;
using MediPassPlus.Domain.Audit;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeAuditWriter : IAuditWriter
{
    public List<AuditEntry> AddedEntries { get; } = [];

    public AuditLog Add(AuditEntry entry)
    {
        AddedEntries.Add(entry);

        return AuditLog.Create(
            entry.UserId,
            entry.PatientId,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.IpAddress,
            DateTimeOffset.UtcNow,
            entry.AccessGrantId);
    }

    public Task<AuditLog> WriteAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
