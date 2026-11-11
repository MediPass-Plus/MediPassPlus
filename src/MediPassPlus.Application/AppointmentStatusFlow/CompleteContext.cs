using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// The tracked state a doctor completion needs: the appointment and the authenticated doctor's profile.
/// </summary>
/// <param name="Appointment">The tracked appointment.</param>
/// <param name="Doctor">The authenticated doctor's profile, or null when the user has none.</param>
public sealed record CompleteContext(Appointment Appointment, Doctor? Doctor);
