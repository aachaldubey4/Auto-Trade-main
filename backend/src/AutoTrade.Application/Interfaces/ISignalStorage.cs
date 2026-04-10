namespace AutoTrade.Application.Interfaces;

/// <summary>
/// Persists signals to MongoDB with query capabilities
/// </summary>
public interface ISignalStorage
{
    /// <summary>
    /// Save a new signal to database
    /// </summary>
    Task<string> SaveSignalAsync(TradingSignal signal);
    
    /// <summary>
    /// Get all active signals
    /// </summary>
    Task<List<TradingSignal>> GetActiveSignalsAsync();
    
    /// <summary>
    /// Get overnight signals
    /// </summary>
    Task<List<TradingSignal>> GetOvernightSignalsAsync();
    
    /// <summary>
    /// Get intraday signals
    /// </summary>
    Task<List<TradingSignal>> GetIntradaySignalsAsync();
    
    /// <summary>
    /// Get signals for specific stock symbol
    /// </summary>
    Task<List<TradingSignal>> GetSignalsBySymbolAsync(string symbol);

    /// <summary>
    /// Get a signal by its ID
    /// </summary>
    Task<TradingSignal?> GetSignalByIdAsync(string signalId);
    
    /// <summary>
    /// Get historical signals with filtering
    /// </summary>
    Task<List<TradingSignal>> GetHistoricalSignalsAsync(DateTime from, DateTime to);
    
    /// <summary>
    /// Update signal status
    /// </summary>
    Task UpdateSignalStatusAsync(string signalId, string status);

    /// <summary>
    /// Mark a signal as executed (optionally overriding prices)
    /// </summary>
    Task ExecuteSignalAsync(string signalId, decimal? entryPrice, decimal? targetPrice, decimal? stopLoss);
    
    /// <summary>
    /// Expire old signals (background task)
    /// </summary>
    Task ExpireOldSignalsAsync();
}
