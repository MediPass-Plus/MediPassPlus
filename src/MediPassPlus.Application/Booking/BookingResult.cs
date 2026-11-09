namespace MediPassPlus.Application.Booking;

/// <summary>
/// The outcome of a booking, cancellation or reschedule: either the resulting appointment or a refusal reason.
/// A result never contains both.
/// </summary>
public sealed class BookingResult
{
    private BookingResult(AppointmentDto? appointment, BookingFailure failure)
    {
        Appointment = appointment;
        Failure = failure;
    }

    /// <summary>The resulting appointment, or null when the operation was refused.</summary>
    public AppointmentDto? Appointment { get; }

    /// <summary>The refusal reason, or <see cref="BookingFailure.None"/> when the operation succeeded.</summary>
    public BookingFailure Failure { get; }

    /// <summary>True when <see cref="Appointment"/> is available.</summary>
    public bool IsSuccess => Appointment is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="appointment">The resulting appointment.</param>
    /// <returns>A successful result.</returns>
    public static BookingResult Success(AppointmentDto appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        return new BookingResult(appointment, BookingFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="BookingFailure.None"/>.</param>
    /// <returns>A refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="BookingFailure.None"/>.
    /// </exception>
    public static BookingResult Denied(BookingFailure failure)
    {
        if (failure == BookingFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new BookingResult(null, failure);
    }
}
