namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The outcome of generating a six-digit code: either the one-time response or a refusal reason. A result is never
/// both.
/// </summary>
public sealed class AccessCodeGenerationResult
{
    private AccessCodeGenerationResult(AccessCodeResponse? response, AccessCodeGenerationFailure failure)
    {
        Response = response;
        Failure = failure;
    }

    /// <summary>The one-time response, or null when the request was refused.</summary>
    public AccessCodeResponse? Response { get; }

    /// <summary>The refusal reason, or <see cref="AccessCodeGenerationFailure.None"/> when the code was generated.</summary>
    public AccessCodeGenerationFailure Failure { get; }

    /// <summary>True when <see cref="Response"/> is available.</summary>
    public bool IsSuccess => Response is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="response">The one-time response.</param>
    /// <returns>A successful result.</returns>
    public static AccessCodeGenerationResult Success(AccessCodeResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new AccessCodeGenerationResult(response, AccessCodeGenerationFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="AccessCodeGenerationFailure.None"/>.</param>
    /// <returns>A refused result.</returns>
    public static AccessCodeGenerationResult Denied(AccessCodeGenerationFailure failure)
    {
        if (failure == AccessCodeGenerationFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new AccessCodeGenerationResult(null, failure);
    }
}
