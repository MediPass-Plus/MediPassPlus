namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The access grant created by an approved request. The appointment, doctor and patient scope is resolved from the
/// appointment, never accepted from the caller (FR-ACS-06).
/// </summary>
/// <param name="GrantId">The grant identifier.</param>
/// <param name="AccessRequestId">The request that was approved.</param>
/// <param name="AppointmentId">The appointment the grant is scoped to.</param>
/// <param name="DoctorId">The doctor who may read the record.</param>
/// <param name="GrantedAtUtc">When the patient approved the request, in UTC.</param>
/// <param name="ExpiresAtUtc">The immutable two-hour limit, in UTC.</param>
public sealed record AccessGrantResponse(
    Guid GrantId,
    Guid AccessRequestId,
    int AppointmentId,
    int DoctorId,
    DateTime GrantedAtUtc,
    DateTime ExpiresAtUtc);
