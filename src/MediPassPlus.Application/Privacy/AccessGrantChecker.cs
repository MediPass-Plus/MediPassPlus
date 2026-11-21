using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Privacy;

/// <summary>
/// Database-backed implementation of the medical access gate. It resolves the real doctor and patient from the
/// appointment (FR-ACS-06), loads the appointment's grant, and applies the lifecycle rules of ADR-0011: a grant is
/// active while <c>now &lt; EndedAtUtc ?? ExpiresAtUtc</c>, never past <c>ExpiresAtUtc</c>, and never past
/// <c>GrantedAtUtc + 2 hours</c> (FR-ACS-07, UC-24).
/// </summary>
public sealed class AccessGrantChecker : IAccessGrantChecker
{
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(2);

    private readonly IVisitAccessScopeService _scopeService;
    private readonly IAccessGrantStore _store;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AccessGrantChecker"/> class.</summary>
    /// <param name="scopeService">Resolves the appointment's doctor and patient and verifies the acting doctor.</param>
    /// <param name="store">Loads the grant for the verified scope.</param>
    /// <param name="timeProvider">The clock used to derive the grant status.</param>
    public AccessGrantChecker(
        IVisitAccessScopeService scopeService,
        IAccessGrantStore store,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeService);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _scopeService = scopeService;
        _store = store;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The checks run in this order, and the first refusal wins:
    /// <list type="number">
    /// <item>the authenticated doctor owns the appointment (the patient is taken from the appointment);</item>
    /// <item>a grant exists for the exact appointment, doctor and patient scope;</item>
    /// <item>the stored grant still matches that scope;</item>
    /// <item>the stored <see cref="AccessGrant.ExpiresAtUtc"/> has not been reached;</item>
    /// <item>the absolute two-hour cap has not been reached;</item>
    /// <item>the grant lifecycle still reports <see cref="AccessGrantStatus.Active"/>.</item>
    /// </list>
    /// </remarks>
    public async Task<AccessGrantCheckResult> RequireActiveGrantAsync(
        Guid doctorUserId,
        int appointmentId,
        CancellationToken cancellationToken = default)
    {
        var scopeResult = await _scopeService.ResolveForDoctorAsync(
            appointmentId,
            doctorUserId,
            cancellationToken);

        if (!scopeResult.IsAllowed)
        {
            return AccessGrantCheckResult.Denied(MapScopeFailure(scopeResult.Failure));
        }

        var scope = scopeResult.Scope
            ?? throw new InvalidOperationException("An allowed scope result must contain a scope.");

        var grant = await _store.FindGrantAsync(
            scope.AppointmentId,
            scope.DoctorId,
            scope.PatientId,
            cancellationToken);

        if (grant is null)
        {
            return AccessGrantCheckResult.Denied(AccessGrantCheckFailure.GrantNotFound);
        }

        // The database enforces the scope through a composite key (ADR-0011 decision 8). Re-check it here so a grant
        // can never authorize another doctor or patient even if a store or a row is wrong.
        if (grant.AppointmentId != scope.AppointmentId
            || grant.DoctorId != scope.DoctorId
            || grant.PatientId != scope.PatientId)
        {
            return AccessGrantCheckResult.Denied(AccessGrantCheckFailure.ScopeMismatch);
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        var failure = EvaluateLifetime(grant, utcNow);

        return failure is null
            ? AccessGrantCheckResult.Allowed(grant)
            : AccessGrantCheckResult.Denied(failure.Value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A report route carries a record, not an appointment, so the grant is resolved from the patient instead: the store
    /// returns every grant this doctor holds for that patient, and each one is evaluated with the same ADR-0011 rules as
    /// the appointment-scoped check. The newest active grant authorizes the request. The stored patient is re-verified
    /// here and the doctor binding is enforced by the store's join on the doctor's user account, so another patient's
    /// grant authorizes nothing (ADR-0015 decision 2).
    /// </remarks>
    public async Task<AccessGrantCheckResult> RequireActiveGrantForPatientAsync(
        Guid doctorUserId,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        if (doctorUserId == Guid.Empty)
        {
            throw new ArgumentException("A doctor user identifier is required.", nameof(doctorUserId));
        }

        if (patientUserId == Guid.Empty)
        {
            throw new ArgumentException("A patient user identifier is required.", nameof(patientUserId));
        }

        var grants = await _store.FindGrantsForDoctorUserAsync(doctorUserId, patientUserId, cancellationToken);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        AccessGrantCheckFailure? refusal = null;

        foreach (var grant in grants)
        {
            if (grant.PatientId != patientUserId)
            {
                refusal ??= AccessGrantCheckFailure.ScopeMismatch;
                continue;
            }

            var failure = EvaluateLifetime(grant, utcNow);

            if (failure is null)
            {
                return AccessGrantCheckResult.Allowed(grant);
            }

            refusal ??= failure;
        }

        return AccessGrantCheckResult.Denied(refusal ?? AccessGrantCheckFailure.GrantNotFound);
    }

    /// <summary>
    /// Applies the ADR-0011 lifecycle to one stored grant and returns the refusal reason, or null when it is still
    /// active. Both bounds are absolute: the stored expiry and the two-hour cap, so a corrupted timestamp can never
    /// extend access (FR-ACS-07, UC-24).
    /// </summary>
    private static AccessGrantCheckFailure? EvaluateLifetime(AccessGrant grant, DateTime utcNow)
    {
        if (utcNow >= grant.ExpiresAtUtc)
        {
            return AccessGrantCheckFailure.GrantExpired;
        }

        if (utcNow >= grant.GrantedAtUtc.Add(MaximumLifetime))
        {
            return AccessGrantCheckFailure.GrantExpired;
        }

        if (!grant.IsActiveAt(utcNow))
        {
            return grant.StatusAt(utcNow) == AccessGrantStatus.Ended
                ? AccessGrantCheckFailure.GrantEnded
                : AccessGrantCheckFailure.GrantExpired;
        }

        return null;
    }

    private static AccessGrantCheckFailure MapScopeFailure(VisitAccessScopeFailure failure) =>
        failure switch
        {
            VisitAccessScopeFailure.AppointmentNotFound => AccessGrantCheckFailure.AppointmentNotFound,
            VisitAccessScopeFailure.DoctorNotFound => AccessGrantCheckFailure.DoctorNotFound,
            VisitAccessScopeFailure.DoctorMismatch => AccessGrantCheckFailure.DoctorMismatch,
            VisitAccessScopeFailure.PatientMismatch => AccessGrantCheckFailure.ScopeMismatch,
            VisitAccessScopeFailure.None => throw new InvalidOperationException(
                "A refused scope result must have a failure reason."),
            _ => throw new InvalidOperationException($"Unknown scope failure: {failure}.")
        };
}
