using MediPassPlus.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediPassPlus.Infrastructure.Persistence.Audit;

/// <summary>
/// Rejects EF changes that would update or delete an audit row (NFR-PRI-03, ADR-0004 decision 6).
/// </summary>
public sealed class AuditLogImmutabilityInterceptor : SaveChangesInterceptor
{
    private const string ErrorMessage =
        "Audit rows are append-only; updates and deletes are not permitted.";

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Guard(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (EntityEntry<AuditLog> entry in context.ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(ErrorMessage);
            }
        }
    }
}
