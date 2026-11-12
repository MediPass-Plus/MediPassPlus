namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// The outcome of a staff check-in: either the staff-facing appointment view or a refusal reason.
/// </summary>
public sealed class CheckInResult
{
    private CheckInResult(StaffAppointmentDto? appointment, CheckInFailure failure)
    {
        Appointment = appointment;
        Failure = failure;
    }

    /// <summary>The updated appointment, or null when the check-in was refused.</summary>
    public StaffAppointmentDto? Appointment { get; }

    /// <summary>The refusal reason, or <see cref="CheckInFailure.None"/> when the check-in succeeded.</summary>
    public CheckInFailure Failure { get; }

    /// <summary>True when <see cref="Appointment"/> is available.</summary>
    public bool IsSuccess => Appointment is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="appointment">The updated appointment.</param>
    /// <returns>A successful result.</returns>
    public static CheckInResult Success(StaffAppointmentDto appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        return new CheckInResult(appointment, CheckInFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="CheckInFailure.None"/>.</param>
    /// <returns>A refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="CheckInFailure.None"/>.
    /// </exception>
    public static CheckInResult Denied(CheckInFailure failure)
    {
        if (failure == CheckInFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new CheckInResult(null, failure);
    }
}
