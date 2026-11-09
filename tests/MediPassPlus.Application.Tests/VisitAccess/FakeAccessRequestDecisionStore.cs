using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeAccessRequestDecisionStore : IAccessRequestDecisionStore
{
    public AccessRequest? Request { get; set; }

    public AccessRequestDecisionContext? Context { get; set; }

    public List<AccessGrant> AddedGrants { get; } = [];

    public int SaveCount { get; private set; }

    public bool WasLoaded { get; private set; }

    public VisitAccessScope? LastScope { get; private set; }

    public bool ConflictOnSave { get; set; }

    public Task<AccessRequest?> FindRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Request is not null && Request.Id == requestId ? Request : null);

    public Task<AccessRequestDecisionContext?> LoadAsync(
        Guid requestId,
        VisitAccessScope scope,
        CancellationToken cancellationToken = default)
    {
        WasLoaded = true;
        LastScope = scope;
        return Task.FromResult(Context);
    }

    public void AddGrant(AccessGrant grant) => AddedGrants.Add(grant);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;

        if (ConflictOnSave)
        {
            throw new AccessRequestDecisionConflictException();
        }

        return Task.CompletedTask;
    }
}
