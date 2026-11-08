using MediPassPlus.Application.Audit;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Starts a visit: verifies the doctor's scope, checks the appointment is today and checked in, reuses the pending
/// request or creates a new one, and replaces the live QR credential with a new two-minute one (FR-ACS-01,
/// FR-ACS-02). A newly created request is audited in the same unit of work (ADR-0011 decision 8, ADR-0004). The
/// filtered unique index rejects a concurrent second pending request, which is surfaced as a conflict.
/// </summary>
public sealed class StartVisitService : IStartVisitService
{
    private readonly IVisitAccessScopeService _scopeService;
    private readonly IVisitStartStore _store;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="StartVisitService"/> class.</summary>
    /// <param name="scopeService">Verifies that the authenticated doctor owns the appointment.</param>
    /// <param name="store">The unit of work that loads and stages the visit-access state.</param>
    /// <param name="auditWriter">Stages the request-creation audit row in the same unit of work.</param>
    /// <param name="timeProvider">The clock used for the QR lifetime and the "today" check.</param>
    public StartVisitService(
        IVisitAccessScopeService scopeService,
        IVisitStartStore store,
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
    public async Task<StartVisitResult> StartAsync(
        int appointmentId,
        Guid doctorUserId,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        var scopeResult = await _scopeService.ResolveForDoctorAsync(
            appointmentId,
            doctorUserId,
            cancellationToken);

        if (!scopeResult.IsAllowed)
        {
            return StartVisitResult.Denied(MapFailure(scopeResult.Failure));
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
            return StartVisitResult.Denied(StartVisitFailure.AppointmentNotFound);
        }

        if (context.Appointment.Status != AppointmentStatus.CheckedIn)
        {
            return StartVisitResult.Denied(StartVisitFailure.AppointmentNotCheckedIn);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var slot = context.Appointment.DoctorSlot
            ?? throw new InvalidOperationException("The appointment has no doctor slot.");

        if (DateOnly.FromDateTime(slot.StartTimeUtc) != DateOnly.FromDateTime(now))
        {
            return StartVisitResult.Denied(StartVisitFailure.AppointmentNotToday);
        }

        var pending = context.Requests
            .Where(request => request.Status == AccessRequestStatus.Pending)
            .OrderByDescending(request => request.CreatedAtUtc)
            .FirstOrDefault();

        var qrToken = QrTokenGenerator.Generate();

        if (context.LiveQrCredentials.Count > 0)
        {
            foreach (var liveCredential in context.LiveQrCredentials)
            {
                liveCredential.Revoke(now);
            }

            // Save the revocations first: the filtered unique index allows only one live QR credential per request.
            await _store.SaveChangesAsync(cancellationToken);
        }

        AccessRequest request;
        if (pending is null)
        {
            request = AccessRequest.Create(context.Appointment, context.Doctor, now);
            _store.AddRequest(request);

            // A new request is an access-control event; stage the audit row so it commits with the request
            // (ADR-0011 decision 8, ADR-0004 decision 7). No medical content or credential is recorded.
            _auditWriter.Add(new AuditEntry(
                doctorUserId,
                context.Appointment.PatientId,
                AuditAction.AccessRequestCreated,
                AuditEntityType.AccessRequest,
                request.Id.ToString(),
                ipAddress));
        }
        else
        {
            request = pending;
        }

        var credential = VisitAccessCredential.Create(
            request.Id,
            VisitCredentialType.QrToken,
            qrToken.Sha256Hash,
            now);

        _store.AddCredential(credential);

        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (PendingRequestConflictException)
        {
            // A concurrent call created its pending request first; the filtered unique index rejected this one.
            // 409 lets the caller retry, and the retry reuses the request that won the race.
            return StartVisitResult.Denied(StartVisitFailure.RequestConflict);
        }

        return StartVisitResult.Success(new StartVisitResponse(
            context.Appointment.Id,
            request.Id,
            qrToken.Value,
            credential.ExpiresAtUtc));
    }

    private static StartVisitFailure MapFailure(VisitAccessScopeFailure failure) =>
        failure switch
        {
            VisitAccessScopeFailure.AppointmentNotFound => StartVisitFailure.AppointmentNotFound,
            VisitAccessScopeFailure.DoctorNotFound => StartVisitFailure.DoctorNotFound,
            VisitAccessScopeFailure.DoctorMismatch => StartVisitFailure.DoctorMismatch,
            VisitAccessScopeFailure.PatientMismatch => StartVisitFailure.DoctorMismatch,
            VisitAccessScopeFailure.None => throw new InvalidOperationException(
                "A refused scope result must have a failure reason."),
            _ => throw new InvalidOperationException($"Unknown scope failure: {failure}.")
        };
}
