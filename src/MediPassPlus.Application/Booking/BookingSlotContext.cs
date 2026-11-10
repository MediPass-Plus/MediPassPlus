using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;

namespace MediPassPlus.Application.Booking;

/// <summary>
/// The tracked state a booking decision needs: the slot, the doctor it should belong to, both user accounts, and
/// whether another active appointment already occupies the slot.
/// </summary>
/// <param name="Slot">The tracked slot that would be booked.</param>
/// <param name="Doctor">The doctor requested by the caller, or null when no doctor has that identifier.</param>
/// <param name="DoctorUser">The doctor's user account, or null when the profile has no account.</param>
/// <param name="PatientUser">The authenticated patient's account, or null when it no longer exists.</param>
/// <param name="HasActiveAppointment">True when a non-cancelled appointment already uses the slot.</param>
public sealed record BookingSlotContext(
    DoctorSlot Slot,
    Doctor? Doctor,
    ApplicationUser? DoctorUser,
    ApplicationUser? PatientUser,
    bool HasActiveAppointment);
