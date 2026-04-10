namespace AutoTrade.Domain.Models;

public class StockSymbol
{
    public string Symbol { get; set; } = string.Empty;        // NSE symbol (e.g., "RELIANCE")
    public string CompanyName { get; set; } = string.Empty;   // Official company name
    public List<string> Aliases { get; set; } = new();        // Alternative names/variations
    public MarketCategory Sector { get; set; }
    public long MarketCap { get; set; }
    public bool IsActive { get; set; }
}