using CVManagement.Repositories;
using CVManagement.Validation;
using Xunit;

namespace CVManagement.Tests.Unit;

public sealed class PagingTests
{
    private static IQueryable<int> Source() => Enumerable.Range(1, 25).AsQueryable();

    private static IQueryable<int> ManyRows() => Enumerable.Range(1, 200).AsQueryable();

    [Fact]
    public void NoParameters_ReturnsABoundedFirstPageNotTheWholeTable()
    {
        var page = Paging.Apply(ManyRows(), null, null).ToList();

        Assert.Equal(PagingLimits.DefaultPageSize, page.Count);
        Assert.Equal(1, page[0]);
        Assert.NotEqual(200, page.Count);
    }

    [Fact]
    public void PageOnly_DefaultsToTheSamePageSize()
    {
        var page2 = Paging.Apply(ManyRows(), 2, null).ToList();

        Assert.Equal(PagingLimits.DefaultPageSize, page2.Count);
        Assert.Equal(PagingLimits.DefaultPageSize + 1, page2[0]);
    }

    [Fact]
    public void SizeOnly_StartsAtFirstPage()
    {
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Paging.Apply(Source(), null, 5));
    }

    [Fact]
    public void Both_SlicesAtOffset()
    {
        Assert.Equal(new[] { 9, 10, 11, 12 }, Paging.Apply(Source(), 3, 4));
    }

    [Fact]
    public void BeyondLastPage_ReturnsEmpty()
    {
        Assert.Empty(Paging.Apply(Source(), 10, 5));
    }

    [Fact]
    public void IntMaxValuePage_DoesNotOverflow_ReturnsEmpty()
    {
        Assert.Empty(Paging.Apply(Source(), int.MaxValue, 100));
    }
}
