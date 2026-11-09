using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;

namespace MediPassPlus.Application.Booking;

/// <summary>
/// The tracked state a cancellation or reschedule decision needs: the scoped appointment with its current slot,
/// the doctor and both user accounts.
/// </summary>
/// <param name="Appointment">The tracked appointment, with its current slot loaded.</param>
/// <param name="Doctor">The doctor who owns the appointment, or null when the profile is missing.</param>
/// <param name="DoctorUser">The doctor's user account, or null when the profile has no account.</param>
/// <param name="PatientUser">The authenticated patient's account, or null when it no longer exists.</param>
public sealed record BookingAppointmentContext(
    Appointment Appointment,
    Doctor? Doctor,
    ApplicationUser? DoctorUser,
    ApplicationUser? PatientUser);
