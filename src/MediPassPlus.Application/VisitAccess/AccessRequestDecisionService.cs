using MediPassPlus.Application.Audit;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Answers a pending access request: verifies that the authenticated patient owns the appointment, consumes the
/// request's credentials, and either creates the access grant (approval) or records the rejection. The request,
/// credential and audit changes commit in one unit of work (FR-ACS-03, FR-ACS-04, FR-ACS-06, FR-PRV-02,
/// ADR-0004, ADR-0011). The same decision can be reached from the request identifier or from the scanned QR token;
/// the token path never reveals whether an unknown token exists and never echoes the token (MP-97).
/// </summary>
public sealed class AccessRequestDecisionService : IAccessRequestDecisionService
{
    private readonly IVisitAccessScopeService _scopeService;
    private readonly IAccessRequestDecisionStore _store;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AccessRequestDecisionService"/> class.</summary>
    /// <param name="scopeService">Verifies that the authenticated patient owns the request's appointment.</param>
    /// <param name="store">The unit of work that loads and stages the decision state.</param>
    /// <param name="auditWriter">Stages the decision audit row in the same unit of work.</param>
    /// <param name="timeProvider">The clock used for the grant and credential timestamps.</param>
    public AccessRequestDecisionService(
        IVisitAccessScopeService scopeService,
        IAccessRequestDecisionStore store,
        IAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeService);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _scopeService = scopeService;
        _store = store;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<AccessRequestDecisionResult> ApproveAsync(
        Guid requestId,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        var (context, failure) = await LoadForDecisionAsync(requestId, patientUserId, cancellationToken);
        if (context is null)
        {
            return AccessRequestDecisionResult.Denied(failure);
        }

        // A completed or cancelled visit can no longer produce a grant, so a stale pending request is refused
        // instead of approving access that the visit cannot use (UC-12, MP-97).
        if (!IsVisitOpen(context.Appointment))
        {
            return AccessRequestDecisionResult.Denied(AccessRequestDecisionFailure.VisitNotOpen);
        }

        return await ApproveContextAsync(context, patientUserId, ipAddress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccessRequestDecisionResult> RejectAsync(
        Guid requestId,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        var (context, failure) = await LoadForDecisionAsync(requestId, patientUserId, cancellationToken);
        if (context is null)
        {
            return AccessRequestDecisionResult.Denied(failure);
        }

        return await RejectContextAsync(context, patientUserId, ipAddress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccessRequestDecisionResult> ApproveByTokenAsync(
        string token,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        var (context, failure) = await LoadForTokenDecisionAsync(token, patientUserId, cancellationToken);
        if (context is null)
        {
            return AccessRequestDecisionResult.Denied(failure);
        }

        if (!IsVisitOpen(context.Decision.Appointment))
        {
            return AccessRequestDecisionResult.Denied(AccessRequestDecisionFailure.VisitNotOpen);
        }

        return await ApproveContextAsync(
            context.Decision,
            patientUserId,
            ipAddress,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccessRequestDecisionResult> RejectByTokenAsync(
        string token,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        var (context, failure) = await LoadForTokenDecisionAsync(token, patientUserId, cancellationToken);
        if (context is null)
        {
            return AccessRequestDecisionResult.Denied(failure);
        }

        // Rejection creates no grant, so it stays available even after the visit closed. It only stops the
        // credential from being reused (FR-ACS-06).
        return await RejectContextAsync(
            context.Decision,
            patientUserId,
            ipAddress,
            cancellationToken);
    }

    private async Task<AccessRequestDecisionResult> ApproveContextAsync(
        AccessRequestDecisionContext context,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        ConsumeCredentials(context.Credentials, now);
        context.Request.Approve();

        var grant = AccessGrant.Create(context.Request, context.Appointment, now);
        _store.AddGrant(grant);

        // The grant is an access-control event; stage the audit row so it commits with the grant, the request
        // status and the credential changes (ADR-0004 decision 7, ADR-0011 decision 8).
        _auditWriter.Add(new AuditEntry(
            patientUserId,
            context.Request.PatientId,
            AuditAction.AccessGrantCreated,
            AuditEntityType.AccessGrant,
            grant.Id.ToString(),
            ipAddress,
            grant.Id));

        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (AccessRequestDecisionConflictException)
        {
            return AccessRequestDecisionResult.Denied(AccessRequestDecisionFailure.RequestConflict);
        }

        return AccessRequestDecisionResult.Approved(new AccessGrantResponse(
            grant.Id,
            context.Request.Id,
            grant.AppointmentId,
            grant.DoctorId,
            grant.GrantedAtUtc,
            grant.ExpiresAtUtc));
    }

    private async Task<AccessRequestDecisionResult> RejectContextAsync(
        AccessRequestDecisionContext context,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        RevokeCredentials(context.Credentials, now);
        context.Request.Reject();

        // Rejection creates no grant, but it is still an access-control event that the patient must be able to see.
        _auditWriter.Add(new AuditEntry(
            patientUserId,
            context.Request.PatientId,
            AuditAction.AccessRequestRejected,
            AuditEntityType.AccessRequest,
            context.Request.Id.ToString(),
            ipAddress));

        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (AccessRequestDecisionConflictException)
        {
            return AccessRequestDecisionResult.Denied(AccessRequestDecisionFailure.RequestConflict);
        }

        return AccessRequestDecisionResult.Rejected();
    }

    private async Task<(AccessRequestDecisionContext? Context, AccessRequestDecisionFailure Failure)>
        LoadForDecisionAsync(Guid requestId, Guid patientUserId, CancellationToken cancellationToken)
    {
        var request = await _store.FindRequestAsync(requestId, cancellationToken);
        if (request is null)
        {
            return (null, AccessRequestDecisionFailure.RequestNotFound);
        }

        // The appointment id comes from the stored request. The authenticated patient is checked against the
        // appointment itself, so a caller-supplied id can never choose another patient's scope (FR-ACS-06).
        var scopeResult = await _scopeService.ResolveForPatientAsync(
            request.AppointmentId,
            patientUserId,
            cancellationToken);

        if (!scopeResult.IsAllowed)
        {
            return (null, MapScopeFailure(scopeResult.Failure));
        }

        var scope = scopeResult.Scope
            ?? throw new InvalidOperationException("An allowed scope result must contain a scope.");

        var context = await _store.LoadAsync(requestId, scope, cancellationToken);
        if (context is null)
        {
            return (null, AccessRequestDecisionFailure.RequestNotFound);
        }

        if (context.Request.Status != AccessRequestStatus.Pending)
        {
            return (null, AccessRequestDecisionFailure.AlreadyAnswered);
        }

        return (context, AccessRequestDecisionFailure.None);
    }

    private async Task<(AccessRequestTokenContext? Context, AccessRequestDecisionFailure Failure)>
        LoadForTokenDecisionAsync(
            string token,
            Guid patientUserId,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var context = await _store.FindByTokenHashAsync(QrTokenGenerator.Hash(token), cancellationToken);
        if (context is null)
        {
            return (null, AccessRequestDecisionFailure.TokenNotFound);
        }

        // An unknown token and another patient's token must answer identically; otherwise the response would
        // reveal that a token exists and who owns it (FR-ACS-06, NFR-PRI-01).
        if (context.Decision.Request.PatientId != patientUserId
            || context.Decision.Appointment.PatientId != patientUserId)
        {
            return (null, AccessRequestDecisionFailure.TokenNotFound);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!context.Credential.IsUsableAt(now))
        {
            return (null, AccessRequestDecisionFailure.TokenUnusable);
        }

        if (context.Decision.Request.Status != AccessRequestStatus.Pending)
        {
            return (null, AccessRequestDecisionFailure.TokenUnusable);
        }

        return (context, AccessRequestDecisionFailure.None);
    }

    private static bool IsVisitOpen(Appointment appointment) =>
        appointment.Status is AppointmentStatus.CheckedIn or AppointmentStatus.InVisit;

    private static AccessRequestDecisionFailure MapScopeFailure(VisitAccessScopeFailure failure) =>
        failure switch
        {
            VisitAccessScopeFailure.AppointmentNotFound => AccessRequestDecisionFailure.RequestNotFound,
            VisitAccessScopeFailure.PatientMismatch => AccessRequestDecisionFailure.PatientMismatch,
            VisitAccessScopeFailure.DoctorNotFound or VisitAccessScopeFailure.DoctorMismatch =>
                AccessRequestDecisionFailure.PatientMismatch,
            VisitAccessScopeFailure.None => throw new InvalidOperationException(
                "A refused scope result must have a failure reason."),
            _ => throw new InvalidOperationException($"Unknown scope failure: {failure}.")
        };

    private static void ConsumeCredentials(
        IReadOnlyList<VisitAccessCredential> credentials,
        DateTime utcNow)
    {
        foreach (var credential in credentials)
        {
            if (credential.IsUsableAt(utcNow))
            {
                credential.Consume(utcNow);
            }
            else if (credential.ConsumedAtUtc is null && credential.RevokedAtUtc is null)
            {
                // An expired or locked credential cannot prove this approval, but leaving it live would keep a
                // useless secret around after the request was answered.
                credential.Revoke(utcNow);
            }
        }
    }

    private static void RevokeCredentials(
        IReadOnlyList<VisitAccessCredential> credentials,
        DateTime utcNow)
    {
        foreach (var credential in credentials)
        {
            if (credential.ConsumedAtUtc is null && credential.RevokedAtUtc is null)
            {
                credential.Revoke(utcNow);
            }
        }
    }
}
