using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Records;

/// <summary>
/// The query string of both history endpoints. The diagnoses page and the reports page are paged independently so
/// neither list can grow without a bound (NFR-PRF-04). The defaults are 1 and 20, and the maximum page size is 50.
/// </summary>
public sealed class RecordHistoryQuery : PageRequest
{
    /// <summary>The reports page number. Defaults to 1.</summary>
    public int ReportPage { get; set; } = DefaultPage;

    /// <summary>The reports page size. Defaults to 20.</summary>
    public int ReportPageSize { get; set; } = DefaultPageSize;

    /// <summary>Returns the reports page size, capped at the maximum.</summary>
    /// <returns>The page size to send to the store.</returns>
    public int GetReportPageSize() => Math.Min(ReportPageSize, MaxPageSize);

    /// <summary>Returns how many report rows to skip to reach the requested page.</summary>
    /// <returns>The number of rows to skip.</returns>
    public int GetReportSkip() =>
        (int)Math.Min((long)(ReportPage - 1) * GetReportPageSize(), int.MaxValue);
}
