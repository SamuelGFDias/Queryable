using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Queryable.Builders;
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
    /// <see cref="IFilterBuilder"/>, <see cref="ISortBuilder"/> e <see cref="IQuerySpecApplier"/>.
    /// O registro é idempotente: chamadas repetidas não sobrescrevem registros já existentes.
    /// </summary>
    /// <param name="services">A coleção de serviços onde os serviços serão registrados.</param>
    /// <returns>A própria <paramref name="services"/>, para permitir encadeamento.</returns>
    /// <exception cref="ArgumentNullException">Quando <paramref name="services"/> é <c>null</c>.</exception>
    public static IServiceCollection AddQueryableDynamicFilter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Singleton de propósito: o mapa de caminhos resolvido por IPropertyPathProvider
        // depende só do Type de T, que não muda em tempo de execução. Registrar como Scoped
        // recriaria o cache a cada requisição, anulando o ganho de performance que o provider
        // existe para trazer — o cache só compensa quando é compartilhado entre requisições.
        services.TryAddSingleton<IPropertyPathProvider, ReflectionPropertyPathProvider>();

        services.TryAddScoped<IFilterBuilder, FilterBuilder>();
        services.TryAddScoped<ISortBuilder, SortBuilder>();
        services.TryAddScoped<IQuerySpecApplier, QuerySpecApplier>();

        return services;
    }
}
