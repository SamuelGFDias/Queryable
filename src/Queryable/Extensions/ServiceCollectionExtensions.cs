using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Queryable.Builders;
using Queryable.Configuration;
using Queryable.Core;
using Queryable.Filtering;
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

        // FilterLimits também é Singleton pela mesma razão: são tetos de segurança decididos na
        // inicialização da aplicação, não por requisição. Instância nova e independente de
        // FilterLimits.Default (o fallback usado por código estático sem acesso a DI, ver a
        // documentação de FilterLimits.Default) — nunca a mesma referência, para que registrar
        // aqui não tenha efeito colateral sobre esse fallback.
        services.TryAddSingleton(new FilterLimits());

        services.TryAddScoped<IFilterBuilder, FilterBuilder>();
        services.TryAddScoped<ISortBuilder, SortBuilder>();
        services.TryAddScoped<IQuerySpecApplier, QuerySpecApplier>();

        return services;
    }

    /// <summary>
    /// Igual a <see cref="AddQueryableDynamicFilter(IServiceCollection)"/>, mas permite
    /// customizar os tetos de segurança de filtro composto (<see cref="FilterLimits"/> —
    /// profundidade máxima, número de nós, tamanho da expressão da mini-linguagem, itens de
    /// <c>in</c>) aplicados por <see cref="Queryable.Filtering.FilterLimitValidator"/> antes da
    /// compilação de uma árvore de filtro vinda de cliente externo.
    /// </summary>
    /// <remarks>
    /// Registra o <see cref="FilterLimits"/> resultante como <c>Singleton</c>, resolvível por
    /// <c>QuerySpecModelBinder&lt;T&gt;.BindModelAsync</c> via
    /// <c>HttpContext.RequestServices</c>. Não afeta <see cref="FilterLimits.Default"/> — código
    /// estático sem acesso a DI (a porta JSON via <see cref="Queryable.Filtering.FilterNodeJsonConverter"/>,
    /// ou uma chamada direta a <c>FilterExpressionParser.Parse</c> fora de um binder) continua
    /// validando contra os defaults, independentemente desta configuração (ver a documentação de
    /// <see cref="FilterLimits.Default"/>).
    /// </remarks>
    /// <param name="services">A coleção de serviços onde os serviços serão registrados.</param>
    /// <param name="configure">Callback que ajusta os campos de uma <see cref="FilterLimits"/> nova, com os defaults como ponto de partida.</param>
    /// <returns>A própria <paramref name="services"/>, para permitir encadeamento.</returns>
    /// <exception cref="ArgumentNullException">Quando <paramref name="services"/> ou <paramref name="configure"/> é <c>null</c>.</exception>
    public static IServiceCollection AddQueryableDynamicFilter(
        this IServiceCollection services,
        Action<FilterLimits> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var limits = new FilterLimits();
        configure(limits);

        // Registrado antes de delegar para a sobrecarga sem parâmetros: TryAddSingleton não
        // sobrescreve um descritor já existente para o mesmo ServiceType, então esta instância
        // customizada vence a instância padrão que a sobrecarga abaixo tentaria registrar.
        services.TryAddSingleton(limits);

        return AddQueryableDynamicFilter(services);
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
