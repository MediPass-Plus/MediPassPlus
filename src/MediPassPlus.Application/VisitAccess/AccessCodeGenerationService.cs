using MediPassPlus.Application.Common;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Generates the six-digit fallback for the authenticated patient's pending request. The visit must be today and
/// checked in or in visit, and the patient must not already have approved access. A new code revokes the previous
/// live code under the same request, including a locked or expired one, so the filtered unique index never blocks a
/// replacement (US-22, ADR-0001). Only the HMAC is stored (FR-ACS-05).
/// </summary>
public sealed class AccessCodeGenerationService : IAccessCodeGenerationService
{
    private readonly IVisitAccessScopeService _scopeService;
    private readonly IAccessCodeStore _store;
    private readonly IAccessCodeHasher _hasher;
    private readonly IClinicTimeZone _clinicTimeZone;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AccessCodeGenerationService"/> class.</summary>
    /// <param name="scopeService">Verifies that the authenticated patient owns the appointment.</param>
    /// <param name="store">The unit of work that loads and stages the code state.</param>
    /// <param name="hasher">Hashes the code before it is stored.</param>
    /// <param name="clinicTimeZone">The clinic calendar used for the "today" check.</param>
    /// <param name="timeProvider">The clock used for the five-minute lifetime.</param>
    public AccessCodeGenerationService(
        IVisitAccessScopeService scopeService,
        IAccessCodeStore store,
        IAccessCodeHasher hasher,
        IClinicTimeZone clinicTimeZone,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeService);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(clinicTimeZone);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _scopeService = scopeService;
        _store = store;
        _hasher = hasher;
        _clinicTimeZone = clinicTimeZone;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<AccessCodeGenerationResult> GenerateAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        var scopeResult = await _scopeService.ResolveForPatientAsync(
            appointmentId,
            patientUserId,
            cancellationToken);

        if (!scopeResult.IsAllowed)
        {
            return AccessCodeGenerationResult.Denied(MapScopeFailure(scopeResult.Failure));
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
            return AccessCodeGenerationResult.Denied(AccessCodeGenerationFailure.AppointmentNotFound);
        }

        if (context.Appointment.Status is not (AppointmentStatus.CheckedIn or AppointmentStatus.InVisit))
        {
            return AccessCodeGenerationResult.Denied(AccessCodeGenerationFailure.AppointmentNotCheckedIn);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var slot = context.Appointment.DoctorSlot
            ?? throw new InvalidOperationException("The appointment has no doctor slot.");

        if (!_clinicTimeZone.IsLocalDate(slot.StartTimeUtc, _clinicTimeZone.GetLocalDate(now)))
        {
            return AccessCodeGenerationResult.Denied(AccessCodeGenerationFailure.AppointmentNotToday);
        }

        if (context.Requests.Any(request => request.Status == AccessRequestStatus.Approved))
        {
            return AccessCodeGenerationResult.Denied(AccessCodeGenerationFailure.AccessAlreadyGranted);
        }

        var pending = context.Requests
            .Where(request => request.Status == AccessRequestStatus.Pending)
            .OrderByDescending(request => request.CreatedAtUtc)
            .FirstOrDefault();

        if (pending is null)
        {
            return AccessCodeGenerationResult.Denied(AccessCodeGenerationFailure.RequestNotPending);
        }

        var liveCodes = context.Credentials
            .Where(credential => credential.AccessRequestId == pending.Id
                && credential.Type == VisitCredentialType.NumericCode
                && credential.RevokedAtUtc is null
                && credential.ConsumedAtUtc is null)
            .ToArray();

        if (liveCodes.Length > 0)
        {
            foreach (var liveCode in liveCodes)
            {
                liveCode.Revoke(now);
            }

            // Save the revocations first: the filtered unique index allows only one live code per request.
            try
            {
                await _store.SaveChangesAsync(cancellationToken);
            }
            catch (AccessCodeConflictException)
            {
                return AccessCodeGenerationResult.Denied(AccessCodeGenerationFailure.RequestConflict);
            }
        }

        var code = AccessCodeGenerator.Generate();
        var credential = VisitAccessCredential.Create(
            pending.Id,
            VisitCredentialType.NumericCode,
            _hasher.Hash(code),
            now);

        _store.AddCredential(credential);

        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch (AccessCodeConflictException)
        {
            // A concurrent generation replaced the code first, or the appointment state changed.
            return AccessCodeGenerationResult.Denied(AccessCodeGenerationFailure.RequestConflict);
        }

        return AccessCodeGenerationResult.Success(new AccessCodeResponse(
            context.Appointment.Id,
            pending.Id,
            code,
            credential.ExpiresAtUtc));
    }

    private static AccessCodeGenerationFailure MapScopeFailure(VisitAccessScopeFailure failure) =>
        failure switch
        {
            VisitAccessScopeFailure.AppointmentNotFound => AccessCodeGenerationFailure.AppointmentNotFound,
            VisitAccessScopeFailure.PatientMismatch => AccessCodeGenerationFailure.PatientMismatch,
            VisitAccessScopeFailure.DoctorNotFound or VisitAccessScopeFailure.DoctorMismatch =>
                AccessCodeGenerationFailure.PatientMismatch,
            VisitAccessScopeFailure.None => throw new InvalidOperationException(
                "A refused scope result must have a failure reason."),
            _ => throw new InvalidOperationException($"Unknown scope failure: {failure}.")
        };
}
