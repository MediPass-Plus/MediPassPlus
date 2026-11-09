using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.Booking;

/// <summary>
/// The tracked target slot for a reschedule and whether another active appointment already uses it.
/// </summary>
/// <param name="Slot">The tracked target slot.</param>
/// <param name="HasActiveAppointment">True when a non-cancelled appointment already uses the slot.</param>
public sealed record TargetSlotContext(DoctorSlot Slot, bool HasActiveAppointment);
