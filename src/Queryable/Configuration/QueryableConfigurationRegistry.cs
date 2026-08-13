using System.Collections.Concurrent;
using System.Reflection;

namespace Queryable.Configuration;

/// <summary>
/// Guarda, por <see cref="Type"/> de entidade, o resultado de uma <see cref="QueryableConfiguration{TEntity}"/>
/// executada — os aliases configurados, os caminhos ignorados e o flag <c>OnlyMapped</c>. Consumido
/// por <c>ReflectionPropertyPathProvider</c> para compor o mapa final de caminhos de cada tipo.
/// </summary>
/// <remarks>
/// Registrado como <c>Singleton</c> no container de DI (ver <c>ServiceCollectionExtensions</c>),
/// pela mesma razão do provider: a configuração é decidida na inicialização da aplicação e não
/// muda em tempo de execução, então não há necessidade de escopo por requisição.
/// </remarks>
public sealed class QueryableConfigurationRegistry
{
    private readonly ConcurrentDictionary<Type, QueryableTypeConfiguration> _configuracoes = new();

    /// <summary>
    /// Registra uma instância de <see cref="QueryableConfiguration{TEntity}"/> já construída
    /// (os aliases já foram declarados no construtor dela). Chamar novamente para o mesmo
    /// <typeparamref name="TEntity"/> substitui a configuração anterior.
    /// </summary>
    public void Register<TEntity>(QueryableConfiguration<TEntity> configuration) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(configuration);
        RegistrarFonte(configuration);
    }

    /// <summary>
    /// Varre <paramref name="assembly"/> em busca de tipos concretos (não abstratos) que herdem de
    /// <see cref="QueryableConfiguration{TEntity}"/>, instancia cada um via construtor sem
    /// parâmetros e registra o resultado.
    /// </summary>
    public void RegisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        IEnumerable<Type> tipos = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true, IsGenericTypeDefinition: false }
                     && HeredaDeQueryableConfiguration(t));

        foreach (Type tipo in tipos)
        {
            var instancia = (IQueryableConfigurationSource)Activator.CreateInstance(tipo)!;
            RegistrarFonte(instancia);
        }
    }

    /// <summary>Devolve a configuração acumulada para <paramref name="entityType"/>, se existir.</summary>
    internal QueryableTypeConfiguration? GetConfiguration(Type entityType) =>
        _configuracoes.TryGetValue(entityType, out QueryableTypeConfiguration? configuracao) ? configuracao : null;

    internal void RegistrarFonte(IQueryableConfigurationSource fonte) =>
        _configuracoes[fonte.EntityType] = fonte.Build();

    private static bool HeredaDeQueryableConfiguration(Type tipo)
    {
        Type? atual = tipo.BaseType;

        while (atual is not null)
        {
            if (atual.IsGenericType && atual.GetGenericTypeDefinition() == typeof(QueryableConfiguration<>))
                return true;

            atual = atual.BaseType;
        }

        return false;
    }
}
