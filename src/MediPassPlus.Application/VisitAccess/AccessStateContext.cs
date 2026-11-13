using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The stored state an access-state read needs: the appointment's newest request and its one grant. Both are
/// read-only; the statuses are derived by the service, never written.
/// </summary>
/// <param name="Request">The appointment's newest access request, or null when none exists.</param>
/// <param name="Grant">The appointment's grant, or null when none exists.</param>
public sealed record AccessStateContext(
    AccessRequest? Request,
    AccessGrant? Grant);
