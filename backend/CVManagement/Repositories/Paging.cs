namespace CVManagement.Repositories;

// Shared Skip/Take slicing; no params means no paging.
internal static class Paging
{
    // page defaults to 1 when only pageSize is given; pageSize defaults to 10 when only page is given.
    public static IQueryable<T> Apply<T>(IQueryable<T> query, int? page, int? pageSize)
    {
        if (page is null && pageSize is null)
        {
            return query;
        }

        var size = pageSize ?? 10;
        var number = page ?? 1;

        // Long math guards against (page - 1) * size overflowing int; past the addressable range, the page is empty.
        var offset = (long)(number - 1) * size;

        return offset > int.MaxValue
            ? query.Take(0)
            : query.Skip((int)offset).Take(size);
    }
}
