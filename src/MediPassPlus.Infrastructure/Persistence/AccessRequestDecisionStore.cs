using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>The EF Core unit of work for approving or rejecting an access request.</summary>
public sealed class AccessRequestDecisionStore : IAccessRequestDecisionStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="AccessRequestDecisionStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public AccessRequestDecisionStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<AccessRequest?> FindRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default) =>
        _context.AccessRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(request => request.Id == requestId, cancellationToken);

    /// <inheritdoc />
    public async Task<AccessRequestDecisionContext?> LoadAsync(
        Guid requestId,
        VisitAccessScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var request = await _context.AccessRequests
            .SingleOrDefaultAsync(
                request => request.Id == requestId
                    && request.AppointmentId == scope.AppointmentId
                    && request.DoctorId == scope.DoctorId
                    && request.PatientId == scope.PatientId,
                cancellationToken);

        if (request is null)
        {
            return null;
        }

        var appointment = await _context.Appointments
            .SingleOrDefaultAsync(
                appointment => appointment.Id == scope.AppointmentId
                    && appointment.DoctorId == scope.DoctorId
                    && appointment.PatientId == scope.PatientId,
                cancellationToken);

        if (appointment is null)
        {
            return null;
        }

        var credentials = await _context.VisitAccessCredentials
            .Where(credential => credential.AccessRequestId == requestId)
            .ToListAsync(cancellationToken);

        return new AccessRequestDecisionContext(request, appointment, credentials);
    }

    /// <inheritdoc />
    public async Task<AccessRequestTokenContext?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        // The QR hash index makes this a single-row lookup. The type filter keeps a numeric-code HMAC from ever
        // matching a token path (ADR-0001, ADR-0002).
        var credential = await _context.VisitAccessCredentials
            .SingleOrDefaultAsync(
                credential => credential.SecretHash == tokenHash
                    && credential.Type == VisitCredentialType.QrToken,
                cancellationToken);

        if (credential is null)
        {
            return null;
        }

        var request = await _context.AccessRequests
            .SingleOrDefaultAsync(
                request => request.Id == credential.AccessRequestId,
                cancellationToken);

        if (request is null)
        {
            return null;
        }

        var appointment = await _context.Appointments
            .SingleOrDefaultAsync(
                appointment => appointment.Id == request.AppointmentId,
                cancellationToken);

        if (appointment is null)
        {
            return null;
        }

        var credentials = await _context.VisitAccessCredentials
            .Where(item => item.AccessRequestId == request.Id)
            .ToListAsync(cancellationToken);

        return new AccessRequestTokenContext(
            credential,
            new AccessRequestDecisionContext(request, appointment, credentials));
    }

    /// <inheritdoc />
    public void AddGrant(AccessGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        _context.AccessGrants.Add(grant);
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
            // AccessRequest.Status is a concurrency token: a concurrent approve/reject answered the request first.
            throw new AccessRequestDecisionConflictException(
                "Another decision answered this access request first.",
                exception);
        }
        catch (DbUpdateException exception) when (IsGrantConflict(exception))
        {
            throw new AccessRequestDecisionConflictException(
                "This appointment already has an access grant.",
                exception);
        }
    }

    private static bool IsGrantConflict(DbUpdateException exception)
    {
        var message = exception.GetBaseException().Message;

        // SQL Server reports the unique index by name; SQLite reports the column pair. Checking both keeps the
        // mapping provider-independent without referencing a provider-specific exception type.
        var isUniqueViolation =
            message.Contains("IX_AccessGrants_AccessRequestId", StringComparison.Ordinal)
            || message.Contains("IX_AccessGrants_AppointmentId", StringComparison.Ordinal)
            || message.Contains(
                "UNIQUE constraint failed: AccessGrants.AccessRequestId",
                StringComparison.Ordinal)
            || message.Contains(
                "UNIQUE constraint failed: AccessGrants.AppointmentId",
                StringComparison.Ordinal);

        return isUniqueViolation
            && exception.Entries.Any(entry => entry.Entity is AccessGrant);
    }
}
