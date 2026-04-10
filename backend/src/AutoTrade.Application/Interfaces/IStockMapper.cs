using AutoTrade.Domain.Models;

namespace AutoTrade.Application.Interfaces;

public interface IStockMapper
{
    Task<List<string>> MapArticleToStocksAsync(ProcessedArticle article);
    Task UpdateStockDatabaseAsync();
    Task<StockSymbol?> FindStockByNameAsync(string companyName);
    Task<List<StockSymbol>> GetStocksByCategoryAsync(MarketCategory category);
    Task InitializeStockDatabaseAsync();
}