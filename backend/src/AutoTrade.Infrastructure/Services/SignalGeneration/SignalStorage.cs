using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace AutoTrade.Infrastructure.Services.SignalGeneration;

/// <summary>
/// Persists signals to MongoDB with query capabilities
/// </summary>
public class SignalStorage(MongoDbContext dbContext, ILogger<SignalStorage> logger) : ISignalStorage
{
    public async Task<string> SaveSignalAsync(TradingSignal signal)
    {
        try
        {
            var document = MapToDocument(signal);
            await dbContext.Signals.InsertOneAsync(document);
            
            logger.LogInformation("Saved {Action} signal for {Symbol} with ID {Id}", 
                signal.Action, signal.Symbol, document.Id);
            
            return document.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save signal for {Symbol}", signal.Symbol);
            throw;
        }
    }

    public async Task<List<TradingSignal>> GetActiveSignalsAsync()
    {
        try
        {
            var filter = Builders<SignalDocument>.Filter.And(
                Builders<SignalDocument>.Filter.Eq(s => s.Status, "active"),
                Builders<SignalDocument>.Filter.Gt(s => s.ExpiresAt, DateTime.UtcNow)
            );
            
            var documents = await dbContext.Signals
                .Find(filter)
                .SortByDescending(s => s.GeneratedAt)
                .ToListAsync();
            
            return documents.Select(MapFromDocument).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get active signals");
            return new List<TradingSignal>();
        }
    }

    public async Task<List<TradingSignal>> GetOvernightSignalsAsync()
    {
        try
        {
            var filter = Builders<SignalDocument>.Filter.And(
                Builders<SignalDocument>.Filter.Eq(s => s.Type, "Overnight"),
                Builders<SignalDocument>.Filter.Eq(s => s.Status, "active")
            );
            
            var documents = await dbContext.Signals
                .Find(filter)
                .SortByDescending(s => s.GeneratedAt)
                .ToListAsync();
            
            return documents.Select(MapFromDocument).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get overnight signals");
            return new List<TradingSignal>();
        }
    }

    public async Task<List<TradingSignal>> GetIntradaySignalsAsync()
    {
        try
        {
            var filter = Builders<SignalDocument>.Filter.And(
                Builders<SignalDocument>.Filter.Eq(s => s.Type, "Intraday"),
                Builders<SignalDocument>.Filter.Eq(s => s.Status, "active")
            );
            
            var documents = await dbContext.Signals
                .Find(filter)
                .SortByDescending(s => s.GeneratedAt)
                .ToListAsync();
            
            return documents.Select(MapFromDocument).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get intraday signals");
            return new List<TradingSignal>();
        }
    }

    public async Task<List<TradingSignal>> GetSignalsBySymbolAsync(string symbol)
    {
        try
        {
            var filter = Builders<SignalDocument>.Filter.Eq(s => s.Symbol, symbol);
            
            var documents = await dbContext.Signals
                .Find(filter)
                .SortByDescending(s => s.GeneratedAt)
                .Limit(50) // Limit to last 50 signals
                .ToListAsync();
            
            return documents.Select(MapFromDocument).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get signals for {Symbol}", symbol);
            return new List<TradingSignal>();
        }
    }

    public async Task<TradingSignal?> GetSignalByIdAsync(string signalId)
    {
        try
        {
            var filter = Builders<SignalDocument>.Filter.Eq(s => s.Id, signalId);
            var document = await dbContext.Signals.Find(filter).FirstOrDefaultAsync();
            return document == null ? null : MapFromDocument(document);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get signal by ID {Id}", signalId);
            return null;
        }
    }

    public async Task<List<TradingSignal>> GetHistoricalSignalsAsync(DateTime from, DateTime to)
    {
        try
        {
            var filter = Builders<SignalDocument>.Filter.And(
                Builders<SignalDocument>.Filter.Gte(s => s.GeneratedAt, from),
                Builders<SignalDocument>.Filter.Lte(s => s.GeneratedAt, to)
            );
            
            var documents = await dbContext.Signals
                .Find(filter)
                .SortByDescending(s => s.GeneratedAt)
                .ToListAsync();
            
            return documents.Select(MapFromDocument).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get historical signals");
            return new List<TradingSignal>();
        }
    }

    public async Task UpdateSignalStatusAsync(string signalId, string status)
    {
        try
        {
            var filter = Builders<SignalDocument>.Filter.Eq(s => s.Id, signalId);
            var update = Builders<SignalDocument>.Update
                .Set(s => s.Status, status)
                .Set(s => s.ExecutedAt, status == "executed" ? DateTime.UtcNow : null);
            
            await dbContext.Signals.UpdateOneAsync(filter, update);
            
            logger.LogInformation("Updated signal {Id} status to {Status}", signalId, status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update signal {Id} status", signalId);
        }
    }

    public async Task ExecuteSignalAsync(string signalId, decimal? entryPrice, decimal? targetPrice, decimal? stopLoss)
    {
        try
        {
            var filter = Builders<SignalDocument>.Filter.Eq(s => s.Id, signalId);
            var update = Builders<SignalDocument>.Update
                .Set(s => s.Status, "executed")
                .Set(s => s.ExecutedAt, DateTime.UtcNow);

            if (entryPrice.HasValue) update = update.Set(s => s.EntryPrice, entryPrice.Value);
            if (targetPrice.HasValue) update = update.Set(s => s.TargetPrice, targetPrice.Value);
            if (stopLoss.HasValue) update = update.Set(s => s.StopLoss, stopLoss.Value);

            await dbContext.Signals.UpdateOneAsync(filter, update);
            logger.LogInformation("Executed signal {Id}", signalId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute signal {Id}", signalId);
        }
    }

    public async Task ExpireOldSignalsAsync()
    {
        try
        {
            var filter = Builders<SignalDocument>.Filter.And(
                Builders<SignalDocument>.Filter.Eq(s => s.Status, "active"),
                Builders<SignalDocument>.Filter.Lt(s => s.ExpiresAt, DateTime.UtcNow)
            );
            
            var update = Builders<SignalDocument>.Update.Set(s => s.Status, "expired");
            
            var result = await dbContext.Signals.UpdateManyAsync(filter, update);
            
            if (result.ModifiedCount > 0)
            {
                logger.LogInformation("Expired {Count} old signals", result.ModifiedCount);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to expire old signals");
        }
    }

    private SignalDocument MapToDocument(TradingSignal signal)
    {
        return new SignalDocument
        {
            Id = signal.Id,
            Symbol = signal.Symbol,
            Action = signal.Action.ToString(),
            SignalStrength = signal.SignalStrength,
            EntryPrice = signal.EntryPrice,
            TargetPrice = signal.TargetPrice,
            StopLoss = signal.StopLoss,
            TechnicalScore = signal.TechnicalScore,
            SentimentScore = signal.SentimentScore,
            Indicators = new IndicatorsData
            {
                Ema20 = signal.Indicators.Ema20,
                Rsi14 = signal.Indicators.Rsi14,
                Macd = new MacdData
                {
                    MacdLine = signal.Indicators.Macd.MacdLine,
                    SignalLine = signal.Indicators.Macd.SignalLine,
                    Histogram = signal.Indicators.Macd.Histogram
                },
                VolumeRatio = signal.Indicators.VolumeRatio,
                CurrentPrice = signal.Indicators.CurrentPrice
            },
            GeneratedAt = signal.GeneratedAt,
            ExpiresAt = signal.ExpiresAt,
            Type = signal.Type.ToString(),
            Status = signal.Status,
            ExecutedAt = null
        };
    }

    private TradingSignal MapFromDocument(SignalDocument doc)
    {
        return new TradingSignal
        {
            Id = doc.Id,
            Symbol = doc.Symbol,
            Action = Enum.Parse<SignalAction>(doc.Action),
            SignalStrength = doc.SignalStrength,
            EntryPrice = doc.EntryPrice,
            TargetPrice = doc.TargetPrice,
            StopLoss = doc.StopLoss,
            TechnicalScore = doc.TechnicalScore,
            SentimentScore = doc.SentimentScore,
            Indicators = new TechnicalIndicators
            {
                Symbol = doc.Symbol,
                Ema20 = doc.Indicators.Ema20,
                Rsi14 = doc.Indicators.Rsi14,
                Macd = new MacdResult
                {
                    MacdLine = doc.Indicators.Macd.MacdLine,
                    SignalLine = doc.Indicators.Macd.SignalLine,
                    Histogram = doc.Indicators.Macd.Histogram
                },
                VolumeRatio = doc.Indicators.VolumeRatio,
                CurrentPrice = doc.Indicators.CurrentPrice,
                TechnicalScore = doc.TechnicalScore,
                CalculatedAt = doc.GeneratedAt
            },
            GeneratedAt = doc.GeneratedAt,
            ExpiresAt = doc.ExpiresAt,
            Type = Enum.Parse<SignalType>(doc.Type),
            Status = doc.Status
        };
    }
}
