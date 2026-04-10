using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace AutoTrade.Infrastructure.Services.SignalGeneration;

/// <summary>
/// Manages configurable list of stocks to monitor
/// </summary>
public class WatchlistManager(TradingSignalsConfig config, ILogger<WatchlistManager> logger) : IWatchlistManager
{
    private readonly List<WatchlistStock> _watchlist = InitializeWatchlist(config, logger);

    public Task<List<WatchlistStock>> GetActiveStocksAsync()
    {
        var activeStocks = _watchlist
            .Where(s => s.IsEnabled)
            .OrderByDescending(s => s.Priority)
            .ThenBy(s => s.Symbol)
            .ToList();
        
        logger.LogDebug("Retrieved {Count} active stocks from watchlist", activeStocks.Count);
        return Task.FromResult(activeStocks);
    }

    public Task<WatchlistStock?> GetStockAsync(string symbol)
    {
        var stock = _watchlist.FirstOrDefault(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(stock);
    }

    public Task UpdateStockAsync(WatchlistStock stock)
    {
        var existing = _watchlist.FirstOrDefault(s => s.Symbol.Equals(stock.Symbol, StringComparison.OrdinalIgnoreCase));
        
        if (existing != null)
        {
            existing.IsEnabled = stock.IsEnabled;
            existing.MinimumVolume = stock.MinimumVolume;
            existing.Priority = stock.Priority;
            existing.LastAnalyzed = stock.LastAnalyzed;
            
            logger.LogInformation("Updated watchlist stock: {Symbol}, Enabled={Enabled}, Priority={Priority}", 
                stock.Symbol, stock.IsEnabled, stock.Priority);
        }
        else
        {
            _watchlist.Add(stock);
            logger.LogInformation("Added new stock to watchlist: {Symbol}", stock.Symbol);
        }
        
        return Task.CompletedTask;
    }

    public Task<bool> IsStockEnabledAsync(string symbol)
    {
        var stock = _watchlist.FirstOrDefault(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(stock?.IsEnabled ?? false);
    }

    private static List<WatchlistStock> InitializeWatchlist(TradingSignalsConfig config, ILogger logger)
    {
        var watchlist = new List<WatchlistStock>();
        
        foreach (var stockConfig in config.Watchlist.DefaultStocks)
        {
            var priority = Enum.TryParse<StockPriority>(stockConfig.Priority, out var p) ? p : StockPriority.Medium;
            
            watchlist.Add(new WatchlistStock
            {
                Symbol = stockConfig.Symbol,
                IsEnabled = stockConfig.IsEnabled,
                MinimumVolume = stockConfig.MinimumVolume,
                Priority = priority,
                LastAnalyzed = DateTime.MinValue
            });
        }
        
        logger.LogInformation("Initialized watchlist with {Count} stocks", watchlist.Count);
        return watchlist;
    }
}
