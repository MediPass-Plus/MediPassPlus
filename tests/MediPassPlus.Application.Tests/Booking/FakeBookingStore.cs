using MediPassPlus.Application.Booking;
using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.Tests.Booking;

internal sealed class FakeBookingStore : IBookingStore
{
    public BookingSlotContext? BookingSlotContext { get; set; }

    public BookingAppointmentContext? AppointmentContext { get; set; }

    public TargetSlotContext? TargetSlotContext { get; set; }

    public PatientAppointmentsPage AppointmentsPage { get; set; } =
        new(Array.Empty<PatientAppointmentRow>(), 0);

    public Guid? ListedPatientUserId { get; private set; }

    public int? ListedSkip { get; private set; }

    public int? ListedTake { get; private set; }

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

    public Task<PatientAppointmentsPage> ListAppointmentsAsync(
        Guid patientUserId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ListedPatientUserId = patientUserId;
        ListedSkip = skip;
        ListedTake = take;

        return Task.FromResult(AppointmentsPage);
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
