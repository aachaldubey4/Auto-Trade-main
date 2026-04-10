namespace AutoTrade.Domain.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

/// <summary>
/// MongoDB document for trading signals
/// </summary>
public class SignalDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;
    
    public string Symbol { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // "BUY" or "SELL"
    public decimal SignalStrength { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TechnicalScore { get; set; }
    public decimal SentimentScore { get; set; }
    
    public IndicatorsData Indicators { get; set; } = new();
    
    public DateTime GeneratedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Type { get; set; } = string.Empty; // "Overnight" or "Intraday"
    public string Status { get; set; } = "active"; // "active", "expired", "executed"
    public DateTime? ExecutedAt { get; set; }
}

public class IndicatorsData
{
    public decimal Ema20 { get; set; }
    public decimal Rsi14 { get; set; }
    public MacdData Macd { get; set; } = new();
    public decimal VolumeRatio { get; set; }
    public decimal CurrentPrice { get; set; }
}

public class MacdData
{
    public decimal MacdLine { get; set; }
    public decimal SignalLine { get; set; }
    public decimal Histogram { get; set; }
}
