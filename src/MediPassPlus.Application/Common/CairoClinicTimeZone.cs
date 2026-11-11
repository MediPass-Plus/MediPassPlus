namespace MediPassPlus.Application.Common;

/// <summary>
/// The clinic calendar is Africa/Cairo. Windows exposes the same zone as "Egypt Standard Time", so both identifiers
/// are tried; every conversion uses <see cref="TimeZoneInfo"/> so daylight-saving changes are handled by the platform
/// instead of a hard-coded offset.
/// </summary>
public sealed class CairoClinicTimeZone : IClinicTimeZone
{
    private static readonly string[] TimeZoneIds = ["Africa/Cairo", "Egypt Standard Time"];

    private static readonly TimeZoneInfo Cairo = ResolveCairo();

    /// <inheritdoc />
    public DateOnly GetLocalDate(DateTime utcDateTime) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utcDateTime), Cairo));

    /// <inheritdoc />
    public DateTime GetStartOfDayUtc(DateOnly localDate) =>
        ToUtc(localDate.ToDateTime(TimeOnly.MinValue));

    /// <inheritdoc />
    public DateTime GetStartOfNextDayUtc(DateOnly localDate) =>
        ToUtc(localDate.AddDays(1).ToDateTime(TimeOnly.MinValue));

    /// <inheritdoc />
    public bool IsLocalDate(DateTime utcDateTime, DateOnly localDate) =>
        GetLocalDate(utcDateTime) == localDate;

    private static DateTime ToUtc(DateTime localDateTime)
    {
        var local = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);

        // A daylight-saving jump can skip local midnight. Move to the first instant that exists on that date.
        while (Cairo.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, Cairo);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static TimeZoneInfo ResolveCairo()
    {
        foreach (var id in TimeZoneIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException(
            "The Africa/Cairo time zone is not available on this machine.");
    }
}
