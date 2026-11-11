using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Tests.AppointmentStatusFlow;

/// <summary>
/// A deterministic clinic calendar for the status tests. The production implementation uses Africa/Cairo; these tests
/// only need a fixed offset because the Cairo conversion has its own tests.
/// </summary>
internal sealed class FakeClinicTimeZone : IClinicTimeZone
{
    public TimeSpan Offset { get; init; } = TimeSpan.FromHours(2);

    public DateOnly GetLocalDate(DateTime utcDateTime) =>
        DateOnly.FromDateTime(utcDateTime + Offset);

    public DateTime GetStartOfDayUtc(DateOnly localDate) =>
        localDate.ToDateTime(TimeOnly.MinValue) - Offset;

    public DateTime GetStartOfNextDayUtc(DateOnly localDate) =>
        localDate.AddDays(1).ToDateTime(TimeOnly.MinValue) - Offset;

    public bool IsLocalDate(DateTime utcDateTime, DateOnly localDate) =>
        GetLocalDate(utcDateTime) == localDate;
}
