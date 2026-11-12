using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Common;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// Applies the appointment state machine for staff check-in and doctor completion. The transition rules live on
/// <see cref="Appointment"/> so staff, visit access and booking share exactly one implementation (FR-BKG-08,
/// FR-STF-03, FR-ACS-08). "Today" is the clinic-local date in Africa/Cairo while every stored timestamp stays UTC.
/// Completion also schedules the visit's grant to end 15 minutes later, capped by its two-hour limit (FR-ACS-07).
/// </summary>
public sealed class AppointmentStatusService : IAppointmentStatusService
{
    private readonly IAppointmentStatusStore _store;
    private readonly IClinicTimeZone _clinicTimeZone;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AppointmentStatusService"/> class.</summary>
    /// <param name="store">The unit of work that loads and stages the appointment state.</param>
    /// <param name="clinicTimeZone">The clinic calendar used for the "today" checks.</param>
    /// <param name="auditWriter">Stages the access-end audit row in the same unit of work.</param>
    /// <param name="timeProvider">The clock used for the transition timestamps.</param>
    public AppointmentStatusService(
        IAppointmentStatusStore store,
        IClinicTimeZone clinicTimeZone,
        IAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clinicTimeZone);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _clinicTimeZone = clinicTimeZone;
        _auditWriter = auditWriter;
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
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

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

        var grant = context.Grant;
        if (grant is not null && grant.IsActiveAt(now))
        {
            // Completion schedules the end 15 minutes later, capped by the two-hour limit; the grant stays active
            // during the grace window (FR-ACS-07, UC-18, ADR-0011 decision 3).
            grant.CompleteVisit(now);

            // TODO: a completion and a patient end that are truly concurrent can overwrite each other because
            // AccessGrant has no concurrency token. Sequential behaviour is correct; add a rowversion migration only
            // if this race becomes relevant.
            _auditWriter.Add(new AuditEntry(
                doctorUserId,
                grant.PatientId,
                AuditAction.AccessEnded,
                AuditEntityType.AccessGrant,
                grant.Id.ToString(),
                ipAddress,
                grant.Id));
        }

        try
        {
            // The appointment transition, the grant end and the audit row commit together (ADR-0004 decision 7).
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
