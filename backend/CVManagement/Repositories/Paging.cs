namespace CVManagement.Repositories;

/// <summary>Shared Skip/Take slicing; no params means no paging.</summary>
internal static class Paging
{
    /// <summary>page defaults to 1 when only pageSize is given; pageSize defaults to 10 when only page is given.</summary>
    public static IQueryable<T> Apply<T>(IQueryable<T> query, int? page, int? pageSize)
    {
        if (page is null && pageSize is null)
        {
            return query;
        }

        var size = pageSize ?? 10;
        var number = page ?? 1;

        return query.Skip((number - 1) * size).Take(size);
    }
}
