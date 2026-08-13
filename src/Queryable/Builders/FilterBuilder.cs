using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Queryable.Extensions;
using Queryable.Interfaces;

namespace Queryable.Builders
{
    public class FilterBuilder : IFilterBuilder
    {
        // Instância padrão compartilhada, usada pelo construtor sem parâmetros, para que o
        // cache do provider também valha para consumidores que instanciam FilterBuilder
        // diretamente (fora do container de DI).
        private static readonly IPropertyPathProvider DefaultPathProvider = new ReflectionPropertyPathProvider();

        private static readonly string[] SupportedOperators = ["eq", "gt", "lt", "gte", "lte", "contains", "in", "neq"];

        private readonly IPropertyPathProvider _pathProvider;

        public FilterBuilder() : this(DefaultPathProvider)
        {
        }

        public FilterBuilder(IPropertyPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            _pathProvider = pathProvider;
        }

        public Expression<Func<T, bool>> BuildPredicate<T>(IDictionary<string, string> queryParams)
        {
            ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
            Expression? finalExpr = null;

            // Mapeia alias para cadeia de propriedades (path)
            IReadOnlyDictionary<string, List<PropertyInfo>> properties = _pathProvider.GetPaths<T>();

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


        private static (string field, string op) ParseKey(string rawKey)
        {
            foreach (string op in SupportedOperators.OrderByDescending(o => o.Length))
            {
                if (rawKey.EndsWith($"__{op}", StringComparison.OrdinalIgnoreCase))
                    return (rawKey[..^$"__{op}".Length], op);
            }

            return (rawKey.ToLowerInvariant(), "eq");
        }

        /// <summary>
        /// Converte uma única string para o valor tipado correspondente ao tipo alvo.
        /// Lógica escalar compartilhada por <see cref="ConvertValue"/> e <see cref="BuildInExpression"/>.
        /// </summary>
        private static object? ConvertScalar(string value, Type targetType)
        {
            Type baseType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
                return null;

            return baseType switch
            {
                _ when baseType == typeof(Guid)     => Guid.Parse(value),
                _ when baseType.IsEnum              => Enum.Parse(baseType, value, true),
                _ when baseType == typeof(DateOnly) => DateOnly.Parse(value, CultureInfo.InvariantCulture),
                _ when baseType == typeof(TimeOnly) => TimeOnly.Parse(value, CultureInfo.InvariantCulture),
                _                                   => Convert.ChangeType(value, baseType, CultureInfo.InvariantCulture)
            };
        }

        private static Expression ConvertValue(string value, PropertyInfo property)
        {
            Type targetType = property.PropertyType;
            object? converted = ConvertScalar(value, targetType);

            return Expression.Constant(converted, targetType);
        }

        private static Expression BuildInExpression(Expression member, string csv, PropertyInfo property)
        {
            Type itemType = property.PropertyType;
            object?[] convertedValues = csv.Split(',')
                                           .Select(v => v.Trim())
                                           .Where(v => v.Length > 0)
                                           .Select(v => ConvertScalar(v, itemType))
                                           .ToArray();

            if (convertedValues.Length == 0)
                throw new ArgumentException($"Nenhum valor válido informado para o operador 'in' da propriedade '{property.Name}'.");

            var typedArray = Array.CreateInstance(itemType, convertedValues.Length);
            for (int i = 0; i < convertedValues.Length; i++)
                typedArray.SetValue(convertedValues[i], i);

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