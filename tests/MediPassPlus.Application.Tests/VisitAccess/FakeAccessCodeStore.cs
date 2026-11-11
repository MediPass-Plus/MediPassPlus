using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeAccessCodeStore : IAccessCodeStore
{
    public AccessCodeContext? Context { get; set; }

    public List<VisitAccessCredential> AddedCredentials { get; } = [];

    public List<AccessGrant> AddedGrants { get; } = [];

    public int SaveCount { get; private set; }

    public int FailedAttemptCount { get; private set; }

    public bool WasLoaded { get; private set; }

    public int? ConflictOnSaveNumber { get; set; }

    public AccessCodeFailedAttemptOutcome? ForcedFailedAttemptOutcome { get; set; }

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

    public Task<AccessCodeFailedAttemptOutcome> RegisterFailedAttemptAsync(
        Guid credentialId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        FailedAttemptCount++;

        if (ForcedFailedAttemptOutcome is { } forced)
        {
            return Task.FromResult(forced);
        }

        var credential = Context?.Credentials
            .SingleOrDefault(candidate => candidate.Id == credentialId);

        if (credential is null)
        {
            return Task.FromResult(AccessCodeFailedAttemptOutcome.Unavailable);
        }

        if (credential.LockedAtUtc is not null)
        {
            return Task.FromResult(AccessCodeFailedAttemptOutcome.AlreadyLocked);
        }

        if (!credential.IsUsableAt(utcNow))
        {
            return Task.FromResult(AccessCodeFailedAttemptOutcome.Unavailable);
        }

        credential.RegisterFailedAttempt(utcNow);

        return Task.FromResult(AccessCodeFailedAttemptOutcome.Recorded);
    }

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
