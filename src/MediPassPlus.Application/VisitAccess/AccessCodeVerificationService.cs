using MediPassPlus.Application.Audit;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Verifies the patient's six-digit fallback for the authenticated doctor. The doctor must own the appointment, the
/// visit must be checked in or in visit, and a live code must exist. A mismatch records a failed attempt and locks the
/// code on the fifth one (FR-ACS-05). A match is the patient's approval recorded out of band: the request is approved,
/// the code is consumed, every other live credential is revoked, the appointment moves to
/// <see cref="AppointmentStatus.InVisit"/>, the two-hour grant is created, and an <c>AccessGrantCreated</c> audit row
/// is staged in the same unit of work (ADR-0004, ADR-0011 amendment 2026-11-12).
/// </summary>
public sealed class AccessCodeVerificationService : IAccessCodeVerificationService
{
    private readonly IVisitAccessScopeService _scopeService;
    private readonly IAccessCodeStore _store;
    private readonly IAccessCodeHasher _hasher;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AccessCodeVerificationService"/> class.</summary>
    /// <param name="scopeService">Verifies that the authenticated doctor owns the appointment.</param>
    /// <param name="store">The unit of work that loads and stages the verification state.</param>
    /// <param name="hasher">Compares the presented code with the stored HMAC in fixed time.</param>
    /// <param name="auditWriter">Stages the grant-creation audit row in the same unit of work.</param>
    /// <param name="timeProvider">The clock used for the attempt, grant and status timestamps.</param>
    public AccessCodeVerificationService(
        IVisitAccessScopeService scopeService,
        IAccessCodeStore store,
        IAccessCodeHasher hasher,
        IAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeService);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _scopeService = scopeService;
        _store = store;
        _hasher = hasher;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<AccessCodeVerificationResult> VerifyAsync(
        int appointmentId,
        Guid doctorUserId,
        string code,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        var scopeResult = await _scopeService.ResolveForDoctorAsync(
            appointmentId,
            doctorUserId,
            cancellationToken);

        if (!scopeResult.IsAllowed)
        {
            return AccessCodeVerificationResult.Denied(MapScopeFailure(scopeResult.Failure));
        }

        var scope = scopeResult.Scope
            ?? throw new InvalidOperationException("An allowed scope result must contain a scope.");

        var context = await _store.LoadAsync(
            scope.AppointmentId,
            scope.DoctorId,
            scope.PatientId,
            cancellationToken);

        if (context is null)
        {
            return AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.AppointmentNotFound);
        }

        if (context.Appointment.Status is not (AppointmentStatus.CheckedIn or AppointmentStatus.InVisit))
        {
            return AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.AppointmentNotCheckedIn);
        }

        if (context.Requests.Any(request => request.Status == AccessRequestStatus.Approved))
        {
            return AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.AccessAlreadyGranted);
        }

        var request = context.Requests
            .Where(candidate => candidate.Status == AccessRequestStatus.Pending)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .FirstOrDefault();

        if (request is null)
        {
            return AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.RequestNotPending);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var credential = context.Credentials
            .Where(candidate => candidate.AccessRequestId == request.Id
                && candidate.Type == VisitCredentialType.NumericCode)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .FirstOrDefault();

        if (credential is null)
        {
            return AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.CodeUnavailable);
        }

        if (credential.LockedAtUtc is not null)
        {
            return AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.CodeLocked);
        }

        if (!credential.IsUsableAt(now))
        {
            return AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.CodeUnavailable);
        }

        if (!_hasher.Verify(code, credential.SecretHash))
        {
            credential.RegisterFailedAttempt(now);

            try
            {
                await _store.SaveChangesAsync(cancellationToken);
            }
            catch (AccessCodeConflictException)
            {
                return AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.RequestConflict);
            }

            return AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.InvalidCode);
        }

        if (context.Appointment.Status == AppointmentStatus.CheckedIn
            && !context.Appointment.TryMarkInVisit(now))
        {
            return AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.AppointmentNotCheckedIn);
        }

        request.Approve();
        credential.Consume(now);
        RevokeOtherLiveCredentials(context.Credentials, credential, now);

        var grant = AccessGrant.Create(request, context.Appointment, now);
        _store.AddGrant(grant);

        // The grant is an access-control event; stage the audit row so it commits with the grant, the request
        // status and the credential changes (ADR-0004 decision 7, ADR-0011 decision 8).
        _auditWriter.Add(new AuditEntry(
            doctorUserId,
            request.PatientId,
            AuditAction.AccessGrantCreated,
            AuditEntityType.AccessGrant,
            grant.Id.ToString(),
            ipAddress,
            grant.Id));

        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (AccessCodeConflictException)
        {
            return AccessCodeVerificationResult.Denied(AccessCodeVerificationFailure.RequestConflict);
        }

        return AccessCodeVerificationResult.Granted(new AccessGrantResponse(
            grant.Id,
            request.Id,
            grant.AppointmentId,
            grant.DoctorId,
            grant.GrantedAtUtc,
            grant.ExpiresAtUtc));
    }

    private static void RevokeOtherLiveCredentials(
        IReadOnlyList<VisitAccessCredential> credentials,
        VisitAccessCredential consumed,
        DateTime utcNow)
    {
        foreach (var credential in credentials)
        {
            if (credential.Id != consumed.Id
                && credential.ConsumedAtUtc is null
                && credential.RevokedAtUtc is null)
            {
                credential.Revoke(utcNow);
            }
        }
    }

    private static AccessCodeVerificationFailure MapScopeFailure(VisitAccessScopeFailure failure) =>
        failure switch
        {
            VisitAccessScopeFailure.AppointmentNotFound => AccessCodeVerificationFailure.AppointmentNotFound,
            VisitAccessScopeFailure.DoctorNotFound => AccessCodeVerificationFailure.DoctorNotFound,
            VisitAccessScopeFailure.DoctorMismatch => AccessCodeVerificationFailure.DoctorMismatch,
            VisitAccessScopeFailure.PatientMismatch => AccessCodeVerificationFailure.DoctorMismatch,
            VisitAccessScopeFailure.None => throw new InvalidOperationException(
                "A refused scope result must have a failure reason."),
            _ => throw new InvalidOperationException($"Unknown scope failure: {failure}.")
        };
}
