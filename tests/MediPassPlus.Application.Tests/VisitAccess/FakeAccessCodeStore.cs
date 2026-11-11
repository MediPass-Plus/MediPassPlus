using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeAccessCodeStore : IAccessCodeStore
{
    public AccessCodeContext? Context { get; set; }

    public List<VisitAccessCredential> AddedCredentials { get; } = [];

    public List<AccessGrant> AddedGrants { get; } = [];

    public int SaveCount { get; private set; }

    public bool WasLoaded { get; private set; }

    public int? ConflictOnSaveNumber { get; set; }

    public Task<AccessCodeContext?> LoadAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        WasLoaded = true;
        return Task.FromResult(Context);
    }

    public void AddCredential(VisitAccessCredential credential) => AddedCredentials.Add(credential);

    public void AddGrant(AccessGrant grant) => AddedGrants.Add(grant);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;

        if (SaveCount == ConflictOnSaveNumber)
        {
            throw new AccessCodeConflictException();
        }

        return Task.CompletedTask;
    }
}
