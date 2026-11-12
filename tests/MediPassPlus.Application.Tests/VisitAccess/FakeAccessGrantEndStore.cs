using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeAccessGrantEndStore : IAccessGrantEndStore
{
    public AccessGrant? Grant { get; set; }

    public AccessGrant? TrackedGrant { get; set; }

    public int SaveCount { get; private set; }

    public bool ConflictOnSave { get; set; }

    public bool WasLoaded { get; private set; }

    public VisitAccessScope? LastScope { get; private set; }

    public Task<AccessGrant?> FindGrantAsync(
        Guid grantId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Grant is not null && Grant.Id == grantId ? Grant : null);

    public Task<AccessGrant?> LoadAsync(
        Guid grantId,
        VisitAccessScope scope,
        CancellationToken cancellationToken = default)
    {
        WasLoaded = true;
        LastScope = scope;

        return Task.FromResult(
            TrackedGrant is not null && TrackedGrant.Id == grantId ? TrackedGrant : null);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;

        return ConflictOnSave
            ? throw new AccessGrantConflictException()
            : Task.CompletedTask;
    }
}
