using System.Linq.Expressions;

namespace Queryable.Interfaces;

public interface IFilterBuilder
{
    Expression<Func<T, bool>> BuildPredicate<T>(IDictionary<string, string> queryParams);
}