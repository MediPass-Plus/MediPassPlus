using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>The EF Core implementation of the read-only visit-access scope lookups.</summary>
public sealed class VisitAccessScopeStore : IVisitAccessScopeStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="VisitAccessScopeStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public VisitAccessScopeStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<Appointment?> FindAppointmentAsync(int appointmentId, CancellationToken cancellationToken) =>
        _context.Appointments
            .AsNoTracking()
            .SingleOrDefaultAsync(appointment => appointment.Id == appointmentId, cancellationToken);

    /// <inheritdoc />
    public Task<Doctor?> FindDoctorByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        _context.Doctors
            .AsNoTracking()
            .SingleOrDefaultAsync(doctor => doctor.UserId == userId, cancellationToken);
}
