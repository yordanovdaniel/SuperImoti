namespace SuperImoti.Web.Infrastructure;

/// <summary>One page of results plus the numbers needed to draw the pager.</summary>
public sealed class PagedResult<T>(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
{
    public static PagedResult<T> Empty(int pageSize) => new([], 1, pageSize, 0);

    public IReadOnlyList<T> Items { get; } = items;

    public int Page { get; } = page;

    public int PageSize { get; } = pageSize;

    public int TotalCount { get; } = totalCount;

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    public int FirstItem => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastItem => Math.Min(Page * PageSize, TotalCount);

    /// <summary>Keeps a requested page number inside 1..last page.</summary>
    public static int ClampPage(int requested, int totalCount, int pageSize)
    {
        var lastPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        return Math.Clamp(requested, 1, lastPage);
    }
}
