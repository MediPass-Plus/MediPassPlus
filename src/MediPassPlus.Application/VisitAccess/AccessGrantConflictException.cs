namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Thrown when the persistence layer rejects an access-grant change because another request ended the grant first.
/// The earlier end always stands, so access is never reopened (FR-ACS-09, FR-ACS-11, ADR-0011). The API maps it to
/// <c>409</c> and asks the caller to refresh and retry.
/// </summary>
public sealed class AccessGrantConflictException : Exception
{
    /// <summary>Initializes a new instance with the default conflict message.</summary>
    public AccessGrantConflictException()
        : base("Another request ended the access grant first.")
    {
    }

    /// <summary>Initializes a new instance with a custom message.</summary>
    /// <param name="message">The conflict message.</param>
    public AccessGrantConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a custom message and the underlying database exception.</summary>
    /// <param name="message">The conflict message.</param>
    /// <param name="innerException">The database exception that detected the conflict.</param>
    public AccessGrantConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
