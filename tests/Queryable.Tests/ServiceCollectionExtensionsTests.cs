using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Queryable.Builders;
using Queryable.Configuration;
using Queryable.Core;
using Queryable.Extensions;
using Queryable.Interfaces;

namespace Queryable.Tests;

// Configuração usada apenas por AddQueryableConfigurationsFromAssembly_EncontraERegistraConfiguracoesDoAssemblyDeTeste,
// para provar que a varredura de assembly encontra e registra configurações concretas.
public sealed class CategoriaAliasQueryConfiguration : QueryableConfiguration<Categoria>
{
    public CategoriaAliasQueryConfiguration()
    {
        For(c => c.Nome).As("apelido");
    }
}

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddQueryableDynamicFilter_RegistraOsTresServicosComoScoped()
    {
        IServiceCollection services = new ServiceCollection();

        services.AddQueryableDynamicFilter();

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IFilterBuilder)
         && d.Lifetime == ServiceLifetime.Scoped
         && d.ImplementationType == typeof(FilterBuilder));

        Assert.Contains(services, d =>
            d.ServiceType == typeof(ISortBuilder)
         && d.Lifetime == ServiceLifetime.Scoped
         && d.ImplementationType == typeof(SortBuilder));

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IQuerySpecApplier)
         && d.Lifetime == ServiceLifetime.Scoped
         && d.ImplementationType == typeof(QuerySpecApplier));
    }

    [Fact]
    public void AddQueryableDynamicFilter_RegistraIPropertyPathProviderComoSingleton()
    {
        IServiceCollection services = new ServiceCollection();

        services.AddQueryableDynamicFilter();

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IPropertyPathProvider)
         && d.Lifetime == ServiceLifetime.Singleton
         && d.ImplementationType == typeof(ReflectionPropertyPathProvider));
    }

    [Fact]
    public void AddQueryableDynamicFilter_RegistraQueryableConfigurationRegistryComoSingleton()
    {
        IServiceCollection services = new ServiceCollection();

        services.AddQueryableDynamicFilter();

        Assert.Contains(services, d =>
            d.ServiceType == typeof(QueryableConfigurationRegistry)
         && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddQueryableConfiguration_RegistraQueryableConfigurationRegistryComoSingleton()
    {
        IServiceCollection services = new ServiceCollection();

        services.AddQueryableConfiguration<CategoriaAliasQueryConfiguration>();

        Assert.Contains(services, d =>
            d.ServiceType == typeof(QueryableConfigurationRegistry)
         && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddQueryableConfigurationsFromAssembly_EncontraERegistraConfiguracoesDoAssemblyDeTeste()
    {
        IServiceCollection services = new ServiceCollection();

        services
            .AddQueryableDynamicFilter()
            .AddQueryableConfigurationsFromAssembly(typeof(ServiceCollectionExtensionsTests).Assembly);

        using ServiceProvider provider = services.BuildServiceProvider();
        IPropertyPathProvider pathProvider = provider.GetRequiredService<IPropertyPathProvider>();

        IReadOnlyDictionary<string, List<System.Reflection.PropertyInfo>> caminhos = pathProvider.GetPaths<Categoria>();

        Assert.True(caminhos.ContainsKey("apelido"));
    }

    [Fact]
    public void AddQueryableConfiguration_EAddQueryableDynamicFilter_CompartilhamAMesmaInstanciaDeRegistry_IndependenteDaOrdem()
    {
        IServiceCollection servicesConfigPrimeiro = new ServiceCollection();
        servicesConfigPrimeiro
            .AddQueryableConfiguration<CategoriaAliasQueryConfiguration>()
            .AddQueryableDynamicFilter();

        using ServiceProvider providerConfigPrimeiro = servicesConfigPrimeiro.BuildServiceProvider();
        var caminhosConfigPrimeiro = providerConfigPrimeiro
            .GetRequiredService<IPropertyPathProvider>()
            .GetPaths<Categoria>();

        Assert.True(caminhosConfigPrimeiro.ContainsKey("apelido"));

        IServiceCollection servicesFilterPrimeiro = new ServiceCollection();
        servicesFilterPrimeiro
            .AddQueryableDynamicFilter()
            .AddQueryableConfiguration<CategoriaAliasQueryConfiguration>();

        using ServiceProvider providerFilterPrimeiro = servicesFilterPrimeiro.BuildServiceProvider();
        var caminhosFilterPrimeiro = providerFilterPrimeiro
            .GetRequiredService<IPropertyPathProvider>()
            .GetPaths<Categoria>();

        Assert.True(caminhosFilterPrimeiro.ContainsKey("apelido"));
    }

    [Fact]
    public void AddQueryableConfiguration_TipoQueNaoHerdaDeQueryableConfiguration_LancaArgumentException()
    {
        IServiceCollection services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddQueryableConfiguration<TipoQueNaoEhConfiguracao>());
    }

    public sealed class TipoQueNaoEhConfiguracao
    {
    }

    [Fact]
    public void AddQueryableDynamicFilter_ServicosResolvemDeUmServiceProvider()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddQueryableDynamicFilter();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<FilterBuilder>(provider.GetService(typeof(IFilterBuilder)));
        Assert.IsType<SortBuilder>(provider.GetService(typeof(ISortBuilder)));
        Assert.IsType<QuerySpecApplier>(provider.GetService(typeof(IQuerySpecApplier)));
    }

    [Fact]
    public void AddQueryableDynamicFilter_ChamadoDuasVezes_NaoDuplicaRegistro()
    {
        IServiceCollection services = new ServiceCollection();

        services.AddQueryableDynamicFilter();
        services.AddQueryableDynamicFilter();

        Assert.Single(services, d => d.ServiceType == typeof(IFilterBuilder));
        Assert.Single(services, d => d.ServiceType == typeof(ISortBuilder));
        Assert.Single(services, d => d.ServiceType == typeof(IQuerySpecApplier));
    }

    [Fact]
    public void AddQueryableDynamicFilter_ServicesNulo_LancaArgumentNullException()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddQueryableDynamicFilter());
    }

    [Fact]
    public void AddQueryableDynamicFilter_Scoped_MesmaInstanciaDentroDoEscopo_InstanciaDiferenteEntreEscopos()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddQueryableDynamicFilter();
        using ServiceProvider provider = services.BuildServiceProvider();

        using (IServiceScope escopo1 = provider.CreateScope())
        {
            IFilterBuilder instancia1 = escopo1.ServiceProvider.GetRequiredService<IFilterBuilder>();
            IFilterBuilder instancia2 = escopo1.ServiceProvider.GetRequiredService<IFilterBuilder>();

            Assert.Same(instancia1, instancia2);
        }

        using IServiceScope escopo2 = provider.CreateScope();
        using IServiceScope escopo3 = provider.CreateScope();

        IFilterBuilder instanciaEscopo2 = escopo2.ServiceProvider.GetRequiredService<IFilterBuilder>();
        IFilterBuilder instanciaEscopo3 = escopo3.ServiceProvider.GetRequiredService<IFilterBuilder>();

        Assert.NotSame(instanciaEscopo2, instanciaEscopo3);
    }

    [Fact]
    public void AddQueryableDynamicFilter_Singleton_MesmaInstanciaEntreEscoposDiferentes()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddQueryableDynamicFilter();
        using ServiceProvider provider = services.BuildServiceProvider();

        using IServiceScope escopo1 = provider.CreateScope();
        using IServiceScope escopo2 = provider.CreateScope();

        IPropertyPathProvider instanciaEscopo1 = escopo1.ServiceProvider.GetRequiredService<IPropertyPathProvider>();
        IPropertyPathProvider instanciaEscopo2 = escopo2.ServiceProvider.GetRequiredService<IPropertyPathProvider>();

        Assert.Same(instanciaEscopo1, instanciaEscopo2);
    }
}
