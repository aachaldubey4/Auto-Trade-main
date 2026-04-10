namespace AutoTrade.Domain.Models;

public class SystemConfig
{
    public List<RSSFeedConfig> RssFeeds { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public ApiConfig Api { get; set; } = new();
    public SignalRConfig SignalR { get; set; } = new();
}

public class RSSFeedConfig
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int FetchInterval { get; set; } = 5;    // minutes
    public int Priority { get; set; } = 5;         // 1-10
}

public class SentimentAnalysisConfig
{
    public double LexiconWeightWithContent { get; set; } = 0.7;
    public double HeadlineWeightWithContent { get; set; } = 0.3;
    public double LexiconWeightHeadlineOnly { get; set; } = 0.4;
    public double HeadlineWeightHeadlineOnly { get; set; } = 0.6;
    public double IndianPhraseMultiplier { get; set; } = 2.0;
    public int MinContentLengthForFullAnalysis { get; set; } = 50;
}

public class DatabaseConfig
{
    public string MongoConnectionString { get; set; } = string.Empty;
    public string RedisConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "autotrade";
}

public class ApiConfig
{
    public int RateLimitPerMinute { get; set; } = 100;
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 100;
}

public class SignalRConfig
{
    public bool Enabled { get; set; } = true;
    public int HeartbeatInterval { get; set; } = 30; // seconds
}