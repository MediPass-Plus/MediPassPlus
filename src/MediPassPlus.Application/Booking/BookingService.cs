using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.Booking;

/// <summary>
/// Applies the booking rules in one place: a slot must exist, belong to the requested doctor, start in the future and
/// be free; the doctor and patient must be active; and an appointment can be cancelled or rescheduled only while it is
/// <see cref="AppointmentStatus.Booked"/> and has not started. The store commits the appointment and slot changes in
/// one transaction, and a filtered unique index makes a concurrent double booking impossible (FR-BKG-03,
/// FR-BKG-04, FR-BKG-05, FR-BKG-09, NFR-REL-03).
/// </summary>
public sealed class BookingService : IBookingService
{
    private readonly IBookingStore _store;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="BookingService"/> class.</summary>
    /// <param name="store">The unit of work that loads and stages the booking state.</param>
    /// <param name="timeProvider">The clock used for the future and started checks.</param>
    public BookingService(IBookingStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<BookingResult> BookAsync(
        int doctorId,
        int slotId,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        var context = await _store.LoadBookingSlotAsync(
            slotId,
            doctorId,
            patientUserId,
            cancellationToken);

        if (context is null)
        {
            return BookingResult.Denied(BookingFailure.SlotNotFound);
        }

        if (context.Doctor is null || context.DoctorUser is null)
        {
            return BookingResult.Denied(BookingFailure.DoctorNotFound);
        }

        if (context.PatientUser is null)
        {
            return BookingResult.Denied(BookingFailure.PatientNotFound);
        }

        if (context.Slot.DoctorId != context.Doctor.Id)
        {
            return BookingResult.Denied(BookingFailure.SlotDoctorMismatch);
        }

        if (!context.DoctorUser.IsActive)
        {
            return BookingResult.Denied(BookingFailure.DoctorInactive);
        }

        if (!context.PatientUser.IsActive)
        {
            return BookingResult.Denied(BookingFailure.PatientInactive);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (context.Slot.StartTimeUtc <= now)
        {
            return BookingResult.Denied(BookingFailure.SlotInPast);
        }

        if (!context.Slot.IsAvailable || context.HasActiveAppointment)
        {
            return BookingResult.Denied(BookingFailure.SlotAlreadyBooked);
        }

        var appointment = new Appointment
        {
            PatientId = patientUserId,
            DoctorId = context.Doctor.Id,
            DoctorSlotId = context.Slot.Id,
            Status = AppointmentStatus.Booked,
            CreatedAtUtc = now
        };

        // The slot flag is the fast listing signal; the filtered unique index is the authoritative double-booking
        // guard. Both changes commit in the same SaveChanges transaction.
        context.Slot.IsAvailable = false;
        _store.AddAppointment(appointment);

        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (BookingConflictException)
        {
            return BookingResult.Denied(BookingFailure.SlotAlreadyBooked);
        }

        return BookingResult.Success(ToDto(appointment, context.Slot));
    }

    /// <inheritdoc />
    public async Task<BookingResult> CancelAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        var context = await _store.LoadAppointmentAsync(appointmentId, patientUserId, cancellationToken);
        if (context is null)
        {
            return BookingResult.Denied(BookingFailure.AppointmentNotFound);
        }

        if (context.PatientUser is null)
        {
            return BookingResult.Denied(BookingFailure.PatientNotFound);
        }

        if (!context.PatientUser.IsActive)
        {
            return BookingResult.Denied(BookingFailure.PatientInactive);
        }

        var appointment = context.Appointment;
        var slot = appointment.DoctorSlot
            ?? throw new InvalidOperationException("The appointment has no doctor slot.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (slot.StartTimeUtc <= now)
        {
            return BookingResult.Denied(BookingFailure.AppointmentAlreadyStarted);
        }

        // Cancelling is always possible for the patient, even when the doctor account was deactivated: FR-BKG-04
        // only requires the appointment not to have started, and blocking it would leave the slot occupied forever.
        // The domain method is the single owner of the only allowed cancellation transition, Booked -> Cancelled
        // (FR-BKG-04, FR-BKG-08).
        if (!appointment.TryCancel(now))
        {
            return BookingResult.Denied(BookingFailure.AppointmentNotCancellable);
        }

        slot.IsAvailable = true;

        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (BookingConflictException)
        {
            return BookingResult.Denied(BookingFailure.AppointmentConflict);
        }

        return BookingResult.Success(ToDto(appointment, slot));
    }

    /// <inheritdoc />
    public async Task<BookingResult> RescheduleAsync(
        int appointmentId,
        int newSlotId,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        var context = await _store.LoadAppointmentAsync(appointmentId, patientUserId, cancellationToken);
        if (context is null)
        {
            return BookingResult.Denied(BookingFailure.AppointmentNotFound);
        }

        if (context.Doctor is null || context.DoctorUser is null)
        {
            return BookingResult.Denied(BookingFailure.DoctorNotFound);
        }

        if (context.PatientUser is null)
        {
            return BookingResult.Denied(BookingFailure.PatientNotFound);
        }

        if (!context.DoctorUser.IsActive)
        {
            return BookingResult.Denied(BookingFailure.DoctorInactive);
        }

        if (!context.PatientUser.IsActive)
        {
            return BookingResult.Denied(BookingFailure.PatientInactive);
        }

        var appointment = context.Appointment;
        var currentSlot = appointment.DoctorSlot
            ?? throw new InvalidOperationException("The appointment has no doctor slot.");

        if (appointment.Status != AppointmentStatus.Booked)
        {
            return BookingResult.Denied(BookingFailure.AppointmentNotReschedulable);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (currentSlot.StartTimeUtc <= now)
        {
            return BookingResult.Denied(BookingFailure.AppointmentAlreadyStarted);
        }

        if (newSlotId == appointment.DoctorSlotId)
        {
            return BookingResult.Denied(BookingFailure.AppointmentAlreadyOnSlot);
        }

        var target = await _store.LoadTargetSlotAsync(newSlotId, cancellationToken);
        if (target is null)
        {
            return BookingResult.Denied(BookingFailure.SlotNotFound);
        }

        if (target.Slot.DoctorId != appointment.DoctorId)
        {
            return BookingResult.Denied(BookingFailure.SlotDoctorMismatch);
        }

        if (target.Slot.StartTimeUtc <= now)
        {
            return BookingResult.Denied(BookingFailure.SlotInPast);
        }

        if (!target.Slot.IsAvailable || target.HasActiveAppointment)
        {
            return BookingResult.Denied(BookingFailure.SlotAlreadyBooked);
        }

        // Only the slot changes. DoctorId and PatientId stay exactly as they were on the stored appointment, so the
        // appointment/doctor/patient scope is preserved (FR-BKG-05).
        appointment.DoctorSlotId = target.Slot.Id;
        currentSlot.IsAvailable = true;
        target.Slot.IsAvailable = false;

        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (BookingConflictException)
        {
            return BookingResult.Denied(BookingFailure.AppointmentConflict);
        }

        return BookingResult.Success(ToDto(appointment, target.Slot));
    }

    private static AppointmentDto ToDto(Appointment appointment, DoctorSlot slot) =>
        new(
            appointment.Id,
            appointment.PatientId,
            appointment.DoctorId,
            appointment.DoctorSlotId,
            appointment.Status.ToString(),
            slot.StartTimeUtc,
            slot.EndTimeUtc,
            appointment.CreatedAtUtc);
}
