using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The tracked state an access-code generation or verification needs: the scoped appointment with its slot, every
/// request for the appointment, and every credential issued for those requests.
/// </summary>
/// <param name="Appointment">The appointment with its doctor slot loaded.</param>
/// <param name="Requests">Every access request for the appointment, newest first.</param>
/// <param name="Credentials">Every credential issued for the appointment's requests, including ended ones.</param>
public sealed record AccessCodeContext(
    Appointment Appointment,
    IReadOnlyList<AccessRequest> Requests,
    IReadOnlyList<VisitAccessCredential> Credentials);
