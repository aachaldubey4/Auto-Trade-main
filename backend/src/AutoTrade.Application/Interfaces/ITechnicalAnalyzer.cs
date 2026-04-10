namespace AutoTrade.Application.Interfaces;

/// <summary>
/// Calculates technical indicators from historical price data
/// </summary>
public interface ITechnicalAnalyzer
{
    /// <summary>
    /// Calculate all technical indicators for a stock symbol
    /// </summary>
    Task<TechnicalIndicators> CalculateIndicatorsAsync(string symbol);
    
    /// <summary>
    /// Calculate Exponential Moving Average
    /// </summary>
    decimal CalculateEma(List<decimal> prices, int period);
    
    /// <summary>
    /// Calculate Relative Strength Index
    /// </summary>
    decimal CalculateRsi(List<decimal> prices, int period);
    
    /// <summary>
    /// Calculate MACD (Moving Average Convergence Divergence)
    /// </summary>
    MacdResult CalculateMacd(List<decimal> prices);
    
    /// <summary>
    /// Calculate volume ratio compared to average
    /// </summary>
    decimal CalculateVolumeRatio(List<OhlcData> data);
}

public class TechnicalIndicators
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Ema20 { get; set; }
    public decimal Rsi14 { get; set; }
    public MacdResult Macd { get; set; } = new();
    public decimal VolumeRatio { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal TechnicalScore { get; set; } // 0-100
    public DateTime CalculatedAt { get; set; }
}

public class MacdResult
{
    public decimal MacdLine { get; set; }
    public decimal SignalLine { get; set; }
    public decimal Histogram { get; set; }
    public bool IsBullish => Histogram > 0;
}
