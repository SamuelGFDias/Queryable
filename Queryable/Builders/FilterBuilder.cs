using System.Linq.Expressions;
using System.Reflection;
using Queryable.Attributes;
using Queryable.Interfaces;

namespace Queryable.Builders;

public class FilterBuilder : IFilterBuilder
{
    private static readonly string[] SupportedOperators = ["eq", "gt", "lt", "gte", "lte", "contains", "in", "neq"];

    public Expression<Func<T, bool>> BuildPredicate<T>(IDictionary<string, string> queryParams)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
        Expression? finalExpr = null;

        Dictionary<string, PropertyInfo> properties = typeof(T)
                                                     .GetProperties()
                                                     .Where(p => Attribute.IsDefined(p, typeof(QueryableAttribute)))
                                                     .Select(p => new
                                                      {
                                                          Property = p,
                                                          Alias = p.GetCustomAttribute<QueryableAttribute>()
                                                                  ?.Alias?.ToLowerInvariant()
                                                               ?? p.Name.ToLowerInvariant()
                                                      })
                                                     .ToDictionary(x => x.Alias, x => x.Property);

        foreach (var (rawKey, value) in queryParams)
        {
            var (propKey, op) = ParseKey(rawKey);

            if (!properties.TryGetValue(propKey, out PropertyInfo? property))
                throw new ArgumentException($"Campo '{rawKey}' não é pesquisável.");

            MemberExpression member = Expression.Property(parameter, property);
            Expression condition = op switch
            {
                "eq"  => Expression.Equal(member, ConvertValue(value, property)),
                "neq" => Expression.NotEqual(member, ConvertValue(value, property)),
                "gt"  => Expression.GreaterThan(member, ConvertValue(value, property)),
                "lt"  => Expression.LessThan(member, ConvertValue(value, property)),
                "gte" => Expression.GreaterThanOrEqual(member, ConvertValue(value, property)),
                "lte" => Expression.LessThanOrEqual(member, ConvertValue(value, property)),
                "contains" when property.PropertyType == typeof(string)
                    => Expression.Call(member, nameof(string.Contains), null, ConvertValue(value, property)),
                "in" => BuildInExpression(member, value, property),
                _    => throw new NotSupportedException($"Operador '{op}' não suportado para {property.Name}")
            };

            finalExpr = finalExpr == null ? condition : Expression.AndAlso(finalExpr, condition);
        }

        return finalExpr == null
                   ? x => true // nenhum filtro aplicado
                   : Expression.Lambda<Func<T, bool>>(finalExpr, parameter);
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
            not null when baseType == typeof(Guid)     => Guid.Parse(value),
            { IsEnum: true }                           => Enum.Parse(baseType, value, ignoreCase: true),
            not null when baseType == typeof(DateOnly) => DateOnly.Parse(value),
            not null when baseType == typeof(TimeOnly) => TimeOnly.Parse(value),
            _                                          => Convert.ChangeType(value, baseType!)
        };

        return Expression.Constant(converted, targetType);
    }

    private static Expression BuildInExpression(MemberExpression member, string csv, PropertyInfo property)
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

        ConstantExpression arrayExpr = Expression.Constant(typedArray, typeof(IEnumerable<>).MakeGenericType(itemType));

        return Expression.Call(containsMethod, arrayExpr, member);
    }
}