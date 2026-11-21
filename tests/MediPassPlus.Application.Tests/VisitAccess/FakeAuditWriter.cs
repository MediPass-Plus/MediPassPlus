using MediPassPlus.Application.Audit;
using MediPassPlus.Domain.Audit;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeAuditWriter : IAuditWriter
{
    public List<AuditEntry> AddedEntries { get; } = [];

    public bool ThrowOnAdd { get; set; }

    public AuditLog Add(AuditEntry entry)
    {
        if (ThrowOnAdd)
        {
            throw new InvalidOperationException("The audit writer is unavailable.");
        }

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
