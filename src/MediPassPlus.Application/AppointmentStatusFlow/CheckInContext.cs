using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.StaffAdmin;

namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// The tracked state a staff check-in needs: the appointment with its slot and doctor, the authenticated staff
/// member's clinic profile, and the patient account used for the staff-facing view.
/// </summary>
/// <param name="Appointment">The tracked appointment, with its doctor and slot loaded.</param>
/// <param name="Doctor">The doctor who owns the appointment.</param>
/// <param name="Staff">The authenticated staff profile, or null when the user has none.</param>
/// <param name="Patient">The patient's account, or null when it no longer exists.</param>
public sealed record CheckInContext(
    Appointment Appointment,
    Doctor? Doctor,
    ClinicStaff? Staff,
    ApplicationUser? Patient);
