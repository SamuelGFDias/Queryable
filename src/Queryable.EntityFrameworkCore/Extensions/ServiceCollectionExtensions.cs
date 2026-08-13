using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Queryable.EntityFrameworkCore.Interfaces;
using Queryable.EntityFrameworkCore.Services;
using Queryable.Extensions;

namespace Queryable.EntityFrameworkCore.Extensions;

/// <summary>
/// Extensões de <see cref="IServiceCollection"/> para registrar os serviços
/// da integração Queryable.DynamicFilter.EntityFrameworkCore.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra os serviços do núcleo (via <c>AddQueryableDynamicFilter</c>) e, como
    /// serviço <c>Scoped</c>, a implementação padrão de <see cref="IPagedQueryService"/>.
    /// O registro é idempotente: chamadas repetidas não sobrescrevem registros já existentes.
    /// </summary>
    /// <param name="services">A coleção de serviços onde os serviços serão registrados.</param>
    /// <returns>A própria <paramref name="services"/>, para permitir encadeamento.</returns>
    /// <exception cref="ArgumentNullException">Quando <paramref name="services"/> é <c>null</c>.</exception>
    public static IServiceCollection AddQueryableDynamicFilterEfCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddQueryableDynamicFilter();
        services.TryAddScoped<IPagedQueryService, PagedQueryService>();

        return services;
    }
}
