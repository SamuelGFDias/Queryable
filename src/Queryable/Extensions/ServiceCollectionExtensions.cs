using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Queryable.Builders;
using Queryable.Configuration;
using Queryable.Core;
using Queryable.Interfaces;

namespace Queryable.Extensions;

/// <summary>
/// Extensões de <see cref="IServiceCollection"/> para registrar os serviços
/// da biblioteca Queryable.DynamicFilter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra, como serviços <c>Scoped</c>, as implementações padrão de
    /// <see cref="IFilterBuilder"/>, <see cref="ISortBuilder"/> e <see cref="IQuerySpecApplier"/>,
    /// além de <see cref="IPropertyPathProvider"/> e <see cref="QueryableConfigurationRegistry"/>
    /// como <c>Singleton</c>. O registro é idempotente: chamadas repetidas não sobrescrevem
    /// registros já existentes.
    /// </summary>
    /// <remarks>
    /// Se alguma configuração fluente for registrada (<see cref="AddQueryableConfiguration{TConfiguration}"/>
    /// ou <see cref="AddQueryableConfigurationsFromAssembly"/>), registre-a <b>antes</b> do
    /// primeiro uso de <see cref="IPropertyPathProvider"/>/<see cref="IFilterBuilder"/>/
    /// <see cref="ISortBuilder"/> — o provider cacheia o mapa de caminhos por tipo na primeira
    /// resolução, então configurar um tipo depois de já ter sido consultado não tem efeito. A
    /// própria chamada a este método pode ocorrer antes ou depois das extensões de configuração;
    /// o que importa é a ordem relativa ao primeiro uso, não ao registro no <c>IServiceCollection</c>.
    /// </remarks>
    /// <param name="services">A coleção de serviços onde os serviços serão registrados.</param>
    /// <returns>A própria <paramref name="services"/>, para permitir encadeamento.</returns>
    /// <exception cref="ArgumentNullException">Quando <paramref name="services"/> é <c>null</c>.</exception>
    public static IServiceCollection AddQueryableDynamicFilter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Singleton de propósito: o mapa de caminhos resolvido por IPropertyPathProvider
        // depende só do Type de T (e da configuração fluente registrada para ele, que também não
        // muda em tempo de execução). Registrar como Scoped recriaria o cache a cada requisição,
        // anulando o ganho de performance que o provider existe para trazer — o cache só
        // compensa quando é compartilhado entre requisições.
        ObterOuCriarRegistry(services);
        services.TryAddSingleton<IPropertyPathProvider, ReflectionPropertyPathProvider>();

        services.TryAddScoped<IFilterBuilder, FilterBuilder>();
        services.TryAddScoped<ISortBuilder, SortBuilder>();
        services.TryAddScoped<IQuerySpecApplier, QuerySpecApplier>();

        return services;
    }

    /// <summary>
    /// Registra uma configuração fluente de mapeamento (<typeparamref name="TConfiguration"/>,
    /// uma classe derivada de <c>QueryableConfiguration&lt;TEntity&gt;</c> com construtor sem
    /// parâmetros) no <see cref="QueryableConfigurationRegistry"/> compartilhado da aplicação.
    /// Encadeável com <see cref="AddQueryableDynamicFilter"/>, em qualquer ordem.
    /// </summary>
    /// <remarks>
    /// Registre antes do primeiro uso do provider — ver observação em
    /// <see cref="AddQueryableDynamicFilter"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Quando <paramref name="services"/> é <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// Quando <typeparamref name="TConfiguration"/> não herda de
    /// <c>QueryableConfiguration&lt;TEntity&gt;</c>.
    /// </exception>
    public static IServiceCollection AddQueryableConfiguration<TConfiguration>(this IServiceCollection services)
        where TConfiguration : class, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        if (new TConfiguration() is not IQueryableConfigurationSource fonte)
            throw new ArgumentException(
                $"'{typeof(TConfiguration).Name}' precisa herdar de QueryableConfiguration<TEntity>.",
                nameof(TConfiguration));

        QueryableConfigurationRegistry registry = ObterOuCriarRegistry(services);
        registry.RegistrarFonte(fonte);

        return services;
    }

    /// <summary>
    /// Varre <paramref name="assembly"/> em busca de tipos concretos que herdem de
    /// <c>QueryableConfiguration&lt;TEntity&gt;</c> e registra cada um no
    /// <see cref="QueryableConfigurationRegistry"/> compartilhado da aplicação. Encadeável com
    /// <see cref="AddQueryableDynamicFilter"/>, em qualquer ordem.
    /// </summary>
    /// <remarks>
    /// Registre antes do primeiro uso do provider — ver observação em
    /// <see cref="AddQueryableDynamicFilter"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Quando <paramref name="services"/> ou <paramref name="assembly"/> é <c>null</c>.
    /// </exception>
    public static IServiceCollection AddQueryableConfigurationsFromAssembly(
        this IServiceCollection services,
        Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        QueryableConfigurationRegistry registry = ObterOuCriarRegistry(services);
        registry.RegisterAssembly(assembly);

        return services;
    }

    /// <summary>
    /// Devolve a instância de <see cref="QueryableConfigurationRegistry"/> já registrada em
    /// <paramref name="services"/> (como instância de singleton), ou cria e registra uma nova.
    /// Garante que <c>AddQueryableDynamicFilter</c>, <c>AddQueryableConfiguration</c> e
    /// <c>AddQueryableConfigurationsFromAssembly</c> compartilhem sempre a mesma instância,
    /// independentemente da ordem em que forem chamados.
    /// </summary>
    private static QueryableConfigurationRegistry ObterOuCriarRegistry(IServiceCollection services)
    {
        ServiceDescriptor? existente = services.FirstOrDefault(d =>
            d.ServiceType == typeof(QueryableConfigurationRegistry)
         && d.ImplementationInstance is QueryableConfigurationRegistry);

        if (existente is not null)
            return (QueryableConfigurationRegistry)existente.ImplementationInstance!;

        var registry = new QueryableConfigurationRegistry();
        services.AddSingleton(registry);

        return registry;
    }
}
