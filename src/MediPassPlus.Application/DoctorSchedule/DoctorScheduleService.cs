using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.DoctorSchedule;

/// <summary>
/// Resolves the doctor from the authenticated user and reads one clinic-local day. The date is interpreted in the
/// Africa/Cairo calendar, so a query for 2026-11-11 covers Cairo midnight to the next Cairo midnight, not UTC
/// midnight (FR-BKG-07, US-10).
/// </summary>
public sealed class DoctorScheduleService : IDoctorScheduleService
{
    private readonly IDoctorScheduleStore _store;
    private readonly IClinicTimeZone _clinicTimeZone;

    /// <summary>Initializes a new instance of the <see cref="DoctorScheduleService"/> class.</summary>
    /// <param name="store">The read store for the doctor profile and appointments.</param>
    /// <param name="clinicTimeZone">The clinic calendar used to turn the local date into a UTC range.</param>
    public DoctorScheduleService(IDoctorScheduleStore store, IClinicTimeZone clinicTimeZone)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clinicTimeZone);

        _store = store;
        _clinicTimeZone = clinicTimeZone;
    }

    /// <inheritdoc />
    public async Task<DoctorScheduleResult> GetAsync(
        Guid doctorUserId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var doctor = await _store.FindDoctorByUserIdAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return DoctorScheduleResult.Denied(DoctorScheduleFailure.DoctorNotFound);
        }

        var fromUtc = _clinicTimeZone.GetStartOfDayUtc(date);
        var toUtc = _clinicTimeZone.GetStartOfNextDayUtc(date);

        var rows = await _store.ListAsync(doctor.Id, fromUtc, toUtc, cancellationToken);

        return DoctorScheduleResult.Success(rows
            .Select(row => new DoctorScheduleItemDto(
                row.AppointmentId,
                row.StartTimeUtc,
                row.EndTimeUtc,
                row.PatientName,
                row.Status.ToString()))
            .ToList());
    }
}
