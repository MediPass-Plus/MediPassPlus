namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The outcome of a patient's approve or reject decision: either the created grant (approval), a successful
/// rejection without a grant, or a refusal reason. A result never contains both a grant and a failure.
/// </summary>
public sealed class AccessRequestDecisionResult
{
    private AccessRequestDecisionResult(AccessGrantResponse? grant, AccessRequestDecisionFailure failure)
    {
        Grant = grant;
        Failure = failure;
    }

    /// <summary>The grant created by an approval, or null for a rejection or a refusal.</summary>
    public AccessGrantResponse? Grant { get; }

    /// <summary>
    /// The refusal reason, or <see cref="AccessRequestDecisionFailure.None"/> when the request was approved or
    /// rejected.
    /// </summary>
    public AccessRequestDecisionFailure Failure { get; }

    /// <summary>True when the request was approved or rejected.</summary>
    public bool IsSuccess => Failure == AccessRequestDecisionFailure.None;

    /// <summary>Creates an approved result with its grant.</summary>
    /// <param name="grant">The grant created by the approval.</param>
    /// <returns>An approved result.</returns>
    public static AccessRequestDecisionResult Approved(AccessGrantResponse grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return new AccessRequestDecisionResult(grant, AccessRequestDecisionFailure.None);
    }

    /// <summary>Creates a successful rejection result. Rejection never creates a grant.</summary>
    /// <returns>A rejection result.</returns>
    public static AccessRequestDecisionResult Rejected() =>
        new(null, AccessRequestDecisionFailure.None);

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="AccessRequestDecisionFailure.None"/>.</param>
    /// <returns>A refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="AccessRequestDecisionFailure.None"/>.
    /// </exception>
    public static AccessRequestDecisionResult Denied(AccessRequestDecisionFailure failure)
    {
        if (failure == AccessRequestDecisionFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new AccessRequestDecisionResult(null, failure);
    }
}
