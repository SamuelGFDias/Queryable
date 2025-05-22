using System.Linq.Expressions;
using System.Reflection;
using Queryable.Attributes;
using Queryable.Interfaces;

namespace Queryable.Builders
{
    public class FilterBuilder : IFilterBuilder
    {
        private static readonly string[] SupportedOperators = ["eq", "gt", "lt", "gte", "lte", "contains", "in", "neq"];

        public Expression<Func<T, bool>> BuildPredicate<T>(IDictionary<string, string> queryParams)
        {
            ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
            Expression? finalExpr = null;

            // Mapeia alias para cadeia de propriedades (path)
            Dictionary<string, List<PropertyInfo>> properties = BuildPropertyPaths<T>();

            foreach (var (rawKey, value) in queryParams)
            {
                var (propKey, op) = ParseKey(rawKey);

                if (!properties.TryGetValue(propKey, out List<PropertyInfo>? path))
                    throw new ArgumentException($"Campo '{propKey}' não é pesquisável.");

                // Constrói MemberExpression encadeado conforme path
                Expression member = path.Aggregate<PropertyInfo, Expression>(
                    parameter,
                    Expression.Property);

                // Propriedade alvo para conversão e tipo
                PropertyInfo targetProp = path.Last();
                Expression condition = op switch
                {
                    "eq"  => Expression.Equal(member, ConvertValue(value, targetProp)),
                    "neq" => Expression.NotEqual(member, ConvertValue(value, targetProp)),
                    "gt"  => Expression.GreaterThan(member, ConvertValue(value, targetProp)),
                    "lt"  => Expression.LessThan(member, ConvertValue(value, targetProp)),
                    "gte" => Expression.GreaterThanOrEqual(member, ConvertValue(value, targetProp)),
                    "lte" => Expression.LessThanOrEqual(member, ConvertValue(value, targetProp)),
                    "contains" when targetProp.PropertyType == typeof(string)
                        => Expression.Call(member, nameof(string.Contains), null, ConvertValue(value, targetProp)),
                    "in" => BuildInExpression(member, value, targetProp),
                    _    => throw new NotSupportedException($"Operador '{op}' não suportado para {targetProp.Name}")
                };

                finalExpr = finalExpr == null
                                ? condition
                                : Expression.AndAlso(finalExpr, condition);
            }

            return finalExpr == null
                       ? x => true
                       : Expression.Lambda<Func<T, bool>>(finalExpr, parameter);
        }

        // Constroi o dicionário de caminhos de propriedades suportadas
        private static Dictionary<string, List<PropertyInfo>> BuildPropertyPaths<T>()
        {
            var result = new Dictionary<string, List<PropertyInfo>>(StringComparer.OrdinalIgnoreCase);
            Type type = typeof(T);

            // Propriedades diretas anotadas
            foreach (PropertyInfo prop in type.GetProperties())
            {
                var attr = prop.GetCustomAttribute<QueryableAttribute>();
                if (attr == null) continue;
                string alias = attr.Alias?.ToLowerInvariant() ?? prop.Name.ToLowerInvariant();
                result[alias] = [prop];
            }

            // Propriedades aninhadas em classes com membros anotados
            foreach (PropertyInfo nav in type.GetProperties())
            {
                var navAttr = nav.GetCustomAttribute<QueryableAttribute>();
                if (navAttr == null)
                    continue;

                string navAlias = (navAttr.Alias ?? nav.Name).ToLowerInvariant();

                foreach (PropertyInfo prop in nav.PropertyType.GetProperties())
                {
                    var subAttr = prop.GetCustomAttribute<QueryableAttribute>();
                    if (subAttr == null)
                        continue;

                    string subAlias = (subAttr.Alias ?? prop.Name).ToLowerInvariant();
                    string fullAlias = $"{navAlias}.{subAlias}";

                    result[fullAlias] = [nav, prop];
                }
            }


            return result;
        }

        private static (string field, string op) ParseKey(string rawKey)
        {
            foreach (string op in SupportedOperators.OrderByDescending(o => o.Length))
            {
                if (rawKey.EndsWith($"__{op}", StringComparison.OrdinalIgnoreCase))
                    return (rawKey[..^$"__{op}".Length].ToLowerInvariant(), op);
            }

            return (rawKey.ToLowerInvariant(), "eq");
        }

        private static Expression ConvertValue(string value, PropertyInfo property)
        {
            Type targetType = property.PropertyType;
            Type baseType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
                return Expression.Constant(null, targetType);

            object converted = baseType switch
            {
                _ when baseType == typeof(Guid)     => Guid.Parse(value),
                _ when baseType.IsEnum              => Enum.Parse(baseType, value, true),
                _ when baseType == typeof(DateOnly) => DateOnly.Parse(value),
                _ when baseType == typeof(TimeOnly) => TimeOnly.Parse(value),
                _                                   => Convert.ChangeType(value, baseType)
            };

            return Expression.Constant(converted, targetType);
        }

        private static Expression BuildInExpression(Expression member, string csv, PropertyInfo property)
        {
            Type itemType = property.PropertyType;
            object[] convertedValues = csv.Split(',')
                                          .Select(v => Convert.ChangeType(v.Trim(), itemType))
                                          .ToArray();

            var typedArray = Array.CreateInstance(itemType, convertedValues.Length);
            convertedValues.CopyTo(typedArray, 0);

            MethodInfo containsMethod = typeof(Enumerable)
                                       .GetMethods(BindingFlags.Static | BindingFlags.Public)
                                       .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
                                       .MakeGenericMethod(itemType);

            ConstantExpression arrayExpr = Expression.Constant(
                typedArray,
                typeof(IEnumerable<>).MakeGenericType(itemType)
            );

            return Expression.Call(containsMethod, arrayExpr, member);
        }
    }
}