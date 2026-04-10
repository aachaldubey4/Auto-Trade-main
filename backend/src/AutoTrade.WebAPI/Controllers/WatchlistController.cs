using AutoTrade.Infrastructure.Data;
using AutoTrade.Domain.Models;
using AutoTrade.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;

namespace AutoTrade.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class WatchlistController(
    IWatchlistManager watchlist,
    IMarketDataProvider marketData,
    ISentimentAnalyzer sentiment,
    MongoDbContext db,
    TradingSignalsConfig config,
    IMemoryCache cache,
    ILogger<WatchlistController> logger)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<WatchlistItemDto>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<List<WatchlistItemDto>>), 500)]
    public async Task<ActionResult<ApiResponse<List<WatchlistItemDto>>>> Get()
    {
        try
        {
            const string cacheKey = "watchlist_items";
            if (cache.TryGetValue(cacheKey, out List<WatchlistItemDto>? cached) && cached != null)
            {
                return Ok(new ApiResponse<List<WatchlistItemDto>> { Success = true, Data = cached });
            }

            var stocks = await watchlist.GetActiveStocksAsync();
            var items = new List<WatchlistItemDto>();
            var symbols = stocks.Select(s => s.Symbol.ToUpperInvariant()).ToList();
            var maxParallel = Math.Max(1, config.MarketData.WatchlistQuoteParallelism);
            var throttler = new SemaphoreSlim(maxParallel, maxParallel);

            var tasks = symbols.Select(async symbol =>
            {
                await throttler.WaitAsync();
                try
                {
                    MarketQuote quote;
                    try
                    {
                        quote = await marketData.GetCurrentQuoteAsync(symbol);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Skipping watchlist symbol {Symbol} due to quote failure", symbol);
                        return;
                    }

                    var sentimentOverall = "neutral";
                    try
                    {
                        var sent = await sentiment.GetNewsSentimentAsync(symbol);
                        sentimentOverall = sent.Overall;
                    }
                    catch
                    {
                        // ignore
                    }

                    var name = await GetCompanyNameAsync(symbol);
                    var change = quote.LastPrice - quote.Close;
                    var changePercent = quote.Close == 0 ? 0 : (change / quote.Close) * 100;

                    lock (items)
                    {
                        items.Add(new WatchlistItemDto
                        {
                            Symbol = symbol,
                            Name = string.IsNullOrWhiteSpace(name) ? symbol : name,
                            Price = quote.LastPrice,
                            Change = change,
                            ChangePercent = changePercent,
                            Sentiment = sentimentOverall,
                            Volume = quote.Volume
                        });
                    }
                }
                finally
                {
                    throttler.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
            var ordered = items.OrderByDescending(i => i.ChangePercent).ToList();

            cache.Set(cacheKey, ordered, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
            });

            return Ok(new ApiResponse<List<WatchlistItemDto>>
            {
                Success = true,
                Data = ordered
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching watchlist");
            if (cache.TryGetValue("watchlist_items", out List<WatchlistItemDto>? cached) && cached != null)
            {
                return Ok(new ApiResponse<List<WatchlistItemDto>> { Success = true, Data = cached });
            }
            return StatusCode(500, new ApiResponse<List<WatchlistItemDto>>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An error occurred while fetching watchlist"
                }
            });
        }
    }

    private async Task<string?> GetCompanyNameAsync(string symbol)
    {
        try
        {
            var filter = Builders<StockDocument>.Filter.Eq(s => s.Symbol, symbol);
            var doc = await db.Stocks.Find(filter).FirstOrDefaultAsync();
            return doc?.CompanyName;
        }
        catch
        {
            return null;
        }
    }
}

public class WatchlistItemDto
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Change { get; set; }
    public decimal ChangePercent { get; set; }
    public string Sentiment { get; set; } = "neutral";
    public long Volume { get; set; }
}
