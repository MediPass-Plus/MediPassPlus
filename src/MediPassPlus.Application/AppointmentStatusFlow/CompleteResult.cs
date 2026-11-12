namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// The outcome of a doctor completion: either the resulting status or a refusal reason.
/// </summary>
public sealed class CompleteResult
{
    private CompleteResult(DoctorAppointmentStatusDto? appointment, CompleteFailure failure)
    {
        Appointment = appointment;
        Failure = failure;
    }

    /// <summary>The completed appointment, or null when the completion was refused.</summary>
    public DoctorAppointmentStatusDto? Appointment { get; }

    /// <summary>The refusal reason, or <see cref="CompleteFailure.None"/> when the completion succeeded.</summary>
    public CompleteFailure Failure { get; }

    /// <summary>True when <see cref="Appointment"/> is available.</summary>
    public bool IsSuccess => Appointment is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="appointment">The completed appointment.</param>
    /// <returns>A successful result.</returns>
    public static CompleteResult Success(DoctorAppointmentStatusDto appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        return new CompleteResult(appointment, CompleteFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="CompleteFailure.None"/>.</param>
    /// <returns>A refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="CompleteFailure.None"/>.
    /// </exception>
    public static CompleteResult Denied(CompleteFailure failure)
    {
        if (failure == CompleteFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new CompleteResult(null, failure);
    }
}
