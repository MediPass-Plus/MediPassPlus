namespace MediPassPlus.Application.Booking;

/// <summary>
/// Thrown when the persistence layer rejects a booking write because another request booked the slot first or changed
/// the appointment/slot state. The API maps it to <c>409</c>.
/// </summary>
public sealed class BookingConflictException : Exception
{
    /// <summary>Initializes a new instance with the default conflict message.</summary>
    public BookingConflictException()
        : base("Another request won the booking race.")
    {
    }

    /// <summary>Initializes a new instance with a custom message.</summary>
    /// <param name="message">The conflict message.</param>
    public BookingConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a custom message and the underlying database exception.</summary>
    /// <param name="message">The conflict message.</param>
    /// <param name="innerException">The database exception that detected the conflict.</param>
    public BookingConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
