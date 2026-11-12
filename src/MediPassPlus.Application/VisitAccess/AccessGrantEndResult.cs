namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The outcome of a patient's request to end a grant: either the ended grant or a refusal reason. A result never
/// contains both.
/// </summary>
public sealed class AccessGrantEndResult
{
    private AccessGrantEndResult(AccessGrantEndedResponse? grant, AccessGrantEndFailure failure)
    {
        Grant = grant;
        Failure = failure;
    }

    /// <summary>The ended grant, or null when the request was refused.</summary>
    public AccessGrantEndedResponse? Grant { get; }

    /// <summary>The refusal reason, or <see cref="AccessGrantEndFailure.None"/> when the grant ended.</summary>
    public AccessGrantEndFailure Failure { get; }

    /// <summary>True when the grant ended.</summary>
    public bool IsSuccess => Failure == AccessGrantEndFailure.None;

    /// <summary>Creates a successful result with the ended grant.</summary>
    /// <param name="grant">The ended grant.</param>
    /// <returns>A successful result.</returns>
    public static AccessGrantEndResult Ended(AccessGrantEndedResponse grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return new AccessGrantEndResult(grant, AccessGrantEndFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="AccessGrantEndFailure.None"/>.</param>
    /// <returns>A refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="AccessGrantEndFailure.None"/>.
    /// </exception>
    public static AccessGrantEndResult Denied(AccessGrantEndFailure failure)
    {
        if (failure == AccessGrantEndFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new AccessGrantEndResult(null, failure);
    }
}
