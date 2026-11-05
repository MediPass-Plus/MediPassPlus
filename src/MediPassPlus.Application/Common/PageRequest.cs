namespace MediPassPlus.Application.Common;

public class PageRequest
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public int Page { get; set; } = DefaultPage;
    public int PageSize { get; set; } = DefaultPageSize;

    // A page size above the maximum is cut down to the maximum.
    public int GetPageSize() => Math.Min(PageSize, MaxPageSize);

    // How many rows to skip to reach this page.
    public int GetSkip() => (int)Math.Min((long)(Page - 1) * GetPageSize(), int.MaxValue);
}
