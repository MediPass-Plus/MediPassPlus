using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// The tracked state a doctor completion needs: the appointment, the authenticated doctor's profile and the
/// appointment's access grant, when one exists.
/// </summary>
/// <param name="Appointment">The tracked appointment.</param>
/// <param name="Doctor">The authenticated doctor's profile, or null when the user has none.</param>
/// <param name="Grant">The appointment's grant, or null when the visit never created one.</param>
public sealed record CompleteContext(Appointment Appointment, Doctor? Doctor, AccessGrant? Grant);
