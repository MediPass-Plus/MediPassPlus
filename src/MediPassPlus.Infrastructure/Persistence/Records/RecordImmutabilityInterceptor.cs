using MediPassPlus.Domain.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediPassPlus.Infrastructure.Persistence.Records;

/// <summary>
/// Rejects EF changes that would update or delete a diagnosis, a prescription or a report (FR-REC-09, ADR-0005).
/// Corrections are new rows that supersede the corrected one, so clinical history can never be rewritten in place,
/// mirroring the audit log guard (ADR-0004 decision 6).
/// </summary>
public sealed class RecordImmutabilityInterceptor : SaveChangesInterceptor
{
    private const string ErrorMessage =
        "Medical record rows are append-only; updates and deletes are not permitted.";

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

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is Diagnosis or Prescription or Report)
            {
                throw new InvalidOperationException(ErrorMessage);
            }
        }
    }
}
