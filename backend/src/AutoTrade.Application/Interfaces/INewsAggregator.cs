using AutoTrade.Domain.Models;

namespace AutoTrade.Application.Interfaces;

public interface INewsAggregator
{
    Task StartMonitoringAsync();
    Task StopMonitoringAsync();
    Task<List<RawArticle>> FetchFromSourceAsync(string sourceUrl);
    Task<bool> ValidateFeedAsync(string feedUrl);
    Task<List<RawArticle>> FetchAllSourcesAsync();
    IReadOnlyList<FeedHealthStatus> GetFeedHealth();
}