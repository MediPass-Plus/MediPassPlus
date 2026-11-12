using MediPassPlus.Application.DoctorSchedule;
using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.Tests.DoctorSchedule;

internal sealed class FakeDoctorScheduleStore : IDoctorScheduleStore
{
    public Doctor? Doctor { get; set; }

    public IReadOnlyList<DoctorScheduleRow> Rows { get; set; } = [];

    public bool WasListed { get; private set; }

    public int? LastDoctorId { get; private set; }

    public DateTime? LastFromUtc { get; private set; }

    public DateTime? LastToUtc { get; private set; }

    public Task<Doctor?> FindDoctorByUserIdAsync(
        Guid doctorUserId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Doctor);

    public Task<IReadOnlyList<DoctorScheduleRow>> ListAsync(
        int doctorId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        WasListed = true;
        LastDoctorId = doctorId;
        LastFromUtc = fromUtc;
        LastToUtc = toUtc;
        return Task.FromResult(Rows);
    }
}
