namespace AutoTrade.Domain.Models;

public class MCPTool
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> InputSchema { get; set; } = new();
}

public class AnalysisResult
{
    public SentimentScore Sentiment { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public List<EntityData> Entities { get; set; } = new();
    public double MarketRelevance { get; set; }
    public MarketCategory Category { get; set; }
}

public class TrendData
{
    public List<string> TrendingTopics { get; set; } = new();
    public Dictionary<string, double> TopicScores { get; set; } = new();
    public DateTime AnalyzedAt { get; set; }
}