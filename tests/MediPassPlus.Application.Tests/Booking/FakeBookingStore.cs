using MediPassPlus.Application.Booking;
using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.Tests.Booking;

internal sealed class FakeBookingStore : IBookingStore
{
    public BookingSlotContext? BookingSlotContext { get; set; }

    public BookingAppointmentContext? AppointmentContext { get; set; }

    public TargetSlotContext? TargetSlotContext { get; set; }

    public bool ConflictOnSave { get; set; }

    public int SaveCount { get; private set; }

    public Appointment? AddedAppointment { get; private set; }

    public bool BookingSlotLoaded { get; private set; }

    public bool AppointmentLoaded { get; private set; }

    public bool TargetSlotLoaded { get; private set; }

    public Task<BookingSlotContext?> LoadBookingSlotAsync(
        int slotId,
        int doctorId,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        BookingSlotLoaded = true;
        return Task.FromResult(BookingSlotContext);
    }

    public Task<BookingAppointmentContext?> LoadAppointmentAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        AppointmentLoaded = true;
        return Task.FromResult(AppointmentContext);
    }

    public Task<TargetSlotContext?> LoadTargetSlotAsync(
        int slotId,
        CancellationToken cancellationToken = default)
    {
        TargetSlotLoaded = true;
        return Task.FromResult(TargetSlotContext);
    }

    public void AddAppointment(Appointment appointment) => AddedAppointment = appointment;

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;

        return ConflictOnSave
            ? throw new BookingConflictException()
            : Task.CompletedTask;
    }
}
