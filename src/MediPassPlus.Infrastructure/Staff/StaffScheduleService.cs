using MediPassPlus.Application.Staff;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Staff;

public class StaffScheduleService : IStaffScheduleService
{
    private readonly MediPassDbContext _db;

    public StaffScheduleService(MediPassDbContext db)
    {
        _db = db;
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

        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        // The clinic filter is part of the query itself, and only reception fields are selected.
        var items = await (
            from a in _db.Appointments.AsNoTracking()
            join slot in _db.DoctorSlots on a.DoctorSlotId equals slot.Id
            join d in _db.Doctors on a.DoctorId equals d.Id
            join doctorUser in _db.Users on d.UserId equals doctorUser.Id
            join patient in _db.Users on a.PatientId equals patient.Id
            where d.ClinicId == clinicId
                && slot.StartTimeUtc >= dayStart
                && slot.StartTimeUtc < dayEnd
            orderby slot.StartTimeUtc
            select new
            {
                a.Id,
                patient.FullName,
                patient.PhoneNumber,
                slot.StartTimeUtc,
                slot.EndTimeUtc,
                DoctorName = doctorUser.FullName,
                a.Status
            })
            .ToListAsync(cancellationToken);

        var result = items
            .Select(i => new StaffScheduleItemDto(
                i.Id,
                i.FullName,
                i.PhoneNumber,
                i.StartTimeUtc,
                i.EndTimeUtc,
                i.DoctorName,
                i.Status.ToString()))
            .ToList();

        return new StaffScheduleResult(result, StaffScheduleFailure.None);
    }
}