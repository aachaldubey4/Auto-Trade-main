namespace AutoTrade.Domain.Models;

public class RawArticle
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
}