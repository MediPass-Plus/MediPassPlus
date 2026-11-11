using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>The EF Core unit of work for generating and verifying the six-digit fallback.</summary>
public sealed class AccessCodeStore : IAccessCodeStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="AccessCodeStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public AccessCodeStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<AccessCodeContext?> LoadAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _context.Appointments
            .Include(candidate => candidate.DoctorSlot)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == appointmentId
                    && candidate.DoctorId == doctorId
                    && candidate.PatientId == patientId,
                cancellationToken);

        if (appointment is null)
        {
            return null;
        }

        var requests = await _context.AccessRequests
            .Where(request => request.AppointmentId == appointmentId)
            .OrderByDescending(request => request.CreatedAtUtc)
            .ThenByDescending(request => request.Id)
            .ToListAsync(cancellationToken);

        var requestIds = requests.Select(request => request.Id).ToArray();

        List<VisitAccessCredential> credentials;
        if (requestIds.Length == 0)
        {
            credentials = [];
        }
        else
        {
            credentials = await _context.VisitAccessCredentials
                .Where(credential => requestIds.Contains(credential.AccessRequestId))
                .ToListAsync(cancellationToken);
        }

        return new AccessCodeContext(appointment, requests, credentials);
    }

    /// <inheritdoc />
    public void AddCredential(VisitAccessCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _context.VisitAccessCredentials.Add(credential);
    }

    /// <inheritdoc />
    public async Task<AccessCodeFailedAttemptOutcome> RegisterFailedAttemptAsync(
        Guid credentialId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        // One conditional UPDATE keeps the attempt counter authoritative. Concurrent entries serialize on the row
        // and each one re-reads the committed value, so no increment is lost and the fifth entry always sets the lock
        // even when several wrong codes arrive at once (FR-ACS-05).
        var updated = await _context.VisitAccessCredentials
            .Where(credential => credential.Id == credentialId
                && credential.Type == VisitCredentialType.NumericCode
                && credential.ConsumedAtUtc == null
                && credential.RevokedAtUtc == null
                && credential.LockedAtUtc == null
                && credential.FailedAttempts < VisitAccessCredential.MaxFailedAttempts
                && credential.ExpiresAtUtc > utcNow)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        credential => credential.FailedAttempts,
                        credential => credential.FailedAttempts + 1)
                    .SetProperty(
                        credential => credential.LockedAtUtc,
                        credential => credential.FailedAttempts + 1 >= VisitAccessCredential.MaxFailedAttempts
                            ? utcNow
                            : credential.LockedAtUtc),
                cancellationToken);

        if (updated > 0)
        {
            return AccessCodeFailedAttemptOutcome.Recorded;
        }

        var state = await _context.VisitAccessCredentials
            .AsNoTracking()
            .Where(credential => credential.Id == credentialId)
            .Select(credential => new
            {
                credential.FailedAttempts,
                credential.LockedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (state is null)
        {
            return AccessCodeFailedAttemptOutcome.Unavailable;
        }

        return state.LockedAtUtc is not null
            || state.FailedAttempts >= VisitAccessCredential.MaxFailedAttempts
            ? AccessCodeFailedAttemptOutcome.AlreadyLocked
            : AccessCodeFailedAttemptOutcome.Unavailable;
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
            // Appointment.Status and AccessRequest.Status are concurrency tokens: another call changed the state
            // first, so the caller must reload instead of overwriting the newer state.
            throw new AccessCodeConflictException(
                "Another request changed the visit state first.",
                exception);
        }
        catch (DbUpdateException exception) when (IsAccessCodeConflict(exception))
        {
            throw new AccessCodeConflictException(
                "Another access-code request for this visit won the race.",
                exception);
        }
    }

    private static bool IsAccessCodeConflict(DbUpdateException exception)
    {
        var message = exception.GetBaseException().Message;

        return IsGrantConflict(exception, message)
            || IsLiveCredentialConflict(exception, message);
    }

    private static bool IsGrantConflict(DbUpdateException exception, string message)
    {
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

    private static bool IsLiveCredentialConflict(DbUpdateException exception, string message)
    {
        var isUniqueViolation =
            message.Contains(
                "IX_VisitAccessCredentials_AccessRequestId_Type",
                StringComparison.Ordinal)
            || message.Contains(
                "UNIQUE constraint failed: VisitAccessCredentials.AccessRequestId",
                StringComparison.Ordinal);

        return isUniqueViolation
            && exception.Entries.Any(entry => entry.Entity is VisitAccessCredential);
    }
}
