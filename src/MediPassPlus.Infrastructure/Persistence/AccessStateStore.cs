using MediPassPlus.Application.VisitAccess;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>
/// The EF Core read store for one appointment's derived access state. The scope is re-checked against the stored
/// appointment, and the newest request and the appointment's single grant are read without tracking (MP-97).
/// </summary>
public sealed class AccessStateStore : IAccessStateStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="AccessStateStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public AccessStateStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<AccessStateContext?> LoadAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var isScoped = await _context.Appointments
            .AsNoTracking()
            .AnyAsync(
                appointment => appointment.Id == appointmentId
                    && appointment.DoctorId == doctorId
                    && appointment.PatientId == patientId,
                cancellationToken);

        if (!isScoped)
        {
            return null;
        }

        var request = await _context.AccessRequests
            .AsNoTracking()
            .Where(request => request.AppointmentId == appointmentId)
            .OrderByDescending(request => request.CreatedAtUtc)
            .ThenByDescending(request => request.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var grant = await _context.AccessGrants
            .AsNoTracking()
            .SingleOrDefaultAsync(
                grant => grant.AppointmentId == appointmentId,
                cancellationToken);

        return new AccessStateContext(request, grant);
    }
}
