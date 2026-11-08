namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Thrown when the persistence layer rejects a second pending access request for the same appointment. The filtered
/// unique index is the authoritative guard; the API maps this conflict to <c>409</c> so the caller can retry and
/// reuse the request that won the race.
/// </summary>
public sealed class PendingRequestConflictException : Exception
{
    /// <summary>Initializes a new instance with the default conflict message.</summary>
    public PendingRequestConflictException()
        : base("Another pending access request already exists for this appointment.")
    {
    }

    /// <summary>Initializes a new instance with a custom message.</summary>
    /// <param name="message">The conflict message.</param>
    public PendingRequestConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a custom message and the underlying database exception.</summary>
    /// <param name="message">The conflict message.</param>
    /// <param name="innerException">The database exception that detected the conflict.</param>
    public PendingRequestConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
