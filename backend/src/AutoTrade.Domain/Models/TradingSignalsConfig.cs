namespace AutoTrade.Domain.Models;

/// <summary>
/// Configuration for trading signal generation system
/// </summary>
public class TradingSignalsConfig
{
    public SignalGenerationConfig SignalGeneration { get; set; } = new();
    public RiskManagementConfig RiskManagement { get; set; } = new();
    public MarketDataConfig MarketData { get; set; } = new();
    public WatchlistConfig Watchlist { get; set; } = new();
    public SchedulingConfig Scheduling { get; set; } = new();
}

public class SignalGenerationConfig
{
    public double TechnicalWeight { get; set; } = 0.7;
    public double SentimentWeight { get; set; } = 0.3;
    public int MinimumSignalStrength { get; set; } = 70;
    public int MaxParallelStocks { get; set; } = 3;
    public BuyConditionsConfig BuyConditions { get; set; } = new();
    public SellConditionsConfig SellConditions { get; set; } = new();
}

public class BuyConditionsConfig
{
    public double MinSentiment { get; set; } = 0.6;
    public bool RequirePriceAboveEma { get; set; } = true;
    public int RsiMin { get; set; } = 30;
    public int RsiMax { get; set; } = 70;
    public bool RequireMacdBullish { get; set; } = true;
    public double MinVolumeRatio { get; set; } = 1.5;
}

public class SellConditionsConfig
{
    public double MaxSentiment { get; set; } = 0.4;
    public bool RequirePriceBelowEmaOrHighRsi { get; set; } = true;
    public int RsiOverbought { get; set; } = 70;
    public bool RequireMacdBearish { get; set; } = true;
    public double MinVolumeRatio { get; set; } = 2.0;
}

public class RiskManagementConfig
{
    public int MaxConcurrentSignals { get; set; } = 8;
    public double PositionSizePercent { get; set; } = 12.5;
    public double StopLossMinPercent { get; set; } = 2.0;
    public double StopLossMaxPercent { get; set; } = 3.0;
    public double TargetMinPercent { get; set; } = 2.0;
    public double TargetMaxPercent { get; set; } = 5.0;
    public double MinRiskRewardRatio { get; set; } = 1.0;
    public double PreferredRiskRewardRatio { get; set; } = 2.0;
    public int DuplicateSignalWindowHours { get; set; } = 6;
}

public class MarketDataConfig
{
    public string PrimaryProvider { get; set; } = "NSE";
    public string FallbackProvider { get; set; } = "YahooFinance";
    public int CacheDurationMinutes { get; set; } = 1;
    public string NseApiUrl { get; set; } = "https://www.nseindia.com/api";
    public string MarketOpenTime { get; set; } = "09:15";
    public string MarketCloseTime { get; set; } = "15:30";
    public string Timezone { get; set; } = "India Standard Time";
    
    /// <summary>
    /// Delay in milliseconds between consecutive API calls to avoid rate limiting
    /// </summary>
    public int ApiCallDelayMs { get; set; } = 500;
    public int NseQuoteRetries { get; set; } = 2;
    public int YahooQuoteRetries { get; set; } = 2;
    public int YahooRetryDelayMs { get; set; } = 500;
    public int BhavcopyCacheMinutes { get; set; } = 30;
    public int QuoteFallbackMaxStalenessHours { get; set; } = 36;
    public int WatchlistQuoteParallelism { get; set; } = 3;
}

public class WatchlistConfig
{
    public int MaxStocks { get; set; } = 15;
    public List<WatchlistStockConfig> DefaultStocks { get; set; } = new();
}

public class WatchlistStockConfig
{
    public string Symbol { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public long MinimumVolume { get; set; }
    public string Priority { get; set; } = "Medium";
}

public class SchedulingConfig
{
    public string OvernightAnalysisTime { get; set; } = "20:00";
    public int IntradayAnalysisIntervalMinutes { get; set; } = 15;
    public int MarketDataRefreshIntervalMinutes { get; set; } = 1;
    public string OvernightSignalExpiryTime { get; set; } = "10:00";
    public int IntradaySignalDurationHoursMin { get; set; } = 3;
    public int IntradaySignalDurationHoursMax { get; set; } = 6;
}
