using System.Data;
using MediPassPlus.Application.Staff;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Staff;

public class StaffSlotService : IStaffSlotService
{
    // SQL Server error numbers: 1205 = deadlock victim, 547 = foreign key violation.
    private const int DeadlockVictim = 1205;
    private const int ForeignKeyViolation = 547;

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

        // The overlap check and the insert run in one Serializable transaction, so two concurrent requests
        // cannot both pass the check. A serialization or deadlock failure is reported as a conflict.
        var strategy = _db.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                // Two ranges overlap when each one starts before the other ends.
                var overlaps = await _db.DoctorSlots.AnyAsync(
                    s => s.DoctorId == doctorId && s.StartTimeUtc < end && start < s.EndTimeUtc,
                    cancellationToken);
                if (overlaps)
                {
                    await transaction.RollbackAsync(cancellationToken);
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
                await transaction.CommitAsync(cancellationToken);

                return new StaffSlotResult(ToDto(slot), StaffSlotFailure.None);
            });
        }
        catch (Exception ex) when (IsSqlError(ex, DeadlockVictim))
        {
            _db.ChangeTracker.Clear();
            return new StaffSlotResult(null, StaffSlotFailure.SlotOverlaps);
        }
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

        // Any appointment that references the slot blocks the delete, even a cancelled one: the appointment
        // history must be kept and the foreign key is Restrict.
        var hasAppointment = await _db.Appointments.AnyAsync(
            a => a.DoctorSlotId == slotId, cancellationToken);
        if (hasAppointment)
        {
            return new StaffSlotResult(null, StaffSlotFailure.SlotAlreadyBooked);
        }

        var dto = ToDto(slot);

        try
        {
            _db.DoctorSlots.Remove(slot);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSqlError(ex, ForeignKeyViolation))
        {
            // A booking was created between the check and the delete.
            _db.ChangeTracker.Clear();
            return new StaffSlotResult(null, StaffSlotFailure.SlotAlreadyBooked);
        }

        return new StaffSlotResult(dto, StaffSlotFailure.None);
    }

    private Task<int?> GetClinicIdAsync(Guid staffUserId, CancellationToken cancellationToken) =>
        _db.ClinicStaff
            .AsNoTracking()
            .Where(s => s.UserId == staffUserId)
            .Select(s => (int?)s.ClinicId)
            .FirstOrDefaultAsync(cancellationToken);

    private static bool IsSqlError(Exception exception, int number)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Errors.Cast<SqlError>().Any(e => e.Number == number))
            {
                return true;
            }
        }

        return false;
    }

    private static StaffSlotDto ToDto(DoctorSlot slot) =>
        new(slot.Id, slot.DoctorId, slot.StartTimeUtc, slot.EndTimeUtc, slot.IsAvailable);
}