namespace MediPassPlus.Application.Common;

/// <summary>
/// One page of a list response: the rows on the requested page together with the total count of the caller's rows and
/// the page that was returned (NFR-PRF-04).
/// </summary>
/// <typeparam name="T">The type of one row on the page.</typeparam>
public class PagedResult<T>
{
    /// <summary>The rows on the requested page.</summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>The number of rows the caller owns across every page.</summary>
    public int TotalCount { get; init; }

    /// <summary>The page number that was returned.</summary>
    public int Page { get; init; }

    /// <summary>The page size that was returned.</summary>
    public int PageSize { get; init; }
}
