namespace AutoTrade.Domain.Models;

public class ProcessedArticle : RawArticle
{
    public SentimentScore Sentiment { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public List<EntityData> Entities { get; set; } = new();
    public MarketCategory MarketCategory { get; set; }
    public double MarketRelevance { get; set; }
    public DateTime ProcessedAt { get; set; }
}