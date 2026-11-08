namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Thrown when the persistence layer rejects a concurrent start-visit write: a second pending access request for the
/// same appointment, or a second live QR credential for the same request. A filtered unique index is the
/// authoritative guard in both cases; the API maps the conflict to <c>409</c> so the caller can retry and reuse the
/// request that won the race.
/// </summary>
public sealed class VisitStartConflictException : Exception
{
    /// <summary>Initializes a new instance with the default conflict message.</summary>
    public VisitStartConflictException()
        : base("Another request for this visit won the race.")
    {
    }

    /// <summary>Initializes a new instance with a custom message.</summary>
    /// <param name="message">The conflict message.</param>
    public VisitStartConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a custom message and the underlying database exception.</summary>
    /// <param name="message">The conflict message.</param>
    /// <param name="innerException">The database exception that detected the conflict.</param>
    public VisitStartConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
