using Xunit;
using Queryable.Core;
using Queryable.Extensions;

namespace Queryable.Tests;

public class PageMetaTests
{
    [Fact]
    public void TotalPages_ComDivisaoExata_CalculaCorretamente()
    {
        var meta = new PageMeta { TotalCount = 20, PageSize = 5, Page = 1 };

        Assert.Equal(4, meta.TotalPages);
    }

    [Fact]
    public void TotalPages_ComResto_ArredondaParaCima()
    {
        var meta = new PageMeta { TotalCount = 21, PageSize = 5, Page = 1 };

        Assert.Equal(5, meta.TotalPages);
    }

    [Fact]
    public void TotalPages_PageSizeZero_NaoDivideePorZeroERetornaZero()
    {
        var meta = new PageMeta { TotalCount = 100, PageSize = 0, Page = 1 };

        Assert.Equal(0, meta.TotalPages);
    }

    [Fact]
    public void TotalPages_TotalCountZero_RetornaZero()
    {
        var meta = new PageMeta { TotalCount = 0, PageSize = 10, Page = 1 };

        Assert.Equal(0, meta.TotalPages);
    }

    [Fact]
    public void HasPrevious_HasNext_NaPrimeiraPagina()
    {
        var meta = new PageMeta { TotalCount = 40, PageSize = 10, Page = 1 };

        Assert.False(meta.HasPrevious);
        Assert.True(meta.HasNext);
    }

    [Fact]
    public void HasPrevious_HasNext_NaPaginaDoMeio()
    {
        var meta = new PageMeta { TotalCount = 40, PageSize = 10, Page = 2 };

        Assert.True(meta.HasPrevious);
        Assert.True(meta.HasNext);
    }

    [Fact]
    public void HasPrevious_HasNext_NaUltimaPagina()
    {
        var meta = new PageMeta { TotalCount = 40, PageSize = 10, Page = 4 };

        Assert.True(meta.HasPrevious);
        Assert.False(meta.HasNext);
    }

    [Fact]
    public void ToPagedResult_MontaItemsEMetaCorretamente()
    {
        var items = new List<int> { 1, 2, 3 };

        PagedResult<int> resultado = items.ToPagedResult(page: 1, pageSize: 10, totalCount: 3);

        Assert.Equal(items, resultado.Items);
        Assert.Equal(1, resultado.Meta.Page);
        Assert.Equal(10, resultado.Meta.PageSize);
        Assert.Equal(3, resultado.Meta.TotalCount);
        Assert.Equal(1, resultado.Meta.TotalPages);
    }
}
