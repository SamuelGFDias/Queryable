namespace Queryable.Core;

public class QuerySpec<T>
{
    private int _page = 1;
    private int _pageSize = 10;
    public Dictionary<string, string> Filters { get; set; } = new();
    public string? Sort { get; set; }

    public int Page
    {
        get => _page;
        set => _page = value > 0 ? value : _page;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > 0 ? value : _pageSize;
    }

    public bool SkipTotalCount { get; set; }
}