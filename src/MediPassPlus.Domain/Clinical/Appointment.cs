namespace MediPassPlus.Domain.Clinical;

/// <summary>
/// A patient's appointment with one doctor at one slot. The status only moves through the explicit transitions
/// below, and every reached state keeps its UTC timestamp (FR-BKG-08).
/// </summary>
public class Appointment
{
    public int Id { get; set; }

    // The Id of the patient's user account (ApplicationUser.Id)
    public Guid PatientId { get; set; }

    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    public int DoctorSlotId { get; set; }
    public DoctorSlot DoctorSlot { get; set; } = null!;

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Booked;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the clinic staff marked the patient as arrived, in UTC.</summary>
    public DateTime? CheckedInAtUtc { get; set; }

    /// <summary>When the doctor started the visit, in UTC.</summary>
    public DateTime? InVisitAtUtc { get; set; }

    /// <summary>When the doctor completed the visit, in UTC.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>When the patient cancelled the appointment, in UTC.</summary>
    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>
    /// Moves a <see cref="AppointmentStatus.Booked"/> appointment to <see cref="AppointmentStatus.CheckedIn"/>.
    /// Any other state is refused and left untouched (FR-BKG-08, FR-STF-03).
    /// </summary>
    /// <param name="utcNow">The current UTC time.</param>
    /// <returns>True when the transition happened.</returns>
    public bool TryCheckIn(DateTime utcNow)
    {
        if (Status != AppointmentStatus.Booked)
        {
            return false;
        }

        Status = AppointmentStatus.CheckedIn;
        CheckedInAtUtc = utcNow;
        return true;
    }

    /// <summary>
    /// Moves a <see cref="AppointmentStatus.CheckedIn"/> appointment to <see cref="AppointmentStatus.InVisit"/>.
    /// Any other state is refused and left untouched (FR-BKG-08, FR-ACS-01).
    /// </summary>
    /// <param name="utcNow">The current UTC time.</param>
    /// <returns>True when the transition happened.</returns>
    public bool TryMarkInVisit(DateTime utcNow)
    {
        if (Status != AppointmentStatus.CheckedIn)
        {
            return false;
        }

        Status = AppointmentStatus.InVisit;
        InVisitAtUtc = utcNow;
        return true;
    }

    /// <summary>
    /// Moves an <see cref="AppointmentStatus.InVisit"/> appointment to <see cref="AppointmentStatus.Completed"/>.
    /// Any other state is refused and left untouched (FR-BKG-08, FR-ACS-08).
    /// </summary>
    /// <param name="utcNow">The current UTC time.</param>
    /// <returns>True when the transition happened.</returns>
    public bool TryComplete(DateTime utcNow)
    {
        if (Status != AppointmentStatus.InVisit)
        {
            return false;
        }

        Status = AppointmentStatus.Completed;
        CompletedAtUtc = utcNow;
        return true;
    }

    /// <summary>
    /// Moves a <see cref="AppointmentStatus.Booked"/> appointment to <see cref="AppointmentStatus.Cancelled"/>.
    /// Any other state is refused and left untouched (FR-BKG-04, FR-BKG-08). "Before the visit starts" is interpreted
    /// as before the appointment's scheduled start and while it is still Booked: once staff checks the patient in,
    /// the patient has arrived and cancellation is refused.
    /// </summary>
    /// <param name="utcNow">The current UTC time.</param>
    /// <returns>True when the transition happened.</returns>
    public bool TryCancel(DateTime utcNow)
    {
        if (Status != AppointmentStatus.Booked)
        {
            return false;
        }

        Status = AppointmentStatus.Cancelled;
        CancelledAtUtc = utcNow;
        return true;
    }
}
