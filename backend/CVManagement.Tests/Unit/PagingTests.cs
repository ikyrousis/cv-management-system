using CVManagement.Repositories;
using Xunit;

namespace CVManagement.Tests.Unit;

public sealed class PagingTests
{
    private static IQueryable<int> Source() => Enumerable.Range(1, 25).AsQueryable();

    [Fact]
    public void NoParameters_ReturnsEverything()
    {
        Assert.Equal(25, Paging.Apply(Source(), null, null).Count());
    }

    [Fact]
    public void PageOnly_DefaultsSizeToTen()
    {
        var page2 = Paging.Apply(Source(), 2, null).ToList();

        Assert.Equal(10, page2.Count);
        Assert.Equal(11, page2[0]);
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
