using AutoTrade.Domain.Models;

namespace AutoTrade.Application.Interfaces;

public interface ISentimentAnalyzer
{
    Task<ProcessedArticle> AnalyzeArticleAsync(RawArticle article);
    Task<List<ProcessedArticle>> BatchAnalyzeAsync(List<RawArticle> articles);
    Task<MarketCategory> CategorizeByMarketAsync(ProcessedArticle article);
    Task<SentimentScore> GetNewsSentimentAsync(string symbol);
}