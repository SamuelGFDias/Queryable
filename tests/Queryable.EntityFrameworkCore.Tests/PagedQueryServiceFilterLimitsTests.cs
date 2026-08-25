using global::Microsoft.Data.Sqlite;
using global::Microsoft.EntityFrameworkCore;
using global::Microsoft.Extensions.DependencyInjection;
using Queryable.Core;
using Queryable.EntityFrameworkCore.Extensions;
using Queryable.EntityFrameworkCore.Interfaces;
using Queryable.EntityFrameworkCore.Services;
using Queryable.Extensions;
using Queryable.Filtering;
using Queryable.Builders;
using Queryable.Interfaces;
using Xunit;

namespace Queryable.EntityFrameworkCore.Tests;

/// <summary>
/// Ponte DI → <see cref="PagedQueryService"/> → <see cref="FilterLimits"/>: o serviço resolvido
/// do container tem que validar a árvore de filtro contra os limites configurados por
/// <c>AddQueryableDynamicFilter(Action&lt;FilterLimits&gt;)</c>, não contra
/// <see cref="FilterLimits.Default"/> (issue #6).
/// </summary>
public sealed class PagedQueryServiceFilterLimitsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public PagedQueryServiceFilterLimitsTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));
        services
            .AddQueryableDynamicFilter(limits => limits.MaxNodes = 5)
            .AddQueryableDynamicFilterEfCore();

        _provider = services.BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreated();
    }

    private static string ExpressaoComCondicoes(int quantidade) =>
        string.Join(" or ", Enumerable.Range(1, quantidade).Select(i => $"preco={i}"));

    [Fact]
    public async Task ServicoResolvidoDoDI_ArvoreAcimaDoLimiteConfiguradoMasAbaixoDoDefault_Lanca()
    {
        using IServiceScope scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPagedQueryService>();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // 10 condições + 1 grupo = 11 nós: acima de MaxNodes=5, bem abaixo do default (100).
        var request = new RequestQuery { FilterExpression = ExpressaoComCondicoes(10) };

        var ex = await Assert.ThrowsAsync<FilterLimitExceededException>(() =>
            service.ApplyFilterPaginatedAsync(context.Produtos, request, p => new ProdutoDto { Id = p.Id }));

        Assert.Equal(FilterLimitKind.MaxNodes, ex.Limit);
        Assert.Equal(5, ex.Allowed);
    }

    [Fact]
    public async Task ServicoResolvidoDoDI_ArvoreDentroDoLimiteConfigurado_NaoLanca()
    {
        using IServiceScope scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPagedQueryService>();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // 3 condições + 1 grupo = 4 nós.
        var request = new RequestQuery { FilterExpression = ExpressaoComCondicoes(3) };

        PagedResult<ProdutoDto> result = await service.ApplyFilterPaginatedAsync(
            context.Produtos, request, p => new ProdutoDto { Id = p.Id });

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task InstanciadoSemLimits_UsaFilterLimitsDefault()
    {
        using IServiceScope scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var service = new PagedQueryService(new QuerySpecApplier(new FilterBuilder(), new SortBuilder()));

        // 10 nós passa no default (100) mesmo com MaxNodes=5 registrado no container.
        var request = new RequestQuery { FilterExpression = ExpressaoComCondicoes(10) };

        PagedResult<ProdutoDto> result = await service.ApplyFilterPaginatedAsync(
            context.Produtos, request, p => new ProdutoDto { Id = p.Id });

        Assert.Empty(result.Items);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Close();
        _connection.Dispose();
    }
}

/// <summary>
/// Etapa 7: <see cref="FilterLimits.MaxPageSize"/> via DI chega ao serviço, e os limites do DI
/// chegam à porta JSON quando registrados nas <see cref="System.Text.Json.JsonSerializerOptions"/>
/// via <see cref="JsonSerializerOptionsExtensions.AddQueryableFilterLimits"/>.
/// </summary>
public sealed class PagedQueryServiceEtapa7LimitsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public PagedQueryServiceEtapa7LimitsTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));
        services
            .AddQueryableDynamicFilter(limits => { limits.MaxPageSize = 50; limits.MaxNodes = 5; })
            .AddQueryableDynamicFilterEfCore();

        _provider = services.BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task ServicoResolvidoDoDI_PageSizeAcimaDoMaxPageSizeConfigurado_Lanca()
    {
        using IServiceScope scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPagedQueryService>();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var request = new RequestQuery { PageSize = 60 };

        var ex = await Assert.ThrowsAsync<FilterLimitExceededException>(() =>
            service.ApplyFilterPaginatedAsync(context.Produtos, request, p => new ProdutoDto { Id = p.Id }));

        Assert.Equal(FilterLimitKind.MaxPageSize, ex.Limit);
    }

    [Fact]
    public void FilterLimitsDoDI_RegistradosNasJsonSerializerOptions_ChegamAoConverter()
    {
        var limits = _provider.GetRequiredService<FilterLimits>();
        var options = new System.Text.Json.JsonSerializerOptions().AddQueryableFilterLimits(limits);

        // 10 condições + grupo = 11 nós: acima de MaxNodes=5 do DI, abaixo do default (100).
        string children = string.Join(",", Enumerable.Range(0, 10).Select(i => $$"""{ "field": "nome", "value": "{{i}}" }"""));
        string json = $$"""{ "logic": "or", "children": [{{children}}] }""";

        Assert.Throws<FilterLimitExceededException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<FilterNode>(json, options));
        Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<FilterNode>(json)); // sem registro: defaults
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Close();
        _connection.Dispose();
    }
}
