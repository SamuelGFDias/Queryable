using System.Collections.Concurrent;
using System.Reflection;
using Queryable.Configuration;
using Queryable.Interfaces;

namespace Queryable.Extensions;

/// <summary>
/// Implementação padrão de <see cref="IPropertyPathProvider"/>. Constrói o mapa de caminhos por
/// reflexão via <see cref="PathExtension.BuildPropertyPaths{T}"/>, aplica por cima a configuração
/// fluente registrada para o tipo (se houver, ver <see cref="QueryableConfigurationRegistry"/>) e
/// cacheia o resultado final por <see cref="Type"/>, para que <c>FilterBuilder</c> e
/// <c>SortBuilder</c> não repitam a mesma varredura recursiva por reflexão quando usados juntos na
/// mesma requisição (ex.: filtro + ordenação sobre o mesmo tipo).
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe: o cache é um <see cref="ConcurrentDictionary{TKey,TValue}"/> e a resolução usa
/// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,System.Func{TKey,TValue})"/>, então
/// chamadas concorrentes para o mesmo tipo sempre observam a mesma instância do mapa.
/// </para>
/// <para>
/// O dicionário final é sempre construído com <c>StringComparer.OrdinalIgnoreCase</c> — tanto o
/// mapa automático de <see cref="PathExtension.BuildPropertyPaths{T}"/> quanto o mapa restrito do
/// modo <c>OnlyMapped()</c> preservam a busca de alias case-insensitive.
/// </para>
/// <para>
/// <b>Sem invalidação de cache, de propósito.</b> O mapa de caminhos é derivado apenas do
/// <see cref="Type"/> de <c>T</c> e da configuração fluente registrada para ele — nenhum dos dois
/// muda em tempo de execução (a configuração fluente é decidida uma única vez, na inicialização
/// da aplicação) — não existe cenário em que a mesma entrada de cache precise ser recalculada
/// durante a vida do processo, então não há expiração, evento de invalidação nem qualquer
/// mecanismo além do que <see cref="ConcurrentDictionary{TKey,TValue}"/> já resolve para a
/// escrita concorrente inicial.
/// </para>
/// </remarks>
public sealed class ReflectionPropertyPathProvider : IPropertyPathProvider
{
    private readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, List<PropertyInfo>>> _cache = new();
    private readonly QueryableConfigurationRegistry? _registry;

    /// <summary>Cria o provider sem nenhuma configuração fluente — resolução 100% por reflexão.</summary>
    public ReflectionPropertyPathProvider() : this(null)
    {
    }

    /// <summary>
    /// Cria o provider com um <see cref="QueryableConfigurationRegistry"/> opcional. Quando
    /// <paramref name="registry"/> é <c>null</c> (ou não tem configuração para um tipo pedido), o
    /// comportamento é idêntico ao construtor sem parâmetros para aquele tipo.
    /// </summary>
    public ReflectionPropertyPathProvider(QueryableConfigurationRegistry? registry)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, List<PropertyInfo>> GetPaths<T>() =>
        _cache.GetOrAdd(typeof(T), _ => BuildFinalMap<T>());

    private IReadOnlyDictionary<string, List<PropertyInfo>> BuildFinalMap<T>()
    {
        Dictionary<string, List<PropertyInfo>> automatico = PathExtension.BuildPropertyPaths<T>();

        QueryableTypeConfiguration? config = _registry?.GetConfiguration(typeof(T));
        if (config is null)
            return automatico;

        RemoverCaminhosIgnorados(automatico, config.IgnoredPaths);

        if (config.OnlyMapped)
        {
            var somenteMapeado = new Dictionary<string, List<PropertyInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach ((string alias, List<PropertyInfo> caminho) in config.MappedAliases)
                somenteMapeado[alias] = caminho;

            return somenteMapeado;
        }

        foreach ((string alias, List<PropertyInfo> caminho) in config.MappedAliases)
            automatico[alias] = caminho;

        return automatico;
    }

    /// <summary>
    /// Remove do mapa automático qualquer entrada cujo caminho corresponda estruturalmente a um
    /// dos caminhos marcados via <c>Ignore(...)</c>. A comparação é pelo caminho de
    /// <see cref="PropertyInfo"/> (declarante + nome de cada segmento), não pelo texto do alias,
    /// porque o alias automático pode vir de <see cref="Queryable.Attributes.QueryableAttribute"/>
    /// e não coincidir com o nome literal da propriedade usada em <c>Ignore</c>.
    /// </summary>
    private static void RemoverCaminhosIgnorados(
        Dictionary<string, List<PropertyInfo>> automatico,
        IReadOnlyList<List<PropertyInfo>> caminhosIgnorados)
    {
        if (caminhosIgnorados.Count == 0)
            return;

        List<string> chavesParaRemover = automatico
            .Where(par => caminhosIgnorados.Any(ignorado => CaminhoEquivalente(ignorado, par.Value)))
            .Select(par => par.Key)
            .ToList();

        foreach (string chave in chavesParaRemover)
            automatico.Remove(chave);
    }

    private static bool CaminhoEquivalente(List<PropertyInfo> a, List<PropertyInfo> b)
    {
        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].DeclaringType != b[i].DeclaringType || a[i].Name != b[i].Name)
                return false;
        }

        return true;
    }
}
