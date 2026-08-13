using global::Microsoft.Data.Sqlite;
using global::Microsoft.EntityFrameworkCore;
using global::Microsoft.Extensions.DependencyInjection;
using Queryable.Core;
using Queryable.EntityFrameworkCore.Extensions;
using Queryable.EntityFrameworkCore.Interfaces;
using Xunit;

namespace Queryable.EntityFrameworkCore.Tests;

/// <summary>
/// 12. AddQueryableDynamicFilterEfCore() registra IPagedQueryService e ele resolve do
/// ServiceProvider — end-to-end via DI, contra SQLite in-memory de verdade.
/// </summary>
public sealed class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public ServiceCollectionExtensionsTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));
        services.AddQueryableDynamicFilterEfCore();

        _provider = services.BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        context.Database.EnsureCreated();

        var categoria = new Categoria { Id = 1, Nome = "Perifericos" };
        context.Categorias.Add(categoria);
        context.Produtos.Add(new Produto
        {
            Id = Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
            Nome = "Mouse Gamer",
            Preco = 150m,
            Ativo = true,
            CategoriaId = categoria.Id,
            Categoria = categoria
        });
        context.SaveChanges();
    }

    [Fact]
    public void AddQueryableDynamicFilterEfCore_RegistraIPagedQueryService()
    {
        using IServiceScope scope = _provider.CreateScope();

        IPagedQueryService? service = scope.ServiceProvider.GetService<IPagedQueryService>();

        Assert.NotNull(service);
    }

    [Fact]
    public async Task AddQueryableDynamicFilterEfCore_ServicoResolvidoFuncionaContraOBanco()
    {
        using IServiceScope scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPagedQueryService>();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var request = new RequestQuery { QueryFilter = "ativo=true", PageSize = 10 };

        PagedResult<ProdutoDto> result = await service.ApplyFilterPaginatedAsync(
            context.Produtos,
            request,
            produto => new ProdutoDto
            {
                Id = produto.Id,
                Nome = produto.Nome,
                Preco = produto.Preco,
                CategoriaNome = produto.Categoria.Nome
            });

        ProdutoDto item = Assert.Single(result.Items);
        Assert.Equal("Mouse Gamer", item.Nome);
        Assert.Equal("Perifericos", item.CategoriaNome);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Close();
        _connection.Dispose();
    }
}
