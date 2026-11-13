namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The outcome of reading one appointment's access state: either the derived state or a refusal reason.
/// </summary>
public sealed class AccessStateResult
{
    private AccessStateResult(AccessStateResponse? response, AccessStateFailure failure)
    {
        Response = response;
        Failure = failure;
    }

    /// <summary>The derived state, or null when the read was refused.</summary>
    public AccessStateResponse? Response { get; }

    /// <summary>The refusal reason, or <see cref="AccessStateFailure.None"/> when the read succeeded.</summary>
    public AccessStateFailure Failure { get; }

    /// <summary>True when <see cref="Response"/> is available.</summary>
    public bool IsSuccess => Response is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="response">The derived access state.</param>
    public static AccessStateResult Success(AccessStateResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new AccessStateResult(response, AccessStateFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="AccessStateFailure.None"/>.</param>
    public static AccessStateResult Denied(AccessStateFailure failure)
    {
        if (failure == AccessStateFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new AccessStateResult(null, failure);
    }
}
