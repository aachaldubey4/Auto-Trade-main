namespace AutoTrade.Application.Interfaces;

/// <summary>
/// Orchestrates signal generation by combining technical and sentiment analysis
/// </summary>
public interface ISignalGenerator
{
    /// <summary>
    /// Generate signals for all watchlist stocks
    /// </summary>
    Task<List<TradingSignal>> GenerateSignalsAsync(SignalType type);

    /// <summary>
    /// Generate signals plus per-stock diagnostics for explainability
    /// </summary>
    Task<SignalGenerationResult> GenerateSignalsWithDiagnosticsAsync(SignalType type);
    
    /// <summary>
    /// Generate signal for a specific stock
    /// </summary>
    Task<TradingSignal?> GenerateSignalForStockAsync(string symbol, SignalType type);
}

public enum SignalType
{
    Overnight,
    Intraday
}

public enum SignalAction
{
    BUY,
    SELL
}

public class TradingSignal
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public SignalAction Action { get; set; }
    public decimal SignalStrength { get; set; } // 0-100
    public decimal EntryPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TechnicalScore { get; set; }
    public decimal SentimentScore { get; set; }
    public TechnicalIndicators Indicators { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public SignalType Type { get; set; }
    public string Status { get; set; } = "active"; // "active", "expired", "executed"
}

public class SignalGenerationResult
{
    public List<TradingSignal> Signals { get; set; } = new();
    public List<StockSignalDiagnostic> Diagnostics { get; set; } = new();
}

public class StockSignalDiagnostic
{
    public string Symbol { get; set; } = string.Empty;
    public SignalType Type { get; set; }
    public string TechnicalData { get; set; } = "pending";
    public string BuyEval { get; set; } = "pending";
    public string SellEval { get; set; } = "pending";
    public string RiskValidation { get; set; } = "pending";
    public string FinalStatus { get; set; } = "pending";
    public List<string> RejectionReasons { get; set; } = new();
    public SignalEvaluationSnapshot? BuySnapshot { get; set; }
    public SignalEvaluationSnapshot? SellSnapshot { get; set; }
}

public class SignalEvaluationSnapshot
{
    public decimal SentimentScore { get; set; }
    public decimal CombinedScore { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal Ema20 { get; set; }
    public decimal Rsi14 { get; set; }
    public bool MacdBullish { get; set; }
    public decimal VolumeRatio { get; set; }
}
