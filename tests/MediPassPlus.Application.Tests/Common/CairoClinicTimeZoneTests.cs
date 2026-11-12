using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Tests.Common;

/// <summary>
/// The clinic calendar is Africa/Cairo. Stored timestamps stay UTC, while a clinic-local date is converted with the
/// platform's daylight-saving rules instead of a hard-coded offset (FR-BKG-07, FR-STF-03).
/// </summary>
public class CairoClinicTimeZoneTests
{
    private readonly CairoClinicTimeZone _timeZone = new();

    [Fact]
    public void GetLocalDate_WhenUtcIsLateEvening_ReturnsTheNextCairoDate()
    {
        var utc = new DateTime(2026, 11, 8, 22, 30, 0, DateTimeKind.Utc);

        var localDate = _timeZone.GetLocalDate(utc);

        Assert.Equal(new DateOnly(2026, 11, 9), localDate);
    }

    [Fact]
    public void GetStartOfDayUtc_ReturnsCairoMidnightAsUtc()
    {
        var startUtc = _timeZone.GetStartOfDayUtc(new DateOnly(2026, 11, 9));

        Assert.Equal(new DateTime(2026, 11, 8, 22, 0, 0, DateTimeKind.Utc), startUtc);
    }

    [Fact]
    public void GetStartOfNextDayUtc_ReturnsTheNextCairoMidnightAsUtc()
    {
        var nextStartUtc = _timeZone.GetStartOfNextDayUtc(new DateOnly(2026, 11, 9));

        Assert.Equal(new DateTime(2026, 11, 9, 22, 0, 0, DateTimeKind.Utc), nextStartUtc);
    }

    [Fact]
    public void GetStartOfDayUtc_InSummer_UsesTheDaylightSavingOffset()
    {
        // Egypt observes daylight saving time in July, so Cairo midnight is 21:00 UTC on the previous day.
        var startUtc = _timeZone.GetStartOfDayUtc(new DateOnly(2026, 7, 1));

        Assert.Equal(new DateTime(2026, 6, 30, 21, 0, 0, DateTimeKind.Utc), startUtc);
    }

    [Fact]
    public void IsLocalDate_ComparesTheCairoDateNotTheUtcDate()
    {
        var utc = new DateTime(2026, 11, 8, 22, 30, 0, DateTimeKind.Utc);

        Assert.False(_timeZone.IsLocalDate(utc, new DateOnly(2026, 11, 8)));
        Assert.True(_timeZone.IsLocalDate(utc, new DateOnly(2026, 11, 9)));
    }
}
