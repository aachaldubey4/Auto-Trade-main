namespace AutoTrade.Application.Interfaces;

/// <summary>
/// Provides market data from NSE India API with Yahoo Finance fallback
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// Get current market quote for a stock symbol
    /// </summary>
    Task<MarketQuote> GetCurrentQuoteAsync(string symbol);
    
    /// <summary>
    /// Get historical OHLC data for specified number of days
    /// </summary>
    Task<List<OhlcData>> GetHistoricalDataAsync(string symbol, int days);
    
    /// <summary>
    /// Check if market is currently open (9:15 AM - 3:30 PM IST, Mon-Fri)
    /// </summary>
    Task<bool> IsMarketOpenAsync();
    
    /// <summary>
    /// Check if a specific date is a market holiday
    /// </summary>
    Task<bool> IsMarketHolidayAsync(DateTime date);
}

public class MarketQuote
{
    public string Symbol { get; set; } = string.Empty;
    public decimal LastPrice { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public DateTime Timestamp { get; set; }
}

public class OhlcData
{
    public DateTime Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
}
