namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The outcome of a visit-access scope check: either an allowed <see cref="VisitAccessScope"/> or a refusal reason.
/// A result is never both.
/// </summary>
public sealed class VisitAccessScopeResult
{
    private VisitAccessScopeResult(VisitAccessScope? scope, VisitAccessScopeFailure failure)
    {
        Scope = scope;
        Failure = failure;
    }

    /// <summary>The verified scope, or null when the check was refused.</summary>
    public VisitAccessScope? Scope { get; }

    /// <summary>The refusal reason, or <see cref="VisitAccessScopeFailure.None"/> when the check succeeded.</summary>
    public VisitAccessScopeFailure Failure { get; }

    /// <summary>True when <see cref="Scope"/> is available.</summary>
    public bool IsAllowed => Scope is not null;

    /// <summary>Creates an allowed result.</summary>
    /// <param name="scope">The verified scope.</param>
    public static VisitAccessScopeResult Allowed(VisitAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new VisitAccessScopeResult(scope, VisitAccessScopeFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="VisitAccessScopeFailure.None"/>.</param>
    public static VisitAccessScopeResult Denied(VisitAccessScopeFailure failure)
    {
        if (failure == VisitAccessScopeFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new VisitAccessScopeResult(null, failure);
    }
}
