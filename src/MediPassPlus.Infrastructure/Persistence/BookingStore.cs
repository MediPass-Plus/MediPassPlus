using MediPassPlus.Application.Booking;
using MediPassPlus.Domain.Clinical;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>
/// The EF Core unit of work for the booking rules. It loads the slot or appointment inside the authenticated
/// patient's scope and commits the appointment and slot changes together. A filtered unique index on
/// <c>Appointments(DoctorSlotId)</c> is the authoritative double-booking guard, and the slot and appointment
/// concurrency tokens turn a lost race into <see cref="BookingConflictException"/> (FR-BKG-03, NFR-REL-03).
/// </summary>
public sealed class BookingStore : IBookingStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="BookingStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public BookingStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<BookingSlotContext?> LoadBookingSlotAsync(
        int slotId,
        int doctorId,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        var slot = await _context.DoctorSlots
            .SingleOrDefaultAsync(slot => slot.Id == slotId, cancellationToken);

        if (slot is null)
        {
            return null;
        }

        var doctor = await _context.Doctors
            .SingleOrDefaultAsync(doctor => doctor.Id == doctorId, cancellationToken);

        var doctorUser = doctor is null
            ? null
            : await _context.Users
                .SingleOrDefaultAsync(user => user.Id == doctor.UserId, cancellationToken);

        var patientUser = await _context.Users
            .SingleOrDefaultAsync(user => user.Id == patientUserId, cancellationToken);

        var hasActiveAppointment = await HasActiveAppointmentAsync(slotId, cancellationToken);

        return new BookingSlotContext(slot, doctor, doctorUser, patientUser, hasActiveAppointment);
    }

    /// <inheritdoc />
    public async Task<BookingAppointmentContext?> LoadAppointmentAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        // The patient id is part of the query, so another patient's appointment is indistinguishable from a
        // missing one and the API can answer 404 without leaking that the appointment exists.
        var appointment = await _context.Appointments
            .Include(appointment => appointment.DoctorSlot)
            .SingleOrDefaultAsync(
                appointment => appointment.Id == appointmentId
                    && appointment.PatientId == patientUserId,
                cancellationToken);

        if (appointment is null)
        {
            return null;
        }

        var doctor = await _context.Doctors
            .SingleOrDefaultAsync(doctor => doctor.Id == appointment.DoctorId, cancellationToken);

        var doctorUser = doctor is null
            ? null
            : await _context.Users
                .SingleOrDefaultAsync(user => user.Id == doctor.UserId, cancellationToken);

        var patientUser = await _context.Users
            .SingleOrDefaultAsync(user => user.Id == patientUserId, cancellationToken);

        return new BookingAppointmentContext(appointment, doctor, doctorUser, patientUser);
    }

    /// <inheritdoc />
    public async Task<TargetSlotContext?> LoadTargetSlotAsync(
        int slotId,
        CancellationToken cancellationToken = default)
    {
        var slot = await _context.DoctorSlots
            .SingleOrDefaultAsync(slot => slot.Id == slotId, cancellationToken);

        if (slot is null)
        {
            return null;
        }

        var hasActiveAppointment = await HasActiveAppointmentAsync(slotId, cancellationToken);

        return new TargetSlotContext(slot, hasActiveAppointment);
    }

    /// <inheritdoc />
    public async Task<PatientAppointmentsPage> ListAppointmentsAsync(
        Guid patientUserId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        // The patient id is part of the query, so the list can never widen to another patient's rows (FR-BKG-06).
        var query =
            from appointment in _context.Appointments.AsNoTracking()
            join slot in _context.DoctorSlots.AsNoTracking()
                on appointment.DoctorSlotId equals slot.Id
            join doctor in _context.Doctors.AsNoTracking()
                on appointment.DoctorId equals doctor.Id
            join doctorUser in _context.Users.AsNoTracking()
                on doctor.UserId equals doctorUser.Id
            join clinic in _context.Clinics.AsNoTracking()
                on doctor.ClinicId equals clinic.Id
            where appointment.PatientId == patientUserId
            select new
            {
                Appointment = appointment,
                Slot = slot,
                Doctor = doctor,
                DoctorUser = doctorUser,
                Clinic = clinic
            };

        var totalCount = await query.CountAsync(cancellationToken);

        // The ordering is applied to the joined columns before the row record is projected: ordering by a member of
        // a record projection is not translated by EF Core.
        var items = await query
            .OrderByDescending(row => row.Slot.StartTimeUtc)
            .ThenByDescending(row => row.Appointment.Id)
            .Skip(skip)
            .Take(take)
            .Select(row => new PatientAppointmentRow(
                row.Appointment.Id,
                row.Doctor.Id,
                row.DoctorUser.FullName,
                row.Clinic.Id,
                row.Clinic.Name,
                row.Slot.Id,
                row.Slot.StartTimeUtc,
                row.Slot.EndTimeUtc,
                row.Appointment.Status,
                row.Appointment.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PatientAppointmentsPage(items, totalCount);
    }

    /// <inheritdoc />
    public void AddAppointment(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        _context.Appointments.Add(appointment);
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
            // Appointment.Status/DoctorSlotId and DoctorSlot.IsAvailable are concurrency tokens: another request
            // booked, cancelled or rescheduled the same row first.
            throw new BookingConflictException(
                "Another request changed the appointment or slot state first.",
                exception);
        }
        catch (DbUpdateException exception) when (IsSlotConflict(exception))
        {
            throw new BookingConflictException(
                "This slot already has an active appointment.",
                exception);
        }
    }

    private Task<bool> HasActiveAppointmentAsync(int slotId, CancellationToken cancellationToken) =>
        _context.Appointments
            .AnyAsync(
                appointment => appointment.DoctorSlotId == slotId
                    && appointment.Status != AppointmentStatus.Cancelled,
                cancellationToken);

    private static bool IsSlotConflict(DbUpdateException exception)
    {
        var message = exception.GetBaseException().Message;

        // SQL Server reports the filtered index by name; SQLite reports the column. Checking both keeps the
        // mapping provider-independent without referencing a provider-specific exception type.
        var isUniqueViolation =
            message.Contains("IX_Appointments_DoctorSlotId_Active", StringComparison.Ordinal)
            || message.Contains(
                "UNIQUE constraint failed: Appointments.DoctorSlotId",
                StringComparison.Ordinal);

        return isUniqueViolation
            && exception.Entries.Any(entry => entry.Entity is Appointment);
    }
}
