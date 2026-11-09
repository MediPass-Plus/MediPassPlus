using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Privacy;

/// <summary>
/// The outcome of an access-grant check: either the active <see cref="AccessGrant"/> or a refusal reason.
/// A result is never both.
/// </summary>
public sealed class AccessGrantCheckResult
{
    private AccessGrantCheckResult(AccessGrant? grant, AccessGrantCheckFailure failure)
    {
        Grant = grant;
        Failure = failure;
    }

    /// <summary>The active grant that authorizes the request, or null when the check was refused.</summary>
    public AccessGrant? Grant { get; }

    /// <summary>The refusal reason, or <see cref="AccessGrantCheckFailure.None"/> when the check succeeded.</summary>
    public AccessGrantCheckFailure Failure { get; }

    /// <summary>True when <see cref="Grant"/> is available.</summary>
    public bool IsAllowed => Grant is not null;

    /// <summary>Creates an allowed result.</summary>
    /// <param name="grant">The active grant.</param>
    /// <returns>The allowed result that carries <paramref name="grant"/>.</returns>
    public static AccessGrantCheckResult Allowed(AccessGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return new AccessGrantCheckResult(grant, AccessGrantCheckFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="AccessGrantCheckFailure.None"/>.</param>
    /// <returns>The refused result that carries <paramref name="failure"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="AccessGrantCheckFailure.None"/>.
    /// </exception>
    public static AccessGrantCheckResult Denied(AccessGrantCheckFailure failure)
    {
        if (failure == AccessGrantCheckFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new AccessGrantCheckResult(null, failure);
    }
}
