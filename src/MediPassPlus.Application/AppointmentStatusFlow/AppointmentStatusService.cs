using MediPassPlus.Application.Common;
using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// Applies the appointment state machine for staff check-in and doctor completion. The transition rules live on
/// <see cref="Appointment"/> so staff, visit access and booking share exactly one implementation (FR-BKG-08,
/// FR-STF-03, FR-ACS-08). "Today" is the clinic-local date in Africa/Cairo while every stored timestamp stays UTC.
/// </summary>
public sealed class AppointmentStatusService : IAppointmentStatusService
{
    private readonly IAppointmentStatusStore _store;
    private readonly IClinicTimeZone _clinicTimeZone;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AppointmentStatusService"/> class.</summary>
    /// <param name="store">The unit of work that loads and stages the appointment state.</param>
    /// <param name="clinicTimeZone">The clinic calendar used for the "today" checks.</param>
    /// <param name="timeProvider">The clock used for the transition timestamps.</param>
    public AppointmentStatusService(
        IAppointmentStatusStore store,
        IClinicTimeZone clinicTimeZone,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clinicTimeZone);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _clinicTimeZone = clinicTimeZone;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<CheckInResult> CheckInAsync(
        int appointmentId,
        Guid staffUserId,
        CancellationToken cancellationToken = default)
    {
        var context = await _store.LoadForCheckInAsync(appointmentId, staffUserId, cancellationToken);
        if (context is null)
        {
            return CheckInResult.Denied(CheckInFailure.AppointmentNotFound);
        }

        if (context.Staff is null)
        {
            return CheckInResult.Denied(CheckInFailure.StaffNotFound);
        }

        if (context.Doctor is null)
        {
            return CheckInResult.Denied(CheckInFailure.AppointmentNotFound);
        }

        if (context.Patient is null)
        {
            return CheckInResult.Denied(CheckInFailure.PatientNotFound);
        }

        if (context.Staff.ClinicId != context.Doctor.ClinicId)
        {
            return CheckInResult.Denied(CheckInFailure.ClinicMismatch);
        }

        var slot = context.Appointment.DoctorSlot
            ?? throw new InvalidOperationException("The appointment has no doctor slot.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var today = _clinicTimeZone.GetLocalDate(now);
        if (!_clinicTimeZone.IsLocalDate(slot.StartTimeUtc, today))
        {
            return CheckInResult.Denied(CheckInFailure.AppointmentNotToday);
        }

        if (!context.Appointment.TryCheckIn(now))
        {
            return CheckInResult.Denied(CheckInFailure.AppointmentNotBooked);
        }

        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (AppointmentStatusConflictException)
        {
            return CheckInResult.Denied(CheckInFailure.AppointmentConflict);
        }

        return CheckInResult.Success(new StaffAppointmentDto(
            context.Appointment.Id,
            context.Patient.FullName,
            context.Patient.PhoneNumber,
            slot.StartTimeUtc,
            slot.EndTimeUtc,
            context.Appointment.Status.ToString(),
            context.Appointment.CheckedInAtUtc
                ?? throw new InvalidOperationException("A checked-in appointment must have a timestamp.")));
    }

    /// <inheritdoc />
    public async Task<CompleteResult> CompleteAsync(
        int appointmentId,
        Guid doctorUserId,
        CancellationToken cancellationToken = default)
    {
        var context = await _store.LoadForCompleteAsync(appointmentId, doctorUserId, cancellationToken);
        if (context is null)
        {
            return CompleteResult.Denied(CompleteFailure.AppointmentNotFound);
        }

        if (context.Doctor is null)
        {
            return CompleteResult.Denied(CompleteFailure.DoctorNotFound);
        }

        if (context.Appointment.DoctorId != context.Doctor.Id)
        {
            return CompleteResult.Denied(CompleteFailure.DoctorMismatch);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!context.Appointment.TryComplete(now))
        {
            return CompleteResult.Denied(CompleteFailure.AppointmentNotInVisit);
        }

        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (AppointmentStatusConflictException)
        {
            return CompleteResult.Denied(CompleteFailure.AppointmentConflict);
        }

        return CompleteResult.Success(new DoctorAppointmentStatusDto(
            context.Appointment.Id,
            context.Appointment.Status.ToString(),
            context.Appointment.CompletedAtUtc
                ?? throw new InvalidOperationException("A completed appointment must have a timestamp.")));
    }
}
