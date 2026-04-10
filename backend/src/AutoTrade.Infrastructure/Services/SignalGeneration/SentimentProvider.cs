using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace AutoTrade.Infrastructure.Services.SignalGeneration;

/// <summary>
/// Provides sentiment scores from news articles stored in MongoDB
/// </summary>
public class SentimentProvider(MongoDbContext dbContext, ILogger<SentimentProvider> logger) : ISentimentProvider
{
    public async Task<decimal> GetLatestSentimentAsync(string symbol, TimeSpan window)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow - window;
            
            // Query MongoDB for articles mentioning this stock symbol in the time window
            var filter = Builders<ArticleDocument>.Filter.And(
                Builders<ArticleDocument>.Filter.AnyEq(a => a.StockSymbols, symbol),
                Builders<ArticleDocument>.Filter.Gte(a => a.PublishedAt, cutoffTime)
            );
            
            var articles = await dbContext.Articles
                .Find(filter)
                .SortByDescending(a => a.PublishedAt)
                .ToListAsync();
            
            if (!articles.Any())
            {
                logger.LogDebug("No articles found for {Symbol} in last {Hours} hours, returning neutral sentiment", 
                    symbol, window.TotalHours);
                return 0.5m; // Neutral sentiment
            }
            
            // Calculate weighted average sentiment (more recent articles have higher weight)
            decimal totalWeight = 0;
            decimal weightedSum = 0;
            
            for (int i = 0; i < articles.Count; i++)
            {
                var article = articles[i];
                
                // Weight decreases with age (most recent = 1.0, oldest = 0.5)
                var weight = 1.0m - (i * 0.5m / articles.Count);
                
                // Convert sentiment to 0-1 scale
                decimal sentimentValue = article.Sentiment?.Overall switch
                {
                    "positive" => (decimal)(article.Sentiment.Positive),
                    "negative" => (decimal)(1.0 - article.Sentiment.Negative),
                    _ => 0.5m
                };
                
                weightedSum += sentimentValue * weight;
                totalWeight += weight;
            }
            
            var averageSentiment = totalWeight > 0 ? weightedSum / totalWeight : 0.5m;
            
            logger.LogInformation("Calculated sentiment for {Symbol} from {Count} articles: {Sentiment}", 
                symbol, articles.Count, averageSentiment);
            
            return Math.Round(averageSentiment, 3);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get sentiment for {Symbol}", symbol);
            return 0.5m; // Return neutral on error
        }
    }
}
