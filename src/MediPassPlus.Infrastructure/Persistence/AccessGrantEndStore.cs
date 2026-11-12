using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>The EF Core unit of work for a patient's immediate end of an access grant.</summary>
public sealed class AccessGrantEndStore : IAccessGrantEndStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="AccessGrantEndStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public AccessGrantEndStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<AccessGrant?> FindGrantAsync(
        Guid grantId,
        CancellationToken cancellationToken = default) =>
        _context.AccessGrants
            .AsNoTracking()
            .SingleOrDefaultAsync(grant => grant.Id == grantId, cancellationToken);

    /// <inheritdoc />
    public Task<AccessGrant?> LoadAsync(
        Guid grantId,
        VisitAccessScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return _context.AccessGrants
            .SingleOrDefaultAsync(
                grant => grant.Id == grantId
                    && grant.AppointmentId == scope.AppointmentId
                    && grant.DoctorId == scope.DoctorId
                    && grant.PatientId == scope.PatientId,
                cancellationToken);
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
            // AccessGrant.EndedAtUtc is a concurrency token: the first end wins and a stale writer is refused, so a
            // completion can never undo a patient end (FR-ACS-09, FR-ACS-11, ADR-0011).
            throw new AccessGrantConflictException(
                "Another request ended the access grant first.",
                exception);
        }
    }
}
