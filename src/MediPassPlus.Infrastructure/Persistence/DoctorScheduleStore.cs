using MediPassPlus.Application.DoctorSchedule;
using MediPassPlus.Domain.Clinical;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>
/// The EF Core read store for a doctor's daily schedule. The doctor is resolved from the authenticated user account,
/// and the query reads only the patient name the doctor needs (FR-BKG-07, FR-STF-04).
/// </summary>
public sealed class DoctorScheduleStore : IDoctorScheduleStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="DoctorScheduleStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public DoctorScheduleStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<Doctor?> FindDoctorByUserIdAsync(
        Guid doctorUserId,
        CancellationToken cancellationToken = default) =>
        _context.Doctors
            .AsNoTracking()
            .SingleOrDefaultAsync(doctor => doctor.UserId == doctorUserId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DoctorScheduleRow>> ListAsync(
        int doctorId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from appointment in _context.Appointments.AsNoTracking()
            join slot in _context.DoctorSlots.AsNoTracking()
                on appointment.DoctorSlotId equals slot.Id
            join patient in _context.Users.AsNoTracking()
                on appointment.PatientId equals patient.Id
            where appointment.DoctorId == doctorId
                && appointment.Status != AppointmentStatus.Cancelled
                && slot.StartTimeUtc >= fromUtc
                && slot.StartTimeUtc < toUtc
            orderby slot.StartTimeUtc, appointment.Id
            select new DoctorScheduleRow(
                appointment.Id,
                slot.StartTimeUtc,
                slot.EndTimeUtc,
                patient.FullName,
                appointment.Status))
            .ToListAsync(cancellationToken);

        return rows;
    }
}
