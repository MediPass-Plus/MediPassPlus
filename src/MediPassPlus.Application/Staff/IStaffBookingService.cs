using MediPassPlus.Application.Booking;

namespace MediPassPlus.Application.Staff;

public enum StaffBookingFailure
{
    None,
    StaffProfileNotFound,
    DoctorNotFound,
    PatientNotFound,
    Booking
}

public sealed record StaffBookingResult(
    AppointmentDto? Appointment,
    StaffBookingFailure Failure,
    BookingFailure BookingFailure)
{
    public bool IsSuccess => Failure == StaffBookingFailure.None;
}

public interface IStaffBookingService
{
    // Books a slot for a patient identified by email. The clinic comes from the staff member's profile,
    // and the doctor must belong to that clinic. Booking rules are delegated to IBookingService.
    Task<StaffBookingResult> BookAsync(
        Guid staffUserId,
        StaffBookAppointmentDto request,
        CancellationToken cancellationToken);
}