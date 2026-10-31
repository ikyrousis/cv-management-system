namespace CVManagement.Validation;

/// Paging rules shared by the controllers, the repository slicing and the frontend defaults.
/// A list request without parameters is served a bounded first page, so no caller can ask the
/// API to hand over the whole table of protected records by omitting a query string.
public static class PagingLimits
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;
}
