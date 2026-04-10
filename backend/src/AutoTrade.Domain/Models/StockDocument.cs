using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AutoTrade.Domain.Models;

public class StockDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    public string Symbol { get; set; } = string.Empty;           // Primary NSE symbol (e.g. "RELIANCE")
    public string CompanyName { get; set; } = string.Empty;      // Official name
    public string ISIN { get; set; } = string.Empty;             // NSE ISIN code
    public string Series { get; set; } = "EQ";                  // Trading series (EQ, BE, etc.)
    public List<string> Aliases { get; set; } = new();          // Alternative names
    public MarketCategory Sector { get; set; }
    public long MarketCap { get; set; }
    public bool IsActive { get; set; }
    public DateTime LastUpdated { get; set; }

    // Search optimization
    public List<string> SearchTerms { get; set; } = new();    // Preprocessed search terms
}