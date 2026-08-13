using System.Collections.Concurrent;
using System.Reflection;
using Queryable.Interfaces;

namespace Queryable.Extensions;

/// <summary>
/// Implementação padrão, só-reflexão, de <see cref="IPropertyPathProvider"/>. Delega a
/// construção do mapa de caminhos para <see cref="PathExtension.BuildPropertyPaths{T}"/> e
/// cacheia o resultado por <see cref="Type"/>, para que <c>FilterBuilder</c> e <c>SortBuilder</c>
/// não repitam a mesma varredura recursiva por reflexão quando usados juntos na mesma
/// requisição (ex.: filtro + ordenação sobre o mesmo tipo).
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe: o cache é um <see cref="ConcurrentDictionary{TKey,TValue}"/> e a resolução usa
/// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,System.Func{TKey,TValue})"/>, então
/// chamadas concorrentes para o mesmo tipo sempre observam a mesma instância do mapa.
/// </para>
/// <para>
/// O dicionário devolvido por <see cref="PathExtension.BuildPropertyPaths{T}"/> é armazenado tal
/// como foi construído (comparador <c>StringComparer.OrdinalIgnoreCase</c>) e apenas exposto
/// através da interface <see cref="IReadOnlyDictionary{TKey,TValue}"/> — não é recriado com um
/// novo dicionário, o que preservaria o comparador padrão e quebraria a busca case-insensitive
/// de alias.
/// </para>
/// <para>
/// <b>Sem invalidação de cache, de propósito.</b> O mapa de caminhos é derivado apenas do
/// <see cref="Type"/> de <c>T</c>, que não muda em tempo de execução — não existe cenário em que
/// a mesma entrada de cache precise ser recalculada durante a vida do processo, então não há
/// expiração, evento de invalidação nem qualquer mecanismo além do que
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> já resolve para a escrita concorrente inicial.
/// </para>
/// </remarks>
public sealed class ReflectionPropertyPathProvider : IPropertyPathProvider
{
    private readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, List<PropertyInfo>>> _cache = new();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, List<PropertyInfo>> GetPaths<T>() =>
        _cache.GetOrAdd(typeof(T), static _ => PathExtension.BuildPropertyPaths<T>());
}
