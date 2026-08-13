using Microsoft.AspNetCore.Mvc.ModelBinding;
using Queryable.Core;

namespace Queryable.Extensions;

public class QuerySpecModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (!context.Metadata.ModelType.IsGenericType)
            return null;

        Type genericType = context.Metadata.ModelType.GetGenericTypeDefinition();

        if (genericType == typeof(QuerySpec<>))
        {
            Type binderType = typeof(QuerySpecModelBinder<>).MakeGenericType(context.Metadata.ModelType.GetGenericArguments()[0]);
            return (IModelBinder?)Activator.CreateInstance(binderType);
        }

        return null;
    }
}
