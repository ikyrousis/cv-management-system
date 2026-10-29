namespace CVManagement.DTOs;

/// A page of items plus the total count so clients can compute page counts.
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    public int TotalCount { get; init; }
}
