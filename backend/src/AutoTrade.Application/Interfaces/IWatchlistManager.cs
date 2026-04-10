namespace AutoTrade.Application.Interfaces;

/// <summary>
/// Manages configurable list of stocks to monitor
/// </summary>
public interface IWatchlistManager
{
    /// <summary>
    /// Get all active stocks from watchlist
    /// </summary>
    Task<List<WatchlistStock>> GetActiveStocksAsync();
    
    /// <summary>
    /// Get specific stock from watchlist
    /// </summary>
    Task<WatchlistStock?> GetStockAsync(string symbol);
    
    /// <summary>
    /// Update stock configuration
    /// </summary>
    Task UpdateStockAsync(WatchlistStock stock);
    
    /// <summary>
    /// Check if stock is enabled in watchlist
    /// </summary>
    Task<bool> IsStockEnabledAsync(string symbol);
}

public class WatchlistStock
{
    public string Symbol { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public long MinimumVolume { get; set; }
    public StockPriority Priority { get; set; }
    public DateTime LastAnalyzed { get; set; }
}

public enum StockPriority
{
    High,
    Medium,
    Low
}
