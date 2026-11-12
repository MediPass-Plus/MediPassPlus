namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Thrown when the persistence layer rejects an access-code write: the appointment state changed, another code
/// replaced the same live credential, or the one-grant-per-appointment guard rejected the grant. The API maps it to
/// <c>409</c> so the caller can reload and retry.
/// </summary>
public sealed class AccessCodeConflictException : Exception
{
    /// <summary>Initializes a new instance with the default conflict message.</summary>
    public AccessCodeConflictException()
        : base("Another access-code request for this visit won the race.")
    {
    }

    /// <summary>Initializes a new instance with a custom message.</summary>
    /// <param name="message">The conflict message.</param>
    public AccessCodeConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a custom message and the underlying database exception.</summary>
    /// <param name="message">The conflict message.</param>
    /// <param name="innerException">The database exception that detected the conflict.</param>
    public AccessCodeConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
