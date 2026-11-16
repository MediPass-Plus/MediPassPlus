using MediPassPlus.Application.Booking;
using MediPassPlus.Application.Staff;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Staff;

public class StaffBookingService : IStaffBookingService
{
    private readonly MediPassDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBookingService _booking;

    public StaffBookingService(
        MediPassDbContext db,
        UserManager<ApplicationUser> userManager,
        IBookingService booking)
    {
        _db = db;
        _userManager = userManager;
        _booking = booking;
    }

    public async Task<StaffBookingResult> BookAsync(
        Guid staffUserId,
        StaffBookAppointmentDto request,
        CancellationToken cancellationToken)
    {
        // The clinic comes from the database, never from the request.
        var clinicId = await _db.ClinicStaff
            .AsNoTracking()
            .Where(s => s.UserId == staffUserId)
            .Select(s => (int?)s.ClinicId)
            .FirstOrDefaultAsync(cancellationToken);

        if (clinicId is null)
        {
            return Fail(StaffBookingFailure.StaffProfileNotFound);
        }

        // The clinic filter is part of the query: a doctor of another clinic looks like a missing doctor.
        var doctorExists = await _db.Doctors
            .AnyAsync(d => d.Id == request.DoctorId && d.ClinicId == clinicId, cancellationToken);
        if (!doctorExists)
        {
            return Fail(StaffBookingFailure.DoctorNotFound);
        }

        // The patient is resolved on the server from the email and must have the Patient role.
        var patient = await _userManager.FindByEmailAsync(request.PatientEmail);
        if (patient is null || !await _userManager.IsInRoleAsync(patient, DefaultRoles.Patient))
        {
            return Fail(StaffBookingFailure.PatientNotFound);
        }

        // All booking rules (free slot, future, active accounts, concurrency) stay in the booking service.
        var result = await _booking.BookAsync(request.DoctorId, request.SlotId, patient.Id, cancellationToken);

        return result.IsSuccess
            ? new StaffBookingResult(result.Appointment, StaffBookingFailure.None, BookingFailure.None)
            : new StaffBookingResult(null, StaffBookingFailure.Booking, result.Failure);
    }

    private static StaffBookingResult Fail(StaffBookingFailure failure) =>
        new(null, failure, BookingFailure.None);
}