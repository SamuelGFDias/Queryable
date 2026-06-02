using System.Reflection;
using Queryable.Attributes;

namespace Queryable.Extensions;

public static class PathExtension
{
    public static Dictionary<string, List<PropertyInfo>> BuildPropertyPaths<T>()
    {
        var result = new Dictionary<string, List<PropertyInfo>>(StringComparer.OrdinalIgnoreCase);
        BuildPathsRecursively(typeof(T), [], "", result);
        return result;
    }

    private static void BuildPathsRecursively(
        Type type,
        List<PropertyInfo> currentPath,
        string prefix,
        Dictionary<string, List<PropertyInfo>> result
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

            // adiciona ao dicionário
            result[fullAlias] = newPath;

            // recursão: se for um tipo de navegação (classe), descer mais um nível
            // cuidado: evite descer em tipos primitivos ou ciclos
            if (prop.PropertyType.IsClass
             && prop.PropertyType != typeof(string))
            {
                BuildPathsRecursively(prop.PropertyType, newPath, fullAlias, result);
            }
        }
    }
}