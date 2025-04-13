namespace Queryable.Core;

public class QuerySpec<T>
{
    public Dictionary<string, string> Filters { get; set; } = new();
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public bool SkipTotalCount { get; set; } = false;
}