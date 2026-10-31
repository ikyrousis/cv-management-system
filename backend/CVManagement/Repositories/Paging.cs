using CVManagement.Validation;

namespace CVManagement.Repositories;

// Shared Skip/Take slicing. Absent parameters fall back to a bounded first page rather than no limit.
internal static class Paging
{
    public static IQueryable<T> Apply<T>(IQueryable<T> query, int? page, int? pageSize)
    {
        var size = pageSize ?? PagingLimits.DefaultPageSize;
        var number = page ?? 1;

        // Long math guards against (page - 1) * size overflowing int; past the addressable range, the page is empty.
        var offset = (long)(number - 1) * size;

        return offset > int.MaxValue
            ? query.Take(0)
            : query.Skip((int)offset).Take(size);
    }
}
