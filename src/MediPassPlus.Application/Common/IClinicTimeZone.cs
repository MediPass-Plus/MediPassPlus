namespace MediPassPlus.Application.Common;

/// <summary>
/// Converts between UTC instants and the clinic's local calendar day. The clinic works on Cairo local dates while
/// every stored timestamp stays in UTC (FR-BKG-07, FR-STF-03).
/// </summary>
public interface IClinicTimeZone
{
    /// <summary>Returns the clinic-local date of a UTC instant.</summary>
    /// <param name="utcDateTime">The UTC instant.</param>
    /// <returns>The local date in the clinic's time zone.</returns>
    DateOnly GetLocalDate(DateTime utcDateTime);

    /// <summary>Returns the UTC instant at which the clinic-local day starts.</summary>
    /// <param name="localDate">The clinic-local date.</param>
    /// <returns>The first UTC instant of that local day.</returns>
    DateTime GetStartOfDayUtc(DateOnly localDate);

    /// <summary>Returns the UTC instant at which the next clinic-local day starts.</summary>
    /// <param name="localDate">The clinic-local date.</param>
    /// <returns>The first UTC instant of the next local day.</returns>
    DateTime GetStartOfNextDayUtc(DateOnly localDate);

    /// <summary>True when the UTC instant falls on the given clinic-local date.</summary>
    /// <param name="utcDateTime">The UTC instant.</param>
    /// <param name="localDate">The clinic-local date.</param>
    /// <returns>True when both describe the same local day.</returns>
    bool IsLocalDate(DateTime utcDateTime, DateOnly localDate);
}
