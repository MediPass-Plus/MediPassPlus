using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Reads one appointment's derived access state. The actor's scope is resolved from the appointment itself, and
/// the grant status and remaining seconds are derived from the stored timestamps at read time; nothing is written
/// (FR-ACS-06, FR-ACS-07, ADR-0011).
/// </summary>
public sealed class AccessStateService : IAccessStateService
{
    private readonly IVisitAccessScopeService _scopeService;
    private readonly IAccessStateStore _store;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AccessStateService"/> class.</summary>
    /// <param name="scopeService">Verifies that the authenticated doctor or patient owns the appointment.</param>
    /// <param name="store">The read-only request and grant lookup.</param>
    /// <param name="timeProvider">The clock used to derive the grant status and remaining seconds.</param>
    public AccessStateService(
        IVisitAccessScopeService scopeService,
        IAccessStateStore store,
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
    public Task<AccessStateResult> GetForDoctorAsync(
        int appointmentId,
        Guid doctorUserId,
        CancellationToken cancellationToken = default) =>
        GetAsync(
            cancellationToken,
            token => _scopeService.ResolveForDoctorAsync(appointmentId, doctorUserId, token));

    /// <inheritdoc />
    public Task<AccessStateResult> GetForPatientAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default) =>
        GetAsync(
            cancellationToken,
            token => _scopeService.ResolveForPatientAsync(appointmentId, patientUserId, token));

    private async Task<AccessStateResult> GetAsync(
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<VisitAccessScopeResult>> resolveScope)
    {
        var scopeResult = await resolveScope(cancellationToken);
        if (!scopeResult.IsAllowed)
        {
            return AccessStateResult.Denied(MapFailure(scopeResult.Failure));
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
            return AccessStateResult.Denied(AccessStateFailure.AppointmentNotFound);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var grant = context.Grant;
        var remainingSeconds = 0;

        if (grant is not null && grant.IsActiveAt(now))
        {
            remainingSeconds = (int)Math.Ceiling((grant.EffectiveEndUtc - now).TotalSeconds);
        }

        return AccessStateResult.Success(new AccessStateResponse(
            scope.AppointmentId,
            context.Request?.Id,
            context.Request?.Status.ToString(),
            grant?.Id,
            grant?.StatusAt(now).ToString(),
            grant?.ExpiresAtUtc,
            grant?.EndedAtUtc,
            remainingSeconds));
    }

    private static AccessStateFailure MapFailure(VisitAccessScopeFailure failure) =>
        failure switch
        {
            VisitAccessScopeFailure.AppointmentNotFound => AccessStateFailure.AppointmentNotFound,
            VisitAccessScopeFailure.DoctorNotFound or VisitAccessScopeFailure.DoctorMismatch =>
                AccessStateFailure.ActorMismatch,
            VisitAccessScopeFailure.PatientMismatch => AccessStateFailure.ActorMismatch,
            VisitAccessScopeFailure.None => throw new InvalidOperationException(
                "A refused scope result must have a failure reason."),
            _ => throw new InvalidOperationException($"Unknown scope failure: {failure}.")
        };
}
