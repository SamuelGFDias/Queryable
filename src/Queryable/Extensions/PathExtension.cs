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
            bool isCollection = prop.PropertyType != typeof(string)
                             && typeof(IEnumerable).IsAssignableFrom(prop.PropertyType);

            if (isCollection)
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