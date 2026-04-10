using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace AutoTrade.Infrastructure.Services.SignalGeneration;

/// <summary>
/// Enforces risk management rules and validates signals
/// </summary>
public class RiskManager(
    ISignalStorage signalStorage,
    IMarketDataProvider marketDataProvider,
    ILogger<RiskManager> logger,
    TradingSignalsConfig config)
    : IRiskManager
{
    public async Task<bool> ValidateSignalAsync(TradingSignal signal)
    {
        var (isValid, _) = await ValidateSignalWithReasonAsync(signal);
        return isValid;
    }

    public async Task<(bool IsValid, string? RejectionReason)> ValidateSignalWithReasonAsync(TradingSignal signal)
    {
        try
        {
            // Rule 1: Check active signal count < max
            var activeCount = await GetActiveSignalCountAsync();
            if (activeCount >= config.RiskManagement.MaxConcurrentSignals)
            {
                logger.LogWarning("Signal rejected for {Symbol}: Max concurrent signals ({Max}) reached", 
                    signal.Symbol, config.RiskManagement.MaxConcurrentSignals);
                return (false, "max_concurrent_reached");
            }
            
            // Rule 2: Check for duplicate signals
            var isDuplicate = await IsDuplicateSignalAsync(signal.Symbol, 
                TimeSpan.FromHours(config.RiskManagement.DuplicateSignalWindowHours));
            if (isDuplicate)
            {
                logger.LogWarning("Signal rejected for {Symbol}: Duplicate signal within {Hours} hours", 
                    signal.Symbol, config.RiskManagement.DuplicateSignalWindowHours);
                return (false, "duplicate_signal");
            }
            
            // Rule 3: Validate stop-loss
            if (!ValidateStopLoss(signal.EntryPrice, signal.StopLoss, signal.Action))
            {
                logger.LogWarning("Signal rejected for {Symbol}: Invalid stop-loss {StopLoss} for entry {Entry}", 
                    signal.Symbol, signal.StopLoss, signal.EntryPrice);
                return (false, "invalid_stop_loss");
            }
            
            // Rule 4: Validate target
            if (!ValidateTarget(signal.EntryPrice, signal.TargetPrice, signal.Action))
            {
                logger.LogWarning("Signal rejected for {Symbol}: Invalid target {Target} for entry {Entry}", 
                    signal.Symbol, signal.TargetPrice, signal.EntryPrice);
                return (false, "invalid_target");
            }
            
            // Rule 5: Validate risk-reward ratio
            var rrRatio = CalculateRiskRewardRatio(signal.EntryPrice, signal.TargetPrice, signal.StopLoss);
            if (rrRatio < (decimal)config.RiskManagement.MinRiskRewardRatio)
            {
                logger.LogWarning("Signal rejected for {Symbol}: Risk-reward ratio {Ratio} below minimum {Min}", 
                    signal.Symbol, rrRatio, config.RiskManagement.MinRiskRewardRatio);
                return (false, "risk_reward_below_minimum");
            }
            
            // Rule 6: For intraday signals, validate market is open
            if (signal.Type == SignalType.Intraday)
            {
                var isMarketOpen = await marketDataProvider.IsMarketOpenAsync();
                if (!isMarketOpen)
                {
                    logger.LogWarning("Signal rejected for {Symbol}: Market is closed for intraday signal", signal.Symbol);
                    return (false, "market_closed");
                }
            }
            
            logger.LogInformation("Signal validated for {Symbol}: All risk checks passed", signal.Symbol);
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating signal for {Symbol}", signal.Symbol);
            return (false, "risk_validation_error");
        }
    }

    public async Task<int> GetActiveSignalCountAsync()
    {
        var activeSignals = await signalStorage.GetActiveSignalsAsync();
        return activeSignals.Count;
    }

    public async Task<bool> IsDuplicateSignalAsync(string symbol, TimeSpan window)
    {
        var cutoffTime = DateTime.UtcNow - window;
        var recentSignals = await signalStorage.GetSignalsBySymbolAsync(symbol);
        
        return recentSignals.Any(s => s.GeneratedAt >= cutoffTime && s.Status == "active");
    }

    public decimal CalculatePositionSize(decimal totalCapital)
    {
        var positionSize = totalCapital * (decimal)config.RiskManagement.PositionSizePercent / 100m;
        return Math.Round(positionSize, 2);
    }

    public bool ValidateStopLoss(decimal entryPrice, decimal stopLoss, SignalAction action)
    {
        decimal stopLossPercent;
        
        if (action == SignalAction.BUY)
        {
            // For BUY: stop-loss should be below entry
            if (stopLoss >= entryPrice)
            {
                return false;
            }
            stopLossPercent = (entryPrice - stopLoss) / entryPrice * 100m;
        }
        else // SELL
        {
            // For SELL: stop-loss should be above entry
            if (stopLoss <= entryPrice)
            {
                return false;
            }
            stopLossPercent = (stopLoss - entryPrice) / entryPrice * 100m;
        }
        
        var minPercent = (decimal)config.RiskManagement.StopLossMinPercent;
        var maxPercent = (decimal)config.RiskManagement.StopLossMaxPercent;
        
        return stopLossPercent >= minPercent && stopLossPercent <= maxPercent;
    }

    public bool ValidateTarget(decimal entryPrice, decimal target, SignalAction action)
    {
        decimal targetPercent;
        
        if (action == SignalAction.BUY)
        {
            // For BUY: target should be above entry
            if (target <= entryPrice)
            {
                return false;
            }
            targetPercent = (target - entryPrice) / entryPrice * 100m;
        }
        else // SELL
        {
            // For SELL: target should be below entry
            if (target >= entryPrice)
            {
                return false;
            }
            targetPercent = (entryPrice - target) / entryPrice * 100m;
        }
        
        var minPercent = (decimal)config.RiskManagement.TargetMinPercent;
        var maxPercent = (decimal)config.RiskManagement.TargetMaxPercent;
        
        return targetPercent >= minPercent && targetPercent <= maxPercent;
    }

    public decimal CalculateRiskRewardRatio(decimal entry, decimal target, decimal stopLoss)
    {
        var risk = Math.Abs(entry - stopLoss);
        var reward = Math.Abs(target - entry);
        
        if (risk == 0)
        {
            return 0;
        }
        
        return Math.Round(reward / risk, 2);
    }
}
