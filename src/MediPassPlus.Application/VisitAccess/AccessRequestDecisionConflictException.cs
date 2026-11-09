namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Thrown when the persistence layer rejects a patient decision because another decision won the race or because the
/// appointment already has a grant. The API maps it to <c>409</c>.
/// </summary>
public sealed class AccessRequestDecisionConflictException : Exception
{
    /// <summary>Initializes a new instance with the default conflict message.</summary>
    public AccessRequestDecisionConflictException()
        : base("Another decision for this access request won the race.")
    {
    }

    /// <summary>Initializes a new instance with a custom message.</summary>
    /// <param name="message">The conflict message.</param>
    public AccessRequestDecisionConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a custom message and the underlying database exception.</summary>
    /// <param name="message">The conflict message.</param>
    /// <param name="innerException">The database exception that detected the conflict.</param>
    public AccessRequestDecisionConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
