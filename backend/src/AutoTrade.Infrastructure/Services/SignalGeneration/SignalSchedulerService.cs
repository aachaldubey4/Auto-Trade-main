using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace AutoTrade.Infrastructure.Services.SignalGeneration;

/// <summary>
/// Background service for scheduled signal generation
/// </summary>
public class SignalSchedulerService(
    IServiceProvider serviceProvider,
    ILogger<SignalSchedulerService> logger,
    TradingSignalsConfig config)
    : BackgroundService
{
    private readonly TimeZoneInfo _istTimeZone = TimeZoneInfo.FindSystemTimeZoneById(config.MarketData.Timezone);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Signal Scheduler Service started");
        
        // Track last execution times to avoid duplicate runs
        DateTime? lastOvernightRun = null;
        DateTime? lastIntradayRun = null;
        DateTime? lastRefreshRun = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var utcNow = DateTime.UtcNow;
                var istNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, _istTimeZone);
                
                // Overnight Analysis: Daily at 8:00 PM IST
                if (ShouldRunOvernightAnalysis(istNow, lastOvernightRun))
                {
                    logger.LogInformation("Starting overnight analysis at {Time} IST", istNow);
                    await RunOvernightAnalysisAsync();
                    lastOvernightRun = istNow;
                }
                
                // Intraday Analysis: Every 15 minutes during market hours
                if (await ShouldRunIntradayAnalysisAsync(istNow, lastIntradayRun))
                {
                    logger.LogInformation("Starting intraday analysis at {Time} IST", istNow);
                    await RunIntradayAnalysisAsync();
                    lastIntradayRun = istNow;
                }
                
                // Market Data Refresh: Every 1 minute during market hours
                if (await ShouldRefreshMarketDataAsync(istNow, lastRefreshRun))
                {
                    await RefreshMarketDataAsync();
                    lastRefreshRun = istNow;
                }
                
                // Expire old signals
                await ExpireOldSignalsAsync();
                
                // Wait 1 minute before next check
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // Normal shutdown — exit loop cleanly
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in scheduler service execution");
                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        
        logger.LogInformation("Signal Scheduler Service stopped");
    }

    private bool ShouldRunOvernightAnalysis(DateTime istNow, DateTime? lastRun)
    {
        var targetTime = TimeSpan.Parse(config.Scheduling.OvernightAnalysisTime);
        
        // Check if current time matches target time (within 1 minute window)
        var isTargetTime = istNow.TimeOfDay >= targetTime && 
                          istNow.TimeOfDay < targetTime.Add(TimeSpan.FromMinutes(1));
        
        // Check if we haven't run today
        var hasntRunToday = lastRun == null || lastRun.Value.Date < istNow.Date;
        
        return isTargetTime && hasntRunToday;
    }

    private async Task<bool> ShouldRunIntradayAnalysisAsync(DateTime istNow, DateTime? lastRun)
    {
        // Check if market is open
        using var scope = serviceProvider.CreateScope();
        var marketDataProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
        
        if (!await marketDataProvider.IsMarketOpenAsync())
        {
            return false;
        }
        
        // Check if it's time for next run (every 15 minutes)
        var intervalMinutes = config.Scheduling.IntradayAnalysisIntervalMinutes;
        var shouldRun = istNow.Minute % intervalMinutes == 0;
        
        // Check if we haven't run in this interval
        var hasntRunInInterval = lastRun == null || 
                                (istNow - lastRun.Value).TotalMinutes >= intervalMinutes;
        
        return shouldRun && hasntRunInInterval;
    }

    private async Task<bool> ShouldRefreshMarketDataAsync(DateTime istNow, DateTime? lastRun)
    {
        // Check if market is open
        using var scope = serviceProvider.CreateScope();
        var marketDataProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
        
        if (!await marketDataProvider.IsMarketOpenAsync())
        {
            return false;
        }
        
        // Refresh every minute during market hours
        var hasntRunInLastMinute = lastRun == null || 
                                   (istNow - lastRun.Value).TotalMinutes >= 1;
        
        return hasntRunInLastMinute;
    }

    private async Task RunOvernightAnalysisAsync()
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var signalGenerator = scope.ServiceProvider.GetRequiredService<ISignalGenerator>();
            
            var signals = await signalGenerator.GenerateSignalsAsync(SignalType.Overnight);
            
            logger.LogInformation("Overnight analysis completed: {Count} signals generated", signals.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run overnight analysis");
        }
    }

    private async Task RunIntradayAnalysisAsync()
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var signalGenerator = scope.ServiceProvider.GetRequiredService<ISignalGenerator>();
            
            var signals = await signalGenerator.GenerateSignalsAsync(SignalType.Intraday);
            
            logger.LogInformation("Intraday analysis completed: {Count} signals generated", signals.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run intraday analysis");
        }
    }

    private async Task RefreshMarketDataAsync()
    {
        try
        {
            // Cache refresh is handled automatically by MarketDataProvider
            // This is just a placeholder for any additional refresh logic
            logger.LogDebug("Market data refresh triggered");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh market data");
        }
    }

    private async Task ExpireOldSignalsAsync()
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var signalStorage = scope.ServiceProvider.GetRequiredService<ISignalStorage>();
            
            await signalStorage.ExpireOldSignalsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to expire old signals");
        }
    }
}
