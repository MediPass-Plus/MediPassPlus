namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The patient-facing access state of one appointment. Every status is derived from the stored timestamps at read
/// time; nothing here is persisted (ADR-0011, MP-97).
/// </summary>
/// <param name="AppointmentId">The appointment being inspected.</param>
/// <param name="AccessRequestId">The appointment's newest request, or null when none exists.</param>
/// <param name="RequestStatus">The request status name, or null when none exists.</param>
/// <param name="GrantId">The appointment's grant, or null when none exists.</param>
/// <param name="GrantStatus">The derived grant status name (Active, Ended or Expired), or null.</param>
/// <param name="ExpiresAtUtc">The grant's immutable two-hour limit, or null.</param>
/// <param name="EndedAtUtc">The grant's scheduled or actual end, or null.</param>
/// <param name="RemainingSeconds">Seconds until the grant's effective end, or 0 when it is not active.</param>
public sealed record AccessStateResponse(
    int AppointmentId,
    Guid? AccessRequestId,
    string? RequestStatus,
    Guid? GrantId,
    string? GrantStatus,
    DateTime? ExpiresAtUtc,
    DateTime? EndedAtUtc,
    int RemainingSeconds);
