using MediPassPlus.Application.Staff;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Staff;

public class StaffSlotService : IStaffSlotService
{
    private readonly MediPassDbContext _db;
    private readonly TimeProvider _time;

    public StaffSlotService(MediPassDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<StaffSlotResult> CreateAsync(
        Guid staffUserId, int doctorId, CreateStaffSlotDto request, CancellationToken cancellationToken)
    {
        // The clinic comes from the database, never from the request.
        var clinicId = await GetClinicIdAsync(staffUserId, cancellationToken);
        if (clinicId is null)
        {
            return new StaffSlotResult(null, StaffSlotFailure.StaffProfileNotFound);
        }

        // The clinic filter is part of the query: a doctor of another clinic looks like a missing doctor.
        var doctorExists = await _db.Doctors
            .AnyAsync(d => d.Id == doctorId && d.ClinicId == clinicId, cancellationToken);
        if (!doctorExists)
        {
            return new StaffSlotResult(null, StaffSlotFailure.DoctorNotFound);
        }

        var start = DateTime.SpecifyKind(request.StartTimeUtc, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(request.EndTimeUtc, DateTimeKind.Utc);

        if (start <= _time.GetUtcNow().UtcDateTime)
        {
            return new StaffSlotResult(null, StaffSlotFailure.SlotInPast);
        }

        // Two ranges overlap when each one starts before the other ends.
        var overlaps = await _db.DoctorSlots
            .AnyAsync(s => s.DoctorId == doctorId && s.StartTimeUtc < end && start < s.EndTimeUtc, cancellationToken);
        if (overlaps)
        {
            return new StaffSlotResult(null, StaffSlotFailure.SlotOverlaps);
        }

        var slot = new DoctorSlot
        {
            DoctorId = doctorId,
            StartTimeUtc = start,
            EndTimeUtc = end,
            IsAvailable = true
        };

        _db.DoctorSlots.Add(slot);
        await _db.SaveChangesAsync(cancellationToken);

        return new StaffSlotResult(ToDto(slot), StaffSlotFailure.None);
    }

    public async Task<StaffSlotResult> DeleteAsync(
        Guid staffUserId, int doctorId, int slotId, CancellationToken cancellationToken)
    {
        var clinicId = await GetClinicIdAsync(staffUserId, cancellationToken);
        if (clinicId is null)
        {
            return new StaffSlotResult(null, StaffSlotFailure.StaffProfileNotFound);
        }

        // Clinic ownership is enforced in the query itself.
        var slot = await (
            from s in _db.DoctorSlots
            join d in _db.Doctors on s.DoctorId equals d.Id
            where s.Id == slotId && s.DoctorId == doctorId && d.ClinicId == clinicId
            select s)
            .FirstOrDefaultAsync(cancellationToken);

        if (slot is null)
        {
            return new StaffSlotResult(null, StaffSlotFailure.SlotNotFound);
        }

        var hasAppointment = await _db.Appointments.AnyAsync(
            a => a.DoctorSlotId == slotId && a.Status != AppointmentStatus.Cancelled, cancellationToken);
        if (hasAppointment)
        {
            return new StaffSlotResult(null, StaffSlotFailure.SlotAlreadyBooked);
        }

        var dto = ToDto(slot);
        _db.DoctorSlots.Remove(slot);
        await _db.SaveChangesAsync(cancellationToken);

        return new StaffSlotResult(dto, StaffSlotFailure.None);
    }

    private Task<int?> GetClinicIdAsync(Guid staffUserId, CancellationToken cancellationToken) =>
        _db.ClinicStaff
            .AsNoTracking()
            .Where(s => s.UserId == staffUserId)
            .Select(s => (int?)s.ClinicId)
            .FirstOrDefaultAsync(cancellationToken);

    private static StaffSlotDto ToDto(DoctorSlot slot) =>
        new(slot.Id, slot.DoctorId, slot.StartTimeUtc, slot.EndTimeUtc, slot.IsAvailable);
}