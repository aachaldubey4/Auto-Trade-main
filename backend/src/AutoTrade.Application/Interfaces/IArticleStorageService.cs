using AutoTrade.Domain.Models;

namespace AutoTrade.Application.Interfaces;

public interface IArticleStorageService
{
    Task<string> StoreArticleAsync(MappedArticle article);
    Task<List<string>> StoreBatchAsync(List<MappedArticle> articles);
    Task<MappedArticle?> GetArticleByIdAsync(string id);
    Task<List<MappedArticle>> GetArticlesAsync(int page, int limit, string? stock = null, string? sentiment = null, int hours = 24);
    Task<List<MappedArticle>> GetArticlesByStockAsync(string symbol, int page, int limit);
    Task<int> GetTotalCountAsync(string? stock = null, string? sentiment = null, int hours = 24);
    Task CleanupOldArticlesAsync(int retentionDays = 30);
    Task<bool> ArticleExistsAsync(string contentHash);
}