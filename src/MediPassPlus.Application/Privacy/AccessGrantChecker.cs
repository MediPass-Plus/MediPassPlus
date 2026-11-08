using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Privacy;

/// <summary>
/// Database-backed implementation of the medical access gate. It resolves the real doctor and patient from the
/// appointment (FR-ACS-06), loads the appointment's grant, and applies the lifecycle rules of ADR-0011: a grant is
/// active while <c>now &lt; EndedAtUtc ?? ExpiresAtUtc</c>, and never past <c>GrantedAtUtc + 2 hours</c>
/// (FR-ACS-07, UC-24).
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

        // The two-hour cap is absolute (FR-ACS-07, UC-24) and must hold even if ExpiresAtUtc is wrong.
        if (utcNow >= grant.GrantedAtUtc.Add(MaximumLifetime))
        {
            return AccessGrantCheckResult.Denied(AccessGrantCheckFailure.GrantExpired);
        }

        if (!grant.IsActiveAt(utcNow))
        {
            return AccessGrantCheckResult.Denied(
                grant.StatusAt(utcNow) == AccessGrantStatus.Ended
                    ? AccessGrantCheckFailure.GrantEnded
                    : AccessGrantCheckFailure.GrantExpired);
        }

        return AccessGrantCheckResult.Allowed(grant);
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
