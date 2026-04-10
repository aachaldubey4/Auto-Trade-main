using AutoTrade.Domain.Models;

namespace AutoTrade.Application.Interfaces;

public interface INewsProcessingService
{
    Task StartProcessingAsync();
    Task StopProcessingAsync();
    Task<List<MappedArticle>> ProcessNewsAsync();
    Task<MappedArticle> ProcessSingleArticleAsync(RawArticle rawArticle);
    bool IsProcessing { get; }
}