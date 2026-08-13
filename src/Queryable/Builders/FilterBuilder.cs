using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Queryable.Extensions;
using Queryable.Filtering;
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

            // Mapeia alias para cadeia de propriedades (path)
            IReadOnlyDictionary<string, List<PropertyInfo>> properties = _pathProvider.GetPaths<T>();

            FilterNode tree = ToFilterTree(queryParams);
            Expression body = Compile(tree, parameter, properties);

            return Expression.Lambda<Func<T, bool>>(body, parameter);
        }

        public Expression<Func<T, bool>> BuildPredicate<T>(FilterNode filter)
        {
            ArgumentNullException.ThrowIfNull(filter);

            ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
            IReadOnlyDictionary<string, List<PropertyInfo>> properties = _pathProvider.GetPaths<T>();

            Expression body = Compile(filter, parameter, properties);

            return Expression.Lambda<Func<T, bool>>(body, parameter);
        }

        /// <summary>
        /// Adaptador legado: traduz o dicionário <c>campo__operador=valor</c> em uma árvore
        /// <see cref="FilterGroup"/> combinada por <see cref="FilterLogic.And"/> — cada entrada
        /// do dicionário vira uma <see cref="FilterCondition"/> folha. Dicionário vazio produz
        /// um grupo AND sem filhos, que <see cref="Compile"/> resolve para um predicado sempre
        /// verdadeiro (mesmo comportamento do código anterior a esta árvore).
        /// </summary>
        private static FilterGroup ToFilterTree(IDictionary<string, string> queryParams)
        {
            List<FilterNode> children = queryParams
                .Select(kv =>
                {
                    var (field, op) = ParseKey(kv.Key);
                    return (FilterNode)new FilterCondition(field, op, kv.Value);
                })
                .ToList();

            return new FilterGroup(FilterLogic.And, children);
        }

        /// <summary>
        /// Compilador recursivo <see cref="FilterNode"/> → <see cref="Expression"/>. Ponto único
        /// de tradução para os três formatos de entrada (dicionário legado, e futuramente
        /// mini-linguagem e JSON): todos produzem uma árvore que passa por aqui.
        /// </summary>
        private static Expression Compile(
            FilterNode node,
            ParameterExpression parameter,
            IReadOnlyDictionary<string, List<PropertyInfo>> properties)
        {
            switch (node)
            {
                case FilterCondition condition:
                    return CompileCondition(condition, parameter, properties);

                case FilterNot not:
                    return Expression.Not(Compile(not.Inner, parameter, properties));

                case FilterGroup { Logic: FilterLogic.And } group:
                    // Grupo AND vazio ⇒ predicado sempre verdadeiro (mesmo comportamento do
                    // dicionário vazio no código anterior a esta árvore).
                    return group.Children.Count == 0
                        ? Expression.Constant(true)
                        : group.Children
                            .Select(child => Compile(child, parameter, properties))
                            .Aggregate(Expression.AndAlso);

                case FilterGroup { Logic: FilterLogic.Or } group:
                    // Grupo OR vazio ⇒ predicado sempre falso: nenhuma condição para ser
                    // verdadeira em uma disjunção é logicamente falso (elemento neutro de OR).
                    // Decisão de design da Etapa 2 — a proposta não define isso explicitamente.
                    return group.Children.Count == 0
                        ? Expression.Constant(false)
                        : group.Children
                            .Select(child => Compile(child, parameter, properties))
                            .Aggregate(Expression.OrElse);

                default:
                    throw new NotSupportedException($"Tipo de nó '{node.GetType().Name}' não suportado.");
            }
        }

        private static Expression CompileCondition(
            FilterCondition condition,
            ParameterExpression parameter,
            IReadOnlyDictionary<string, List<PropertyInfo>> properties)
        {
            if (!properties.TryGetValue(condition.Field, out List<PropertyInfo>? path))
                throw new ArgumentException($"Campo '{condition.Field}' não é pesquisável.");

            // Constrói MemberExpression encadeado conforme path
            Expression member = path.Aggregate<PropertyInfo, Expression>(
                parameter,
                Expression.Property);

            // Propriedade alvo para conversão e tipo
            PropertyInfo targetProp = path.Last();
            string value = condition.Value;

            return condition.Operator switch
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
                _    => throw new NotSupportedException($"Operador '{condition.Operator}' não suportado para {targetProp.Name}")
            };
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