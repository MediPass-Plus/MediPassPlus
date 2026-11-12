using MediPassPlus.Application.Audit;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Ends a live access grant immediately at the patient's request (FR-ACS-09, UC-19). The grant's appointment scope is
/// resolved from the database, never from caller-supplied identifiers (FR-ACS-06, ADR-0011 decision 8), and the end
/// commits together with its <c>AccessEnded</c> audit row (ADR-0004 decision 7).
/// </summary>
public sealed class AccessGrantEndService : IAccessGrantEndService
{
    private readonly IVisitAccessScopeService _scopeService;
    private readonly IAccessGrantEndStore _store;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AccessGrantEndService"/> class.</summary>
    /// <param name="scopeService">Verifies that the authenticated patient owns the grant's appointment.</param>
    /// <param name="store">The unit of work that loads and stages the grant end.</param>
    /// <param name="auditWriter">Stages the <c>AccessEnded</c> row in the same unit of work.</param>
    /// <param name="timeProvider">The clock used for the end timestamp.</param>
    public AccessGrantEndService(
        IVisitAccessScopeService scopeService,
        IAccessGrantEndStore store,
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
    public async Task<AccessGrantEndResult> EndAsync(
        Guid grantId,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        var grant = await _store.FindGrantAsync(grantId, cancellationToken);
        if (grant is null)
        {
            return AccessGrantEndResult.Denied(AccessGrantEndFailure.GrantNotFound);
        }

        // The appointment id comes from the stored grant. The authenticated patient is checked against the
        // appointment itself, so a caller-supplied grant id can never choose another patient's scope (FR-ACS-06).
        var scopeResult = await _scopeService.ResolveForPatientAsync(
            grant.AppointmentId,
            patientUserId,
            cancellationToken);

        if (!scopeResult.IsAllowed)
        {
            return AccessGrantEndResult.Denied(MapScopeFailure(scopeResult.Failure));
        }

        var scope = scopeResult.Scope
            ?? throw new InvalidOperationException("An allowed scope result must contain a scope.");

        var tracked = await _store.LoadAsync(grantId, scope, cancellationToken);
        if (tracked is null)
        {
            return AccessGrantEndResult.Denied(AccessGrantEndFailure.GrantNotFound);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!tracked.IsActiveAt(now))
        {
            // Ended and expired are terminal; an ended grant is never reused (FR-ACS-11, ADR-0011 decision 5).
            return AccessGrantEndResult.Denied(AccessGrantEndFailure.GrantNotActive);
        }

        tracked.EndByPatient(now);

        // The grant is an access-control event; stage the audit row so it commits with the end (ADR-0004 decision 7).
        _auditWriter.Add(new AuditEntry(
            patientUserId,
            tracked.PatientId,
            AuditAction.AccessEnded,
            AuditEntityType.AccessGrant,
            tracked.Id.ToString(),
            ipAddress,
            tracked.Id));

        await _store.SaveChangesAsync(cancellationToken);

        return AccessGrantEndResult.Ended(new AccessGrantEndedResponse(
            tracked.Id,
            tracked.EndedAtUtc
                ?? throw new InvalidOperationException("An ended grant must have an end timestamp."),
            tracked.EndReason?.ToString()
                ?? throw new InvalidOperationException("An ended grant must have an end reason.")));
    }

    private static AccessGrantEndFailure MapScopeFailure(VisitAccessScopeFailure failure) =>
        failure switch
        {
            VisitAccessScopeFailure.AppointmentNotFound => AccessGrantEndFailure.GrantNotFound,
            VisitAccessScopeFailure.PatientMismatch => AccessGrantEndFailure.PatientMismatch,
            VisitAccessScopeFailure.DoctorNotFound or VisitAccessScopeFailure.DoctorMismatch =>
                AccessGrantEndFailure.PatientMismatch,
            VisitAccessScopeFailure.None => throw new InvalidOperationException(
                "A refused scope result must have a failure reason."),
            _ => throw new InvalidOperationException($"Unknown scope failure: {failure}.")
        };
}
