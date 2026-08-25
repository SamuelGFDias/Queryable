using System.Collections;
using System.Reflection;
using Queryable.Attributes;

namespace Queryable.Extensions;

public static class PathExtension
{
    // Profundidade máxima de aninhamento ao descer em propriedades de navegação.
    // Funciona como rede de segurança adicional às guardas de coleção e de ciclo abaixo,
    // limitando o custo de grafos de entidades muito profundos.
    private const int MaxDepth = 5;

    public static Dictionary<string, List<PropertyInfo>> BuildPropertyPaths<T>()
    {
        var result = new Dictionary<string, List<PropertyInfo>>(StringComparer.OrdinalIgnoreCase);
        // o próprio tipo raiz entra no conjunto de "tipos no caminho atual" antes de começar,
        // para que um ciclo direto para T (ex.: No.Proximo do tipo No) também seja cortado.
        BuildPathsRecursively(typeof(T), [], "", result, [typeof(T)], 0);
        return result;
    }

    /// <summary>
    /// Regra única de "é coleção" compartilhada entre a guarda de recursão daqui,
    /// <c>FilterBuilder</c> e <c>SortBuilder</c>: qualquer tipo que implemente
    /// <see cref="IEnumerable"/>, exceto <see cref="string"/>. Centralizada para os três não
    /// divergirem (issue #2).
    /// </summary>
    internal static bool IsCollectionType(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    /// <summary>
    /// Mensagem única para o uso de uma propriedade de coleção onde só um valor escalar é
    /// aceito (filtro ou ordenação). Aponta para o único recurso de coleção suportado:
    /// <c>For(x =&gt; x.{Prop}.Count)</c> via configuração fluent.
    /// </summary>
    internal static string CollectionNotScalarMessage(string alias, string uso) =>
        $"Campo '{alias}' é uma coleção e não pode ser usado em {uso}; para filtrar ou ordenar pela " +
        $"quantidade de itens, mapeie-o via configuração fluent: For(x => x.{PropertyPathOf(alias)}.Count).As(\"...\"). " +
        "Filtros sobre os itens da coleção (any/all) não são suportados.";

    private static string PropertyPathOf(string alias) =>
        string.Join('.', alias.Split('.').Select(s => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..]));

    private static void BuildPathsRecursively(
        Type type,
        List<PropertyInfo> currentPath,
        string prefix,
        Dictionary<string, List<PropertyInfo>> result,
        HashSet<Type> typesInCurrentPath,
        int depth
    )
    {
        foreach (PropertyInfo prop in type.GetProperties())
        {
            var attr = prop.GetCustomAttribute<QueryableAttribute>();
            // if (attr == null)
            //     continue;

            // alias desta propriedade
            string alias = attr?.Alias?.ToLowerInvariant() ?? prop.Name;
            // prefix calcula "department", "department.branch", "department.branch.client", ...
            string fullAlias = string.IsNullOrEmpty(prefix)
                                   ? alias
                                   : $"{prefix}.{alias}";

            // novo caminho de propriedades
            List<PropertyInfo> newPath = currentPath.Append(prop).ToList();

            // adiciona ao dicionário (a propriedade em si é sempre endereçável, mesmo
            // quando as guardas abaixo impedem descer para dentro do seu tipo)
            result[fullAlias] = newPath;

            // guarda de coleção: List<T>, ICollection<T>, arrays etc. não têm um único valor
            // endereçável - não dá para encadear Expression.Property através de uma coleção
            // para filtrar/ordenar. Descer nelas também é o vetor clássico de recursão infinita
            // do EF Core (ex.: Categoria.Produtos -> Produto.Categoria -> ...) e gera aliases
            // lixo vindos dos próprios membros da lista (Capacity, Count, Item...).
            if (IsCollectionType(prop.PropertyType))
                continue;

            // recursão: se for um tipo de navegação (classe), descer mais um nível
            // cuidado: evite descer em tipos primitivos ou ciclos
            if (prop.PropertyType.IsClass
             && prop.PropertyType != typeof(string))
            {
                // guarda de profundidade: rede de segurança extra, independente do ciclo,
                // contra grafos de entidades muito profundos.
                if (depth >= MaxDepth)
                    continue;

                // guarda de ciclo por caminho: só bloqueia reentrar em um tipo que já está
                // no caminho atual (não é um conjunto global - o mesmo tipo pode aparecer
                // legitimamente em ramos irmãos, ex.: Pedido.EnderecoEntrega e
                // Pedido.EnderecoCobranca, ambos Endereco, e os dois devem ser mapeados).
                if (!typesInCurrentPath.Add(prop.PropertyType))
                    continue;

                BuildPathsRecursively(prop.PropertyType, newPath, fullAlias, result, typesInCurrentPath, depth + 1);

                // ao voltar da recursão, o tipo sai do caminho atual
                typesInCurrentPath.Remove(prop.PropertyType);
            }
        }
    }
}