namespace AutoTrade.Application.Interfaces;

/// <summary>
/// Provides sentiment scores from news articles
/// </summary>
public interface ISentimentProvider
{
    /// <summary>
    /// Get latest sentiment score for a stock symbol within a time window
    /// </summary>
    Task<decimal> GetLatestSentimentAsync(string symbol, TimeSpan window);
}
