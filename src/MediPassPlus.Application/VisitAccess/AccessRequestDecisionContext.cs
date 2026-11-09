using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The tracked state a patient decision needs: the request, the appointment that fixes its scope, and every
/// credential that must stop being usable once the request is answered.
/// </summary>
/// <param name="Request">The tracked request being answered.</param>
/// <param name="Appointment">The appointment that fixes the request's doctor and patient scope.</param>
/// <param name="Credentials">Every credential issued for the request.</param>
public sealed record AccessRequestDecisionContext(
    AccessRequest Request,
    Appointment Appointment,
    IReadOnlyList<VisitAccessCredential> Credentials);
