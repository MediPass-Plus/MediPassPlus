using MediPassPlus.Application.Privacy;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>The EF Core implementation of the read-only access-grant lookup.</summary>
public sealed class AccessGrantStore : IAccessGrantStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="AccessGrantStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public AccessGrantStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<AccessGrant?> FindGrantAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken) =>
        _context.AccessGrants
            .AsNoTracking()
            .SingleOrDefaultAsync(
                grant => grant.AppointmentId == appointmentId
                    && grant.DoctorId == doctorId
                    && grant.PatientId == patientId,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccessGrant>> FindGrantsForDoctorUserAsync(
        Guid doctorUserId,
        Guid patientUserId,
        CancellationToken cancellationToken)
    {
        // The join is the doctor binding: a grant stores a doctor profile, and that profile belongs to exactly one user
        // account, so a caller cannot ask for grants that belong to another doctor (ADR-0015 decision 2).
        var grants = await _context.AccessGrants
            .AsNoTracking()
            .Where(grant => grant.PatientId == patientUserId
                && _context.Doctors.Any(
                    doctor => doctor.Id == grant.DoctorId && doctor.UserId == doctorUserId))
            .OrderByDescending(grant => grant.GrantedAtUtc)
            .ToListAsync(cancellationToken);

        return grants;
    }
}
