using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Polly;
using Polly.CircuitBreaker;
using System.Text.Json;
using YahooFinanceApi;
using System.Collections.Concurrent;

namespace AutoTrade.Infrastructure.Services.SignalGeneration;

/// <summary>
/// Provides market data from NSE India API with Yahoo Finance fallback
/// </summary>
public class MarketDataProvider(
    HttpClient httpClient,
    IMemoryCache cache,
    ILogger<MarketDataProvider> logger,
    TradingSignalsConfig config)
    : IMarketDataProvider
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly TimeZoneInfo _istTimeZone = TimeZoneInfo.FindSystemTimeZoneById(config.MarketData.Timezone);
    private readonly AsyncCircuitBreakerPolicy _nseCircuitBreaker = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (exception, duration) =>
                {
                    logger.LogWarning("NSE API circuit breaker opened for {Duration}s due to: {Message}", 
                        duration.TotalSeconds, exception.Message);
                },
                onReset: () =>
                {
                    logger.LogInformation("NSE API circuit breaker reset");
                });
    private readonly SemaphoreSlim _rateLimitSemaphore = new(1, 1);
    private DateTime _lastApiCallTime = DateTime.MinValue;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _bhavcopyLocks = new();

    // NSE holidays for 2024-2026 (update annually)
    private readonly HashSet<DateTime> _marketHolidays = new()
    {
        new DateTime(2024, 1, 26),  // Republic Day
        new DateTime(2024, 3, 8),   // Mahashivratri
        new DateTime(2024, 3, 25),  // Holi
        new DateTime(2024, 3, 29),  // Good Friday
        new DateTime(2024, 4, 11),  // Id-Ul-Fitr
        new DateTime(2024, 4, 17),  // Ram Navami
        new DateTime(2024, 4, 21),  // Mahavir Jayanti
        new DateTime(2024, 5, 1),   // Maharashtra Day
        new DateTime(2024, 6, 17),  // Bakri Id
        new DateTime(2024, 7, 17),  // Muharram
        new DateTime(2024, 8, 15),  // Independence Day
        new DateTime(2024, 10, 2),  // Gandhi Jayanti
        new DateTime(2024, 11, 1),  // Diwali
        new DateTime(2024, 11, 15), // Gurunanak Jayanti
        new DateTime(2024, 12, 25), // Christmas
        // Add 2025-2026 holidays as needed
    };

    // Initialization block for primary constructor
    static MarketDataProvider() {}
    
    public async Task<MarketQuote> GetCurrentQuoteAsync(string symbol)
    {
        var cacheKey = $"quote_{symbol}";
        
        // Check cache first
        if (cache.TryGetValue(cacheKey, out MarketQuote? cachedQuote) && cachedQuote != null)
        {
            logger.LogDebug("Returning cached quote for {Symbol}", symbol);
            return cachedQuote;
        }

        try
        {
            // Try NSE India API first with circuit breaker
            var quote = await _nseCircuitBreaker.ExecuteAsync(async () => await FetchFromNseWithRetryAsync(symbol));
            
            // Cache for configured duration
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(config.MarketData.CacheDurationMinutes));
            
            cache.Set(cacheKey, quote, cacheOptions);
            
            logger.LogInformation("Fetched quote for {Symbol} from NSE: Price={Price}", symbol, quote.LastPrice);
            return quote;
        }
        catch (BrokenCircuitException)
        {
            logger.LogWarning("NSE API circuit breaker is open, falling back to Yahoo Finance for {Symbol}", symbol);
            var quote = await FetchQuoteWithFallbackAsync(symbol);
            cache.Set(cacheKey, quote, new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(config.MarketData.CacheDurationMinutes)));
            return quote;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch quote for {Symbol} from NSE, trying Yahoo Finance", symbol);
            var quote = await FetchQuoteWithFallbackAsync(symbol);
            cache.Set(cacheKey, quote, new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(config.MarketData.CacheDurationMinutes)));
            return quote;
        }
    }

    public async Task<List<OhlcData>> GetHistoricalDataAsync(string symbol, int days)
    {
        var cacheKey = $"historical_{symbol}_{days}";
        
        // Check cache first (cache historical data for 5 minutes)
        if (cache.TryGetValue(cacheKey, out List<OhlcData>? cachedData) && cachedData != null)
        {
            logger.LogDebug("Returning cached historical data for {Symbol}", symbol);
            return cachedData;
        }

        try
        {
            List<OhlcData> historicalData;
            
            try
            {
                // Prefer NSE archives (bhavcopy) to avoid Yahoo rate limiting.
                historicalData = await FetchHistoricalFromNseArchivesAsync(symbol, days);

                if (historicalData.Count < days)
                {
                    logger.LogWarning(
                        "NSE archives returned only {Count}/{Requested} trading days for {Symbol}, falling back to Yahoo Finance",
                        historicalData.Count,
                        days,
                        symbol);

                    var toDate = DateTime.UtcNow;
                    var fromDate = toDate.AddDays(-GetCalendarLookbackDays(days));
                    historicalData = await FetchHistoricalFromYahooFinanceAsync(symbol, fromDate, toDate);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "NSE archives failed for historical data of {Symbol}, trying Yahoo Finance", symbol);
                var toDate = DateTime.UtcNow;
                var fromDate = toDate.AddDays(-GetCalendarLookbackDays(days));
                historicalData = await FetchHistoricalFromYahooFinanceAsync(symbol, fromDate, toDate);
            }
            
            // Take only the requested number of trading days
            var result = historicalData.OrderByDescending(d => d.Date).Take(days).OrderBy(d => d.Date).ToList();
            
            // Cache for 5 minutes
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(5));
            
            cache.Set(cacheKey, result, cacheOptions);
            
            logger.LogInformation("Fetched {Count} days of historical data for {Symbol}", result.Count, symbol);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch historical data for {Symbol}", symbol);
            throw;
        }
    }

    private async Task<List<OhlcData>> FetchHistoricalFromNseArchivesAsync(string symbol, int days)
    {
        var result = new List<OhlcData>();
        var seenDates = new HashSet<DateTime>();
        var maxLookbackDays = GetCalendarLookbackDays(days);

        // Iterate back over calendar days until we gather enough trading days.
        var date = DateTime.UtcNow.Date;
        var attempts = 0;
        while (result.Count < days && attempts < maxLookbackDays)
        {
            attempts++;
            var d = date.AddDays(-attempts);
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

            try
            {
                var snapshot = await GetBhavcopySnapshotAsync(d);
                if (!snapshot.TryGetValue(symbol, out var row)) continue;

                if (seenDates.Add(row.Date))
                {
                    result.Add(row);
                }
            }
            catch
            {
                // Ignore and continue scanning backwards.
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException($"No historical data found for {symbol} in NSE archives");
        }

        return result;
    }

    private static int GetCalendarLookbackDays(int tradingDays)
    {
        // Trading days are typically ~5/7 of calendar days; this buffer also covers holidays.
        return Math.Max(tradingDays * 3, tradingDays + 45);
    }

    private async Task<Dictionary<string, OhlcData>> GetBhavcopySnapshotAsync(DateTime date)
    {
        var cacheKey = $"bhavcopy_snapshot_{date:yyyyMMdd}";
        if (cache.TryGetValue(cacheKey, out Dictionary<string, OhlcData>? cached) && cached != null)
        {
            return cached;
        }

        var lockObj = _bhavcopyLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await lockObj.WaitAsync();
        try
        {
            if (cache.TryGetValue(cacheKey, out Dictionary<string, OhlcData>? existing) && existing != null)
            {
                return existing;
            }

            var url = $"https://nsearchives.nseindia.com/products/content/sec_bhavdata_full_{date:ddMMyyyy}.csv";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<string, OhlcData>(StringComparer.OrdinalIgnoreCase);
            }

            var csv = await response.Content.ReadAsStringAsync();
            var parsed = ParseBhavcopySnapshot(csv);
            cache.Set(cacheKey, parsed, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, config.MarketData.BhavcopyCacheMinutes))
            });
            return parsed;
        }
        finally
        {
            lockObj.Release();
            _bhavcopyLocks.TryRemove(cacheKey, out _);
        }
    }

    private static Dictionary<string, OhlcData> ParseBhavcopySnapshot(string csv)
    {
        var result = new Dictionary<string, OhlcData>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(csv);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine)) return result;

        var headers = headerLine.Split(',').Select(h => h.Trim()).ToArray();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
        {
            index[headers[i]] = i;
        }

        if (!index.TryGetValue("SYMBOL", out var symbolIdx)) return result;
        if (!index.TryGetValue("SERIES", out var seriesIdx)) return result;
        if (!index.TryGetValue("OPEN_PRICE", out var openIdx)) return result;
        if (!index.TryGetValue("HIGH_PRICE", out var highIdx)) return result;
        if (!index.TryGetValue("LOW_PRICE", out var lowIdx)) return result;
        if (!index.TryGetValue("CLOSE_PRICE", out var closeIdx)) return result;
        if (!index.TryGetValue("TTL_TRD_QNTY", out var volIdx)) volIdx = -1;
        if (!index.TryGetValue("DATE1", out var dateIdx) && !index.TryGetValue("TIMESTAMP", out dateIdx)) dateIdx = -1;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length <= closeIdx) continue;

            if (!parts[seriesIdx].Equals("EQ", StringComparison.OrdinalIgnoreCase)) continue;

            if (!decimal.TryParse(parts[openIdx], out var open)) continue;
            if (!decimal.TryParse(parts[highIdx], out var high)) continue;
            if (!decimal.TryParse(parts[lowIdx], out var low)) continue;
            if (!decimal.TryParse(parts[closeIdx], out var close)) continue;

            long volume = 0;
            if (volIdx >= 0 && volIdx < parts.Length)
            {
                _ = long.TryParse(parts[volIdx], out volume);
            }

            DateTime date = DateTime.UtcNow.Date;
            if (dateIdx >= 0 && dateIdx < parts.Length)
            {
                _ = DateTime.TryParse(parts[dateIdx], out date);
            }

            result[parts[symbolIdx]] = new OhlcData
            {
                Date = date.Date,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            };
        }

        return result;
    }

    public Task<bool> IsMarketOpenAsync()
    {
        var istNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _istTimeZone);
        
        // Check if weekend
        if (istNow.DayOfWeek == DayOfWeek.Saturday || istNow.DayOfWeek == DayOfWeek.Sunday)
        {
            return Task.FromResult(false);
        }
        
        // Check if holiday
        if (_marketHolidays.Contains(istNow.Date))
        {
            return Task.FromResult(false);
        }
        
        // Parse market hours from config
        var openTime = TimeSpan.Parse(config.MarketData.MarketOpenTime);
        var closeTime = TimeSpan.Parse(config.MarketData.MarketCloseTime);
        
        // Check if current time is within market hours
        var isOpen = istNow.TimeOfDay >= openTime && istNow.TimeOfDay <= closeTime;
        
        return Task.FromResult(isOpen);
    }

    public Task<bool> IsMarketHolidayAsync(DateTime date)
    {
        var isHoliday = _marketHolidays.Contains(date.Date);
        return Task.FromResult(isHoliday);
    }

    private async Task<MarketQuote> FetchFromNseAsync(string symbol)
    {
        // Apply rate limiting
        await ApplyRateLimitAsync();
        
        var url = $"{config.MarketData.NseApiUrl}/quote-equity?symbol={symbol}";
        
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;
        
        // Parse NSE API response
        if (!root.TryGetProperty("priceInfo", out var priceInfo))
        {
            throw new HttpRequestException("NSE response missing priceInfo");
        }

        var lastPrice = priceInfo.GetProperty("lastPrice").GetDecimal();
        var open = priceInfo.TryGetProperty("open", out var openEl) ? openEl.GetDecimal() : lastPrice;

        var high = lastPrice;
        var low = lastPrice;
        if (priceInfo.TryGetProperty("intraDayHighLow", out var intra))
        {
            if (intra.TryGetProperty("max", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number) high = maxEl.GetDecimal();
            if (intra.TryGetProperty("min", out var minEl) && minEl.ValueKind == JsonValueKind.Number) low = minEl.GetDecimal();
        }

        var close = priceInfo.TryGetProperty("previousClose", out var prevCloseEl) && prevCloseEl.ValueKind == JsonValueKind.Number
            ? prevCloseEl.GetDecimal()
            : priceInfo.TryGetProperty("close", out var closeEl) && closeEl.ValueKind == JsonValueKind.Number
                ? closeEl.GetDecimal()
                : lastPrice;

        long volume = 0;
        if (priceInfo.TryGetProperty("totalTradedVolume", out var volEl) && volEl.ValueKind == JsonValueKind.Number)
        {
            volume = volEl.GetInt64();
        }
        
        return new MarketQuote
        {
            Symbol = symbol,
            LastPrice = lastPrice,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Applies rate limiting between API calls based on configuration
    /// </summary>
    private async Task ApplyRateLimitAsync()
    {
        await _rateLimitSemaphore.WaitAsync();
        try
        {
            var delayMs = config.MarketData.ApiCallDelayMs;
            if (delayMs > 0)
            {
                var timeSinceLastCall = DateTime.UtcNow - _lastApiCallTime;
                var requiredDelay = TimeSpan.FromMilliseconds(delayMs);
                
                if (timeSinceLastCall < requiredDelay)
                {
                    var remainingDelay = requiredDelay - timeSinceLastCall;
                    logger.LogDebug("Rate limiting: waiting {DelayMs}ms before next API call", remainingDelay.TotalMilliseconds);
                    await Task.Delay(remainingDelay);
                }
            }
            
            _lastApiCallTime = DateTime.UtcNow;
        }
        finally
        {
            _rateLimitSemaphore.Release();
        }
    }

    private async Task<List<OhlcData>> FetchHistoricalFromNseAsync(string symbol, DateTime from, DateTime to)
    {
        var fromStr = from.ToString("dd-MM-yyyy");
        var toStr = to.ToString("dd-MM-yyyy");
        var url = $"{config.MarketData.NseApiUrl}/historical/cm/equity?symbol={symbol}&from={fromStr}&to={toStr}";
        
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;
        
        var dataArray = root.GetProperty("data");
        var result = new List<OhlcData>();
        
        foreach (var item in dataArray.EnumerateArray())
        {
            result.Add(new OhlcData
            {
                Date = DateTime.Parse(item.GetProperty("CH_TIMESTAMP").GetString()!),
                Open = item.GetProperty("CH_OPENING_PRICE").GetDecimal(),
                High = item.GetProperty("CH_TRADE_HIGH_PRICE").GetDecimal(),
                Low = item.GetProperty("CH_TRADE_LOW_PRICE").GetDecimal(),
                Close = item.GetProperty("CH_CLOSING_PRICE").GetDecimal(),
                Volume = item.GetProperty("CH_TOT_TRADED_QTY").GetInt64()
            });
        }
        
        return result;
    }
    
    private async Task<MarketQuote> FetchFromYahooFinanceAsync(string symbol)
    {
        try
        {
            // Convert NSE symbol to Yahoo format (e.g., RELIANCE → RELIANCE.NS)
            var yahooSymbol = $"{symbol}.NS";
            
            logger.LogInformation("Fetching quote for {Symbol} from Yahoo Finance", yahooSymbol);
            
            // Fetch data from Yahoo Finance
            var securities = await Yahoo.Symbols(yahooSymbol).Fields(Field.Symbol, Field.RegularMarketPrice, 
                Field.RegularMarketOpen, Field.RegularMarketDayHigh, Field.RegularMarketDayLow, 
                Field.RegularMarketPreviousClose, Field.RegularMarketVolume).QueryAsync();
            
            var security = securities[yahooSymbol];
            
            var quote = new MarketQuote
            {
                Symbol = symbol,
                LastPrice = (decimal)security.RegularMarketPrice,
                Open = (decimal)security.RegularMarketOpen,
                High = (decimal)security.RegularMarketDayHigh,
                Low = (decimal)security.RegularMarketDayLow,
                Close = (decimal)security.RegularMarketPreviousClose,
                Volume = security.RegularMarketVolume,
                Timestamp = DateTime.UtcNow
            };
            
            logger.LogInformation("Successfully fetched quote for {Symbol} from Yahoo Finance: Price={Price}", 
                symbol, quote.LastPrice);
            
            return quote;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch quote for {Symbol} from Yahoo Finance", symbol);
            throw new InvalidOperationException($"Failed to fetch market data for {symbol} from both NSE and Yahoo Finance", ex);
        }
    }
    
    private async Task<List<OhlcData>> FetchHistoricalFromYahooFinanceAsync(string symbol, DateTime from, DateTime to)
    {
        try
        {
            // Convert NSE symbol to Yahoo format
            var yahooSymbol = $"{symbol}.NS";
            
            logger.LogInformation("Fetching historical data for {Symbol} from Yahoo Finance", yahooSymbol);
            
            // Fetch historical data from Yahoo Finance
            var history = await Yahoo.GetHistoricalAsync(yahooSymbol, from, to, Period.Daily);
            
            var result = history.Select(candle => new OhlcData
            {
                Date = candle.DateTime,
                Open = (decimal)candle.Open,
                High = (decimal)candle.High,
                Low = (decimal)candle.Low,
                Close = (decimal)candle.Close,
                Volume = candle.Volume
            }).ToList();
            
            logger.LogInformation("Successfully fetched {Count} days of historical data for {Symbol} from Yahoo Finance", 
                result.Count, symbol);
            
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch historical data for {Symbol} from Yahoo Finance", symbol);
            throw new InvalidOperationException($"Failed to fetch historical data for {symbol} from both NSE and Yahoo Finance", ex);
        }
    }
    private async Task<MarketQuote> FetchFromNseWithRetryAsync(string symbol)
    {
        var retries = Math.Max(0, config.MarketData.NseQuoteRetries);
        var delayMs = Math.Max(100, config.MarketData.ApiCallDelayMs);
        Exception? lastError = null;
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                return await FetchFromNseAsync(symbol);
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt == retries) break;
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs * (attempt + 1)));
            }
        }

        throw lastError ?? new InvalidOperationException($"Failed to fetch NSE quote for {symbol}");
    }

    private async Task<MarketQuote> FetchQuoteWithFallbackAsync(string symbol)
    {
        try
        {
            return await FetchFromYahooFinanceWithRetryAsync(symbol);
        }
        catch (Exception yEx)
        {
            logger.LogWarning(yEx, "Yahoo quote fallback failed for {Symbol}, trying bhavcopy fallback", symbol);
            var archiveQuote = await TryFallbackQuoteFromRecentBhavcopyAsync(symbol);
            if (archiveQuote != null)
            {
                logger.LogWarning("Using bhavcopy fallback quote for {Symbol}: {Price} ({Timestamp:O})",
                    symbol, archiveQuote.LastPrice, archiveQuote.Timestamp);
                return archiveQuote;
            }
            throw new InvalidOperationException($"Failed to fetch market data for {symbol} from NSE, Yahoo, and bhavcopy fallback", yEx);
        }
    }

    private async Task<MarketQuote> FetchFromYahooFinanceWithRetryAsync(string symbol)
    {
        var retries = Math.Max(0, config.MarketData.YahooQuoteRetries);
        var baseDelayMs = Math.Max(100, config.MarketData.YahooRetryDelayMs);
        Exception? lastError = null;
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                await ApplyRateLimitAsync();
                return await FetchFromYahooFinanceAsync(symbol);
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt == retries) break;
                var jitter = Random.Shared.Next(25, 175);
                await Task.Delay(TimeSpan.FromMilliseconds(baseDelayMs * (attempt + 1) + jitter));
            }
        }

        throw lastError ?? new InvalidOperationException($"Failed to fetch Yahoo quote for {symbol}");
    }

    private async Task<MarketQuote?> TryFallbackQuoteFromRecentBhavcopyAsync(string symbol)
    {
        var maxAgeHours = Math.Max(1, config.MarketData.QuoteFallbackMaxStalenessHours);
        var now = DateTime.UtcNow;
        var maxAge = TimeSpan.FromHours(maxAgeHours);
        for (var offset = 1; offset <= 7; offset++)
        {
            var date = now.Date.AddDays(-offset);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

            var snapshot = await GetBhavcopySnapshotAsync(date);
            if (!snapshot.TryGetValue(symbol, out var row)) continue;

            var age = now - row.Date;
            if (age > maxAge) continue;

            return new MarketQuote
            {
                Symbol = symbol,
                LastPrice = row.Close,
                Open = row.Open,
                High = row.High,
                Low = row.Low,
                Close = row.Close,
                Volume = row.Volume,
                Timestamp = row.Date
            };
        }

        logger.LogWarning("Bhavcopy fallback quote not found within staleness window ({Hours}h) for {Symbol}", maxAgeHours, symbol);
        return null;
    }
}
