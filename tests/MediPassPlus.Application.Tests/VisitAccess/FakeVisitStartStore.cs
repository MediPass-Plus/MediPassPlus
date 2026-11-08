using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeVisitStartStore : IVisitStartStore
{
    public StartVisitContext? Context { get; set; }

    public List<AccessRequest> AddedRequests { get; } = [];

    public List<VisitAccessCredential> AddedCredentials { get; } = [];

    public int SaveCount { get; private set; }

    public bool WasLoaded { get; private set; }

    public bool ThrowPendingRequestConflictOnSave { get; set; }

    public Task<StartVisitContext?> LoadAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        WasLoaded = true;
        return Task.FromResult(Context);
    }

    public void AddRequest(AccessRequest request) => AddedRequests.Add(request);

    public void AddCredential(VisitAccessCredential credential) => AddedCredentials.Add(credential);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;

        if (ThrowPendingRequestConflictOnSave)
        {
            throw new PendingRequestConflictException();
        }

        return Task.CompletedTask;
    }
}
