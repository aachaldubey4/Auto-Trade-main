namespace AutoTrade.Application.Interfaces;

/// <summary>
/// Enforces risk management rules and validates signals
/// </summary>
public interface IRiskManager
{
    /// <summary>
    /// Validate a signal against all risk rules
    /// </summary>
    Task<bool> ValidateSignalAsync(TradingSignal signal);

    /// <summary>
    /// Validate a signal and return rejection reason when invalid
    /// </summary>
    Task<(bool IsValid, string? RejectionReason)> ValidateSignalWithReasonAsync(TradingSignal signal);
    
    /// <summary>
    /// Get count of currently active signals
    /// </summary>
    Task<int> GetActiveSignalCountAsync();
    
    /// <summary>
    /// Check if a duplicate signal exists within the time window
    /// </summary>
    Task<bool> IsDuplicateSignalAsync(string symbol, TimeSpan window);
    
    /// <summary>
    /// Calculate position size based on total capital
    /// </summary>
    decimal CalculatePositionSize(decimal totalCapital);
    
    /// <summary>
    /// Validate stop-loss is within acceptable range
    /// </summary>
    bool ValidateStopLoss(decimal entryPrice, decimal stopLoss, SignalAction action);
    
    /// <summary>
    /// Validate target price is within acceptable range
    /// </summary>
    bool ValidateTarget(decimal entryPrice, decimal target, SignalAction action);
    
    /// <summary>
    /// Calculate risk-reward ratio
    /// </summary>
    decimal CalculateRiskRewardRatio(decimal entry, decimal target, decimal stopLoss);
}
