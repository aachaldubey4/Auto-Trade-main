namespace AutoTrade.Domain.Models;

public class MappedArticle : ProcessedArticle
{
    public List<string> StockSymbols { get; set; } = new();
    public bool IsGeneralMarket { get; set; }
}