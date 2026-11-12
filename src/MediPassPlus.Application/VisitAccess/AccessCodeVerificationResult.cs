namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The outcome of verifying a six-digit code: either the created grant or a refusal reason. A result never contains
/// both a grant and a failure.
/// </summary>
public sealed class AccessCodeVerificationResult
{
    private AccessCodeVerificationResult(AccessGrantResponse? grant, AccessCodeVerificationFailure failure)
    {
        Grant = grant;
        Failure = failure;
    }

    /// <summary>The grant created by a match, or null for a refusal.</summary>
    public AccessGrantResponse? Grant { get; }

    /// <summary>The refusal reason, or <see cref="AccessCodeVerificationFailure.None"/> when the code matched.</summary>
    public AccessCodeVerificationFailure Failure { get; }

    /// <summary>True when the code matched and the grant was created.</summary>
    public bool IsSuccess => Failure == AccessCodeVerificationFailure.None;

    /// <summary>Creates a successful result carrying the created grant.</summary>
    /// <param name="grant">The created grant.</param>
    /// <returns>A successful result.</returns>
    public static AccessCodeVerificationResult Granted(AccessGrantResponse grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return new AccessCodeVerificationResult(grant, AccessCodeVerificationFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="AccessCodeVerificationFailure.None"/>.</param>
    /// <returns>A refused result.</returns>
    public static AccessCodeVerificationResult Denied(AccessCodeVerificationFailure failure)
    {
        if (failure == AccessCodeVerificationFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new AccessCodeVerificationResult(null, failure);
    }
}
