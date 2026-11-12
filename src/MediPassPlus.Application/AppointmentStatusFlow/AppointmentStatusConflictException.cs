namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// Thrown when the persistence layer rejects a status change because another request changed the appointment first.
/// The API maps it to <c>409</c>.
/// </summary>
public sealed class AppointmentStatusConflictException : Exception
{
    /// <summary>Initializes a new instance with the default conflict message.</summary>
    public AppointmentStatusConflictException()
        : base("Another request changed the appointment status first.")
    {
    }

    /// <summary>Initializes a new instance with a custom message.</summary>
    /// <param name="message">The conflict message.</param>
    public AppointmentStatusConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a custom message and the underlying database exception.</summary>
    /// <param name="message">The conflict message.</param>
    /// <param name="innerException">The database exception that detected the conflict.</param>
    public AppointmentStatusConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
