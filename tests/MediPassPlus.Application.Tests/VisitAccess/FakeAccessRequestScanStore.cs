using MediPassPlus.Application.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeAccessRequestScanStore : IAccessRequestScanStore
{
    public AccessRequestScanContext? Context { get; set; }

    public string? LastTokenHash { get; private set; }

    public Task<AccessRequestScanContext?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        LastTokenHash = tokenHash;
        return Task.FromResult(Context);
    }
}
