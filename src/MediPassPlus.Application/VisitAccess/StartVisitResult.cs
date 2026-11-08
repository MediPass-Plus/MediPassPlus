namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The outcome of starting a visit: either the one-time response or a refusal reason. A result is never both.
/// </summary>
public sealed class StartVisitResult
{
    private StartVisitResult(StartVisitResponse? response, StartVisitFailure failure)
    {
        Response = response;
        Failure = failure;
    }

    /// <summary>The one-time response, or null when the request was refused.</summary>
    public StartVisitResponse? Response { get; }

    /// <summary>The refusal reason, or <see cref="StartVisitFailure.None"/> when the visit started.</summary>
    public StartVisitFailure Failure { get; }

    /// <summary>True when <see cref="Response"/> is available.</summary>
    public bool IsSuccess => Response is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="response">The one-time response.</param>
    /// <returns>A successful result.</returns>
    public static StartVisitResult Success(StartVisitResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new StartVisitResult(response, StartVisitFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="StartVisitFailure.None"/>.</param>
    /// <returns>A refused result.</returns>
    public static StartVisitResult Denied(StartVisitFailure failure)
    {
        if (failure == StartVisitFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new StartVisitResult(null, failure);
    }
}
