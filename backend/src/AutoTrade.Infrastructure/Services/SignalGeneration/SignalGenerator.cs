using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using System.Collections.Concurrent;

namespace AutoTrade.Infrastructure.Services.SignalGeneration;

/// <summary>
/// Orchestrates signal generation by combining technical and sentiment analysis
/// </summary>
public class SignalGenerator(
    IWatchlistManager watchlistManager,
    ITechnicalAnalyzer technicalAnalyzer,
    ISentimentProvider sentimentProvider,
    IRiskManager riskManager,
    ISignalStorage signalStorage,
    IMarketDataProvider marketDataProvider,
    ILogger<SignalGenerator> logger,
    TradingSignalsConfig config)
    : ISignalGenerator
{
    private readonly Random _random = new();

    public async Task<List<TradingSignal>> GenerateSignalsAsync(SignalType type)
    {
        var result = await GenerateSignalsWithDiagnosticsAsync(type);
        return result.Signals;
    }

    public async Task<SignalGenerationResult> GenerateSignalsWithDiagnosticsAsync(SignalType type)
    {
        logger.LogInformation("Starting {Type} signal generation", type);
        
        var signals = new ConcurrentBag<TradingSignal>();
        var diagnostics = new ConcurrentBag<StockSignalDiagnostic>();
        var stocks = await watchlistManager.GetActiveStocksAsync();

        var maxParallel = Math.Max(1, config.SignalGeneration.MaxParallelStocks);
        await Parallel.ForEachAsync(
            stocks,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallel },
            async (stock, _) =>
        {
            var diagnostic = new StockSignalDiagnostic
            {
                Symbol = stock.Symbol,
                Type = type
            };
            try
            {
                var signal = await GenerateSignalForStockInternalAsync(stock.Symbol, type, diagnostic);
                if (signal != null)
                {
                    signals.Add(signal);
                    logger.LogInformation("Generated {Action} signal for {Symbol} with strength {Strength}", 
                        signal.Action, signal.Symbol, signal.SignalStrength);
                }
            }
            catch (Exception ex)
            {
                diagnostic.TechnicalData = "error";
                diagnostic.RiskValidation = "error";
                diagnostic.FinalStatus = "error";
                diagnostic.RejectionReasons.Add("generation_error");
                logger.LogError(ex, "Failed to generate signal for {Symbol}", stock.Symbol);
            }
            finally
            {
                diagnostics.Add(diagnostic);
            }
        });
        
        logger.LogInformation("Completed {Type} signal generation: {Count} signals generated", type, signals.Count);
        return new SignalGenerationResult
        {
            Signals = signals.OrderByDescending(s => s.SignalStrength).ToList(),
            Diagnostics = diagnostics.OrderBy(d => d.Symbol).ToList()
        };
    }

    public async Task<TradingSignal?> GenerateSignalForStockAsync(string symbol, SignalType type)
    {
        var diagnostic = new StockSignalDiagnostic { Symbol = symbol, Type = type };
        return await GenerateSignalForStockInternalAsync(symbol, type, diagnostic);
    }

    private async Task<TradingSignal?> GenerateSignalForStockInternalAsync(
        string symbol,
        SignalType type,
        StockSignalDiagnostic diagnostic)
    {
        try
        {
            // Step 1: Fetch technical indicators
            var indicators = await technicalAnalyzer.CalculateIndicatorsAsync(symbol);
            diagnostic.TechnicalData = "ok";
            
            // Step 2: Fetch sentiment score (last 24 hours)
            var sentimentScore = await sentimentProvider.GetLatestSentimentAsync(symbol, TimeSpan.FromHours(24));
            
            // Step 3: Calculate combined score
            var technicalScore = indicators.TechnicalScore;
            var combinedScore = CalculateCombinedScore(technicalScore, sentimentScore);
            
            // Step 4: Evaluate BUY conditions
            var buyReasons = new List<string>();
            var buySignal = EvaluateBuyConditions(indicators, sentimentScore, combinedScore, buyReasons);
            diagnostic.BuyEval = buySignal ? "passed" : "failed";
            diagnostic.BuySnapshot = BuildSnapshot(indicators, sentimentScore, combinedScore);
            
            // Step 5: Evaluate SELL conditions
            var sellReasons = new List<string>();
            var sellSignal = EvaluateSellConditions(indicators, sentimentScore, combinedScore, sellReasons);
            diagnostic.SellEval = sellSignal ? "passed" : "failed";
            diagnostic.SellSnapshot = BuildSnapshot(indicators, sentimentScore, combinedScore);
            
            // Step 6: Create signal if conditions met
            TradingSignal? signal = null;
            
            if (buySignal)
            {
                signal = await CreateSignalAsync(symbol, SignalAction.BUY, type, indicators, sentimentScore, combinedScore);
            }
            else if (sellSignal)
            {
                signal = await CreateSignalAsync(symbol, SignalAction.SELL, type, indicators, sentimentScore, combinedScore);
            }
            else
            {
                diagnostic.RiskValidation = "skipped";
                diagnostic.FinalStatus = "no_signal_conditions";
                diagnostic.RejectionReasons.AddRange(buyReasons.Concat(sellReasons).Distinct());
            }
            
            // Step 7: Validate with Risk Manager
            if (signal != null)
            {
                var (isValid, rejectionReason) = await riskManager.ValidateSignalWithReasonAsync(signal);
                if (isValid)
                {
                    // Step 8: Persist signal
                    await signalStorage.SaveSignalAsync(signal);
                    diagnostic.RiskValidation = "passed";
                    diagnostic.FinalStatus = "generated";
                    return signal;
                }
                else
                {
                    diagnostic.RiskValidation = "failed";
                    diagnostic.FinalStatus = "rejected_by_risk";
                    if (!string.IsNullOrWhiteSpace(rejectionReason))
                    {
                        diagnostic.RejectionReasons.Add(rejectionReason);
                    }
                    logger.LogWarning("Signal for {Symbol} rejected by risk manager", symbol);
                    return null;
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            diagnostic.TechnicalData = "error";
            diagnostic.RiskValidation = "error";
            diagnostic.FinalStatus = "error";
            if (ex is InvalidOperationException && ex.Message.Contains("Insufficient historical data", StringComparison.OrdinalIgnoreCase))
            {
                diagnostic.RejectionReasons.Add("insufficient_history");
            }
            else
            {
                diagnostic.RejectionReasons.Add("generation_error");
            }
            logger.LogError(ex, "Error generating signal for {Symbol}", symbol);
            return null;
        }
    }

    private decimal CalculateCombinedScore(decimal technicalScore, decimal sentimentScore)
    {
        // Convert sentiment (0-1) to 0-100 scale
        var sentimentScore100 = sentimentScore * 100m;
        
        // Combine: Technical 70% + Sentiment 30%
        var combined = (technicalScore * (decimal)config.SignalGeneration.TechnicalWeight) + 
                      (sentimentScore100 * (decimal)config.SignalGeneration.SentimentWeight);
        
        return Math.Round(combined, 2);
    }

    private bool EvaluateBuyConditions(
        TechnicalIndicators indicators,
        decimal sentimentScore,
        decimal combinedScore,
        List<string> reasons)
    {
        var buyConfig = config.SignalGeneration.BuyConditions;

        var sentimentOk = sentimentScore > (decimal)buyConfig.MinSentiment;
        if (!sentimentOk) reasons.Add("sentiment_below_minimum");

        var emaOk = !buyConfig.RequirePriceAboveEma || indicators.CurrentPrice > indicators.Ema20;
        if (!emaOk) reasons.Add("price_vs_ema_failed");

        var rsiOk = indicators.Rsi14 >= buyConfig.RsiMin && indicators.Rsi14 <= buyConfig.RsiMax;
        if (!rsiOk) reasons.Add("rsi_out_of_range");

        var macdOk = !buyConfig.RequireMacdBullish || indicators.Macd.IsBullish;
        if (!macdOk) reasons.Add("macd_condition_failed");

        var volumeOk = indicators.VolumeRatio > (decimal)buyConfig.MinVolumeRatio;
        if (!volumeOk) reasons.Add("volume_below_threshold");

        var scoreOk = combinedScore > config.SignalGeneration.MinimumSignalStrength;
        if (!scoreOk) reasons.Add("combined_score_below_threshold");

        return sentimentOk && emaOk && rsiOk && macdOk && volumeOk && scoreOk;
    }

    private bool EvaluateSellConditions(
        TechnicalIndicators indicators,
        decimal sentimentScore,
        decimal combinedScore,
        List<string> reasons)
    {
        var sellConfig = config.SignalGeneration.SellConditions;

        var sentimentOk = sentimentScore < (decimal)sellConfig.MaxSentiment;
        if (!sentimentOk) reasons.Add("sentiment_above_maximum");

        var trendOk = !sellConfig.RequirePriceBelowEmaOrHighRsi ||
                      (indicators.CurrentPrice < indicators.Ema20 || indicators.Rsi14 > sellConfig.RsiOverbought);
        if (!trendOk) reasons.Add("price_vs_ema_failed");

        var macdOk = !sellConfig.RequireMacdBearish || !indicators.Macd.IsBullish;
        if (!macdOk) reasons.Add("macd_condition_failed");

        var volumeOk = indicators.VolumeRatio > (decimal)sellConfig.MinVolumeRatio;
        if (!volumeOk) reasons.Add("volume_below_threshold");

        var scoreOk = combinedScore > config.SignalGeneration.MinimumSignalStrength;
        if (!scoreOk) reasons.Add("combined_score_below_threshold");

        return sentimentOk && trendOk && macdOk && volumeOk && scoreOk;
    }

    private static SignalEvaluationSnapshot BuildSnapshot(
        TechnicalIndicators indicators,
        decimal sentimentScore,
        decimal combinedScore)
    {
        return new SignalEvaluationSnapshot
        {
            SentimentScore = Math.Round(sentimentScore, 3),
            CombinedScore = combinedScore,
            CurrentPrice = indicators.CurrentPrice,
            Ema20 = indicators.Ema20,
            Rsi14 = indicators.Rsi14,
            MacdBullish = indicators.Macd.IsBullish,
            VolumeRatio = indicators.VolumeRatio
        };
    }

    private async Task<TradingSignal> CreateSignalAsync(
        string symbol, 
        SignalAction action, 
        SignalType type, 
        TechnicalIndicators indicators, 
        decimal sentimentScore, 
        decimal combinedScore)
    {
        var entryPrice = indicators.CurrentPrice;
        
        // Calculate target and stop-loss based on action and type
        var (targetPrice, stopLoss) = CalculatePrices(entryPrice, action, type);
        
        // Calculate expiration time
        var expiresAt = CalculateExpirationTime(type);
        
        var signal = new TradingSignal
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Symbol = symbol,
            Action = action,
            SignalStrength = combinedScore,
            EntryPrice = entryPrice,
            TargetPrice = targetPrice,
            StopLoss = stopLoss,
            TechnicalScore = indicators.TechnicalScore,
            SentimentScore = sentimentScore * 100m, // Convert to 0-100 scale
            Indicators = indicators,
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            Type = type,
            Status = "active"
        };
        
        return signal;
    }

    private (decimal targetPrice, decimal stopLoss) CalculatePrices(decimal entryPrice, SignalAction action, SignalType type)
    {
        // Get percentage ranges from config
        var targetMinPercent = (decimal)config.RiskManagement.TargetMinPercent / 100m;
        var targetMaxPercent = (decimal)config.RiskManagement.TargetMaxPercent / 100m;
        var stopLossMinPercent = (decimal)config.RiskManagement.StopLossMinPercent / 100m;
        var stopLossMaxPercent = (decimal)config.RiskManagement.StopLossMaxPercent / 100m;
        
        // Random percentage within range
        var targetPercent = targetMinPercent + ((decimal)_random.NextDouble() * (targetMaxPercent - targetMinPercent));
        var stopLossPercent = stopLossMinPercent + ((decimal)_random.NextDouble() * (stopLossMaxPercent - stopLossMinPercent));
        
        decimal targetPrice, stopLoss;
        
        if (action == SignalAction.BUY)
        {
            targetPrice = entryPrice * (1 + targetPercent);
            stopLoss = entryPrice * (1 - stopLossPercent);
        }
        else // SELL
        {
            targetPrice = entryPrice * (1 - targetPercent);
            stopLoss = entryPrice * (1 + stopLossPercent);
        }
        
        return (Math.Round(targetPrice, 2), Math.Round(stopLoss, 2));
    }

    private DateTime CalculateExpirationTime(SignalType type)
    {
        var istTimeZone = TimeZoneInfo.FindSystemTimeZoneById(config.MarketData.Timezone);
        var istNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istTimeZone);
        
        if (type == SignalType.Overnight)
        {
            // Expires at configured time next trading day (default 10:00 AM IST)
            var expiryTime = TimeSpan.Parse(config.Scheduling.OvernightSignalExpiryTime);
            var expiryDate = istNow.Date.AddDays(1);
            
            // Skip weekends
            while (expiryDate.DayOfWeek == DayOfWeek.Saturday || expiryDate.DayOfWeek == DayOfWeek.Sunday)
            {
                expiryDate = expiryDate.AddDays(1);
            }
            
            var expiryDateTime = expiryDate.Add(expiryTime);
            return TimeZoneInfo.ConvertTimeToUtc(expiryDateTime, istTimeZone);
        }
        else // Intraday
        {
            // Expires 3-6 hours from now (random within range)
            var minHours = config.Scheduling.IntradaySignalDurationHoursMin;
            var maxHours = config.Scheduling.IntradaySignalDurationHoursMax;
            var hours = minHours + (_random.NextDouble() * (maxHours - minHours));
            
            return DateTime.UtcNow.AddHours(hours);
        }
    }
}
