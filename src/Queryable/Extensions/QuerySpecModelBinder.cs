using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Queryable.Core;

namespace Queryable.Extensions;

public partial class QuerySpecModelBinder<T> : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        IQueryCollection query = bindingContext.HttpContext.Request.Query;

        var spec = new QuerySpec<T>();

        foreach (var (key, value) in query)
        {
            if (key.Equals("page", StringComparison.OrdinalIgnoreCase))
            {
                spec.Page = int.TryParse(value, out int page) ? page : 1;
            } else if (key.Equals("pageSize", StringComparison.OrdinalIgnoreCase))
            {
                spec.PageSize = int.TryParse(value, out int pageSize) ? pageSize : 10;
            } else if (key.Equals("sort", StringComparison.OrdinalIgnoreCase))
            {
                spec.Sort = value;
            } else if (key.Equals("skipTotalCount", StringComparison.OrdinalIgnoreCase))
            {
                spec.SkipTotalCount = bool.TryParse(value, out bool skip) && skip;
            } else if (SwaggerFilterRegex().Match(key) is { Success: true } match)
            {
                string cleanKey = match.Groups[1].Value;
                spec.Filters[cleanKey.ToLowerInvariant()] = value!;
            } else
            {
                spec.Filters[key.ToLowerInvariant()] = value!;
            }
        }

        bindingContext.Result = ModelBindingResult.Success(spec);
        return Task.CompletedTask;
    }

    [GeneratedRegex(@"Filters\[(.*?)\]")]
    private static partial Regex SwaggerFilterRegex();
}