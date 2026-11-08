using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The tracked state a start-visit decision needs: the scoped appointment and doctor, every request for the
/// appointment, and every QR credential that is still live on those requests.
/// </summary>
/// <param name="Appointment">The appointment with its doctor slot loaded.</param>
/// <param name="Doctor">The doctor who owns the appointment.</param>
/// <param name="Requests">Every access request for the appointment, newest first.</param>
/// <param name="LiveQrCredentials">Unrevoked and unconsumed QR credentials that must be replaced.</param>
public sealed record StartVisitContext(
    Appointment Appointment,
    Doctor Doctor,
    IReadOnlyList<AccessRequest> Requests,
    IReadOnlyList<VisitAccessCredential> LiveQrCredentials);
