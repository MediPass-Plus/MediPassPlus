namespace MediPassPlus.Application.Common;

/// <summary>
/// The shared paging query string of a list endpoint: page 1 and 20 rows by default, and at most 50 rows per page
/// (NFR-PRF-04). The page size is validated by the endpoint and capped at <see cref="MaxPageSize"/> before it reaches
/// the store.
/// </summary>
public class PageRequest
{
    /// <summary>The default page number.</summary>
    public const int DefaultPage = 1;

    /// <summary>The default page size.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The largest page size a list endpoint accepts.</summary>
    public const int MaxPageSize = 50;

    /// <summary>The requested page number, starting at 1.</summary>
    public int Page { get; set; } = DefaultPage;

    /// <summary>The requested page size.</summary>
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>Returns the page size, capped at <see cref="MaxPageSize"/>.</summary>
    /// <returns>The page size to send to the store.</returns>
    public int GetPageSize() => Math.Min(PageSize, MaxPageSize);

    /// <summary>Returns how many rows to skip to reach the requested page.</summary>
    /// <returns>The number of rows to skip.</returns>
    public int GetSkip() => (int)Math.Min((long)(Page - 1) * GetPageSize(), int.MaxValue);
}
