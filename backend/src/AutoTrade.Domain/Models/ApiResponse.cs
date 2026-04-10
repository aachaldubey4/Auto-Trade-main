namespace AutoTrade.Domain.Models;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public PaginationInfo? Pagination { get; set; }
    public ErrorInfo? Error { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class PaginationInfo
{
    public int Page { get; set; }
    public int Limit { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasNext { get; set; }
    public bool HasPrevious { get; set; }
}

public class ErrorInfo
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public class NewsResponse
{
    public List<MappedArticle> Articles { get; set; } = new();
    public int TotalCount { get; set; }
    public AppliedFilters Filters { get; set; } = new();
}

public class AppliedFilters
{
    public string? Stock { get; set; }
    public string? Sentiment { get; set; }
    public int Hours { get; set; }
    public MarketCategory? Category { get; set; }
}