namespace MediPassPlus.Application.DoctorSchedule;

/// <summary>
/// The outcome of a doctor schedule request: either the ordered appointments or a refusal reason.
/// </summary>
public sealed class DoctorScheduleResult
{
    private DoctorScheduleResult(
        IReadOnlyList<DoctorScheduleItemDto>? items,
        DoctorScheduleFailure failure)
    {
        Items = items;
        Failure = failure;
    }

    /// <summary>The ordered appointments, or null when the request was refused.</summary>
    public IReadOnlyList<DoctorScheduleItemDto>? Items { get; }

    /// <summary>The refusal reason, or <see cref="DoctorScheduleFailure.None"/> on success.</summary>
    public DoctorScheduleFailure Failure { get; }

    /// <summary>True when <see cref="Items"/> is available.</summary>
    public bool IsSuccess => Items is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="items">The ordered appointments.</param>
    /// <returns>A successful result.</returns>
    public static DoctorScheduleResult Success(IReadOnlyList<DoctorScheduleItemDto> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new DoctorScheduleResult(items, DoctorScheduleFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="DoctorScheduleFailure.None"/>.</param>
    /// <returns>A refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="DoctorScheduleFailure.None"/>.
    /// </exception>
    public static DoctorScheduleResult Denied(DoctorScheduleFailure failure)
    {
        if (failure == DoctorScheduleFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new DoctorScheduleResult(null, failure);
    }
}
