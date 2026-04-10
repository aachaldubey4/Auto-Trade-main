using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AutoTrade.Domain.Models;

public class ArticleDocument
{
    [BsonId]
    public ObjectId Id { get; set; }
    
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Content identification
    public string ContentHash { get; set; } = string.Empty;
    
    // AI Analysis results
    public SentimentScore Sentiment { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public List<EntityData> Entities { get; set; } = new();
    public MarketCategory MarketCategory { get; set; }
    public double MarketRelevance { get; set; }
    
    // Stock mapping
    public List<string> StockSymbols { get; set; } = new();
    public bool IsGeneralMarket { get; set; }
    
    // Metadata
    public string ProcessingStatus { get; set; } = "pending"; // "pending", "processed", "failed"
    public string? ProcessingError { get; set; }
    
    // Indexing
    public double[]? SearchVector { get; set; }  // For semantic search
}