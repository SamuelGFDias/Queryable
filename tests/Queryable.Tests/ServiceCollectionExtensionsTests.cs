using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Queryable.Builders;
using Queryable.Core;
using Queryable.Extensions;
using Queryable.Interfaces;

namespace Queryable.Tests;

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
}
