using MediPassPlus.Application.Common;
using MediPassPlus.Application.Staff;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Staff;

public class StaffScheduleService : IStaffScheduleService
{
    private readonly MediPassDbContext _db;
    private readonly IClinicTimeZone _clinicTimeZone;

    public StaffScheduleService(MediPassDbContext db, IClinicTimeZone clinicTimeZone)
    {
        _db = db;
        _clinicTimeZone = clinicTimeZone;
    }

    public async Task<StaffScheduleResult> GetScheduleAsync(
        Guid staffUserId,
        DateOnly date,
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
            return new StaffScheduleResult([], StaffScheduleFailure.StaffProfileNotFound);
        }

        // The requested date is a clinic-local (Africa/Cairo) day, converted to UTC boundaries.
        var dayStart = _clinicTimeZone.GetStartOfDayUtc(date);
        var dayEnd = _clinicTimeZone.GetStartOfNextDayUtc(date);

        // The clinic filter is part of the query. Cancelled appointments are hidden, like the doctor schedule.
        var items = await (
            from a in _db.Appointments.AsNoTracking()
            join slot in _db.DoctorSlots on a.DoctorSlotId equals slot.Id
            join d in _db.Doctors on a.DoctorId equals d.Id
            join patient in _db.Users on a.PatientId equals patient.Id
            where d.ClinicId == clinicId
                && a.Status != AppointmentStatus.Cancelled
                && slot.StartTimeUtc >= dayStart
                && slot.StartTimeUtc < dayEnd
            orderby slot.StartTimeUtc, a.Id
            select new
            {
                a.Id,
                patient.FullName,
                patient.PhoneNumber,
                slot.StartTimeUtc,
                a.Status
            })
            .ToListAsync(cancellationToken);

        var result = items
            .Select(i => new StaffScheduleItemDto(
                i.Id,
                i.FullName,
                i.PhoneNumber,
                i.StartTimeUtc,
                i.Status.ToString()))
            .ToList();

        return new StaffScheduleResult(result, StaffScheduleFailure.None);
    }
}