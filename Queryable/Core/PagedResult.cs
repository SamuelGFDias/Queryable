namespace Queryable.Core;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public PageMeta Meta { get; set; } = new();
}
