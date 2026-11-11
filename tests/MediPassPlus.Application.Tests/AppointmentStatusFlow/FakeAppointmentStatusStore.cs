using MediPassPlus.Application.AppointmentStatusFlow;

namespace MediPassPlus.Application.Tests.AppointmentStatusFlow;

internal sealed class FakeAppointmentStatusStore : IAppointmentStatusStore
{
    public CheckInContext? CheckInContext { get; set; }

    public CompleteContext? CompleteContext { get; set; }

    public bool ConflictOnSave { get; set; }

    public int SaveCount { get; private set; }

    public bool CheckInLoaded { get; private set; }

    public bool CompleteLoaded { get; private set; }

    public Task<CheckInContext?> LoadForCheckInAsync(
        int appointmentId,
        Guid staffUserId,
        CancellationToken cancellationToken = default)
    {
        CheckInLoaded = true;
        return Task.FromResult(CheckInContext);
    }

    public Task<CompleteContext?> LoadForCompleteAsync(
        int appointmentId,
        Guid doctorUserId,
        CancellationToken cancellationToken = default)
    {
        CompleteLoaded = true;
        return Task.FromResult(CompleteContext);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;

        return ConflictOnSave
            ? throw new AppointmentStatusConflictException()
            : Task.CompletedTask;
    }
}
