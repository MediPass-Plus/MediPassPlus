using MediPassPlus.Application.AppointmentStatusFlow;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.StaffAdmin;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>
/// The EF Core unit of work for the appointment status flow. The read side resolves the appointment with its doctor
/// and slot, the authenticated staff profile and the patient account; the write side maps a lost concurrency race to
/// <see cref="AppointmentStatusConflictException"/> (FR-BKG-08, NFR-REL-03).
/// </summary>
public sealed class AppointmentStatusStore : IAppointmentStatusStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="AppointmentStatusStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public AppointmentStatusStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<CheckInContext?> LoadForCheckInAsync(
        int appointmentId,
        Guid staffUserId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _context.Appointments
            .Include(appointment => appointment.Doctor)
            .Include(appointment => appointment.DoctorSlot)
            .SingleOrDefaultAsync(
                appointment => appointment.Id == appointmentId,
                cancellationToken);

        if (appointment is null)
        {
            return null;
        }

        var staff = await _context.ClinicStaff
            .SingleOrDefaultAsync(staff => staff.UserId == staffUserId, cancellationToken);

        var patient = await _context.Users
            .SingleOrDefaultAsync(user => user.Id == appointment.PatientId, cancellationToken);

        return new CheckInContext(appointment, appointment.Doctor, staff, patient);
    }

    /// <inheritdoc />
    public async Task<CompleteContext?> LoadForCompleteAsync(
        int appointmentId,
        Guid doctorUserId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _context.Appointments
            .SingleOrDefaultAsync(
                appointment => appointment.Id == appointmentId,
                cancellationToken);

        if (appointment is null)
        {
            return null;
        }

        var doctor = await _context.Doctors
            .SingleOrDefaultAsync(doctor => doctor.UserId == doctorUserId, cancellationToken);

        return new CompleteContext(appointment, doctor);
    }

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Appointment.Status is a concurrency token: another request checked in or completed the same row first.
            throw new AppointmentStatusConflictException(
                "Another request changed the appointment status first.",
                exception);
        }
    }
}
