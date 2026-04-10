using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace AutoTrade.Infrastructure.Services.SignalGeneration;

/// <summary>
/// Calculates technical indicators from historical price data
/// </summary>
public class TechnicalAnalyzer(
    IMarketDataProvider marketDataProvider,
    ILogger<TechnicalAnalyzer> logger,
    TradingSignalsConfig config)
    : ITechnicalAnalyzer
{
    public async Task<TechnicalIndicators> CalculateIndicatorsAsync(string symbol)
    {
        try
        {
            // Fetch historical data (50 days for indicators)
            var historicalData = await marketDataProvider.GetHistoricalDataAsync(symbol, 50);
            
            if (historicalData.Count < 50)
            {
                throw new InvalidOperationException($"Insufficient historical data for {symbol}. Need 50 days, got {historicalData.Count}");
            }
            
            // Get current quote, but don't fail the entire symbol if live quote providers are throttled.
            MarketQuote currentQuote;
            try
            {
                currentQuote = await marketDataProvider.GetCurrentQuoteAsync(symbol);
            }
            catch (Exception ex)
            {
                var lastCandle = historicalData[^1];
                logger.LogWarning(ex,
                    "Falling back to latest historical close for {Symbol} quote due to live quote fetch failure",
                    symbol);
                currentQuote = new MarketQuote
                {
                    Symbol = symbol,
                    LastPrice = lastCandle.Close,
                    Open = lastCandle.Open,
                    High = lastCandle.High,
                    Low = lastCandle.Low,
                    Close = lastCandle.Close,
                    Volume = lastCandle.Volume,
                    Timestamp = DateTime.UtcNow
                };
            }
            
            // Extract closing prices
            var closingPrices = historicalData.Select(d => d.Close).ToList();
            
            // Calculate all indicators
            var ema20 = CalculateEma(closingPrices, 20);
            var rsi14 = CalculateRsi(closingPrices, 14);
            var macd = CalculateMacd(closingPrices);
            var volumeRatio = CalculateVolumeRatio(historicalData);
            
            // Calculate technical score
            var technicalScore = CalculateTechnicalScore(currentQuote.LastPrice, ema20, rsi14, macd, volumeRatio);
            
            var indicators = new TechnicalIndicators
            {
                Symbol = symbol,
                Ema20 = ema20,
                Rsi14 = rsi14,
                Macd = macd,
                VolumeRatio = volumeRatio,
                CurrentPrice = currentQuote.LastPrice,
                TechnicalScore = technicalScore,
                CalculatedAt = DateTime.UtcNow
            };
            
            logger.LogInformation("Calculated technical indicators for {Symbol}: EMA20={EMA}, RSI14={RSI}, MACD={MACD}, Volume={Volume}, Score={Score}",
                symbol, ema20, rsi14, macd.Histogram, volumeRatio, technicalScore);
            
            return indicators;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to calculate technical indicators for {Symbol}", symbol);
            throw;
        }
    }

    public decimal CalculateEma(List<decimal> prices, int period)
    {
        if (prices.Count < period)
        {
            throw new ArgumentException($"Insufficient data points. Need {period}, got {prices.Count}");
        }
        
        // Calculate multiplier: 2 / (period + 1)
        var multiplier = 2.0m / (period + 1);
        
        // Initialize EMA with SMA (Simple Moving Average) for first value
        var sma = prices.Take(period).Average();
        var ema = sma;
        
        // Calculate EMA for remaining values
        for (int i = period; i < prices.Count; i++)
        {
            ema = (prices[i] * multiplier) + (ema * (1 - multiplier));
        }
        
        return Math.Round(ema, 2);
    }

    public decimal CalculateRsi(List<decimal> prices, int period)
    {
        if (prices.Count < period + 1)
        {
            throw new ArgumentException($"Insufficient data points. Need {period + 1}, got {prices.Count}");
        }
        
        // Calculate price changes
        var gains = new List<decimal>();
        var losses = new List<decimal>();
        
        for (int i = 1; i < prices.Count; i++)
        {
            var change = prices[i] - prices[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? Math.Abs(change) : 0);
        }
        
        // Calculate average gain and loss over the period
        var avgGain = gains.Skip(gains.Count - period).Take(period).Average();
        var avgLoss = losses.Skip(losses.Count - period).Take(period).Average();
        
        // Avoid division by zero
        if (avgLoss == 0)
        {
            return 100; // If no losses, RSI is 100
        }
        
        // Calculate RS and RSI
        var rs = avgGain / avgLoss;
        var rsi = 100 - (100 / (1 + rs));
        
        return Math.Round(rsi, 2);
    }

    public MacdResult CalculateMacd(List<decimal> prices)
    {
        if (prices.Count < 26)
        {
            throw new ArgumentException($"Insufficient data points for MACD. Need 26, got {prices.Count}");
        }
        
        // Calculate EMA(12) and EMA(26)
        var ema12 = CalculateEma(prices, 12);
        var ema26 = CalculateEma(prices, 26);
        
        // MACD Line = EMA(12) - EMA(26)
        var macdLine = ema12 - ema26;
        
        // For Signal Line, we need to calculate EMA(9) of MACD values
        // Simplified: use a portion of recent MACD values
        // In production, you'd track MACD history
        var macdValues = new List<decimal>();
        for (int i = prices.Count - 9; i < prices.Count; i++)
        {
            var subset = prices.Take(i + 1).ToList();
            if (subset.Count >= 26)
            {
                var e12 = CalculateEma(subset, 12);
                var e26 = CalculateEma(subset, 26);
                macdValues.Add(e12 - e26);
            }
        }
        
        var signalLine = macdValues.Count >= 9 ? CalculateEma(macdValues, 9) : macdLine * 0.9m;
        
        // Histogram = MACD Line - Signal Line
        var histogram = macdLine - signalLine;
        
        return new MacdResult
        {
            MacdLine = Math.Round(macdLine, 2),
            SignalLine = Math.Round(signalLine, 2),
            Histogram = Math.Round(histogram, 2)
        };
    }

    public decimal CalculateVolumeRatio(List<OhlcData> data)
    {
        if (data.Count < 20)
        {
            throw new ArgumentException($"Insufficient data points for volume ratio. Need 20, got {data.Count}");
        }
        
        // Get current volume (most recent day)
        var currentVolume = data[^1].Volume;
        
        // Calculate 20-day average volume
        var avgVolume = data.TakeLast(20).Average(d => d.Volume);
        
        // Avoid division by zero
        if (avgVolume == 0)
        {
            return 1.0m;
        }
        
        var ratio = (decimal)currentVolume / (decimal)avgVolume;
        
        return Math.Round(ratio, 2);
    }

    private decimal CalculateTechnicalScore(decimal currentPrice, decimal ema20, decimal rsi14, MacdResult macd, decimal volumeRatio)
    {
        // EMA Score (25% weight)
        var emaScore = currentPrice > ema20 ? 100 : 0;
        
        // RSI Score (25% weight)
        decimal rsiScore;
        if (rsi14 >= 30 && rsi14 <= 70)
        {
            rsiScore = 100; // Optimal range
        }
        else if (rsi14 < 20 || rsi14 > 80)
        {
            rsiScore = 0; // Extreme oversold/overbought
        }
        else
        {
            rsiScore = 50; // Moderately oversold/overbought
        }
        
        // MACD Score (25% weight)
        var macdScore = macd.IsBullish ? 100 : 0;
        
        // Volume Score (25% weight)
        decimal volumeScore;
        if (volumeRatio > 1.5m)
        {
            volumeScore = 100; // High volume
        }
        else if (volumeRatio >= 1.0m)
        {
            volumeScore = 75; // Above average
        }
        else if (volumeRatio >= 0.5m)
        {
            volumeScore = 50; // Below average
        }
        else
        {
            volumeScore = 25; // Low volume
        }
        
        // Calculate weighted score
        var totalScore = (emaScore * 0.25m) + (rsiScore * 0.25m) + (macdScore * 0.25m) + (volumeScore * 0.25m);
        
        return Math.Round(totalScore, 2);
    }
}
