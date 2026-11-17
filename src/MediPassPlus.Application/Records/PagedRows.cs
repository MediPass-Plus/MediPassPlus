namespace MediPassPlus.Application.Records;

/// <summary>One page of rows plus the total row count the store counted for the same filter.</summary>
/// <typeparam name="T">The row type.</typeparam>
/// <param name="Items">The rows on this page, in the store's order.</param>
/// <param name="TotalCount">The total number of rows that match the filter, not just this page.</param>
public sealed record PagedRows<T>(IReadOnlyList<T> Items, int TotalCount);
