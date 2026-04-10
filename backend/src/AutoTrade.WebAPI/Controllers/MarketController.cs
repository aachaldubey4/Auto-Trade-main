using AutoTrade.Domain.Models;
using AutoTrade.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace AutoTrade.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class MarketController(
    IMarketDataProvider marketData,
    TradingSignalsConfig config,
    IMemoryCache cache,
    IHttpClientFactory httpClientFactory,
    ILogger<MarketController> logger)
    : ControllerBase
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();

    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<MarketStatusDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<MarketStatusDto>), 500)]
    public async Task<ActionResult<ApiResponse<MarketStatusDto>>> GetMarketStatus()
    {
        try
        {
            var isOpen = await marketData.IsMarketOpenAsync();
            var istTimeZone = TimeZoneInfo.FindSystemTimeZoneById(config.MarketData.Timezone);
            var istNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istTimeZone);

            return Ok(new ApiResponse<MarketStatusDto>
            {
                Success = true,
                Data = new MarketStatusDto
                {
                    IsOpen = isOpen,
                    Timezone = config.MarketData.Timezone,
                    ServerTimeUtc = DateTime.UtcNow,
                    ServerTimeIst = istNow,
                    MarketOpenTime = config.MarketData.MarketOpenTime,
                    MarketCloseTime = config.MarketData.MarketCloseTime
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting market status");
            return StatusCode(500, new ApiResponse<MarketStatusDto>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An error occurred while fetching market status"
                }
            });
        }
    }

    [HttpGet("quote/{symbol}")]
    [ProducesResponseType(typeof(ApiResponse<MarketQuote>), 200)]
    [ProducesResponseType(typeof(ApiResponse<MarketQuote>), 400)]
    [ProducesResponseType(typeof(ApiResponse<MarketQuote>), 500)]
    public async Task<ActionResult<ApiResponse<MarketQuote>>> GetQuote(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return BadRequest(new ApiResponse<MarketQuote>
            {
                Success = false,
                Error = new ErrorInfo { Code = "INVALID_SYMBOL", Message = "Symbol is required" }
            });
        }

        try
        {
            var quote = await marketData.GetCurrentQuoteAsync(symbol.Trim().ToUpperInvariant());
            return Ok(new ApiResponse<MarketQuote> { Success = true, Data = quote });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching quote for {Symbol}", symbol);
            return StatusCode(500, new ApiResponse<MarketQuote>
            {
                Success = false,
                Error = new ErrorInfo { Code = "INTERNAL_ERROR", Message = "Failed to fetch quote" }
            });
        }
    }

    [HttpGet("history/{symbol}")]
    [ProducesResponseType(typeof(ApiResponse<List<OhlcData>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<List<OhlcData>>), 400)]
    [ProducesResponseType(typeof(ApiResponse<List<OhlcData>>), 500)]
    public async Task<ActionResult<ApiResponse<List<OhlcData>>>> GetHistory(string symbol, [FromQuery] int days = 30)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return BadRequest(new ApiResponse<List<OhlcData>>
            {
                Success = false,
                Error = new ErrorInfo { Code = "INVALID_SYMBOL", Message = "Symbol is required" }
            });
        }

        if (days is < 1 or > 365)
        {
            return BadRequest(new ApiResponse<List<OhlcData>>
            {
                Success = false,
                Error = new ErrorInfo { Code = "INVALID_DAYS", Message = "Days must be between 1 and 365" }
            });
        }

        try
        {
            var data = await marketData.GetHistoricalDataAsync(symbol.Trim().ToUpperInvariant(), days);
            return Ok(new ApiResponse<List<OhlcData>> { Success = true, Data = data });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching history for {Symbol}", symbol);
            return StatusCode(500, new ApiResponse<List<OhlcData>>
            {
                Success = false,
                Error = new ErrorInfo { Code = "INTERNAL_ERROR", Message = "Failed to fetch historical data" }
            });
        }
    }

    [HttpGet("index/nifty50")]
    [ProducesResponseType(typeof(ApiResponse<NiftyIndexDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<NiftyIndexDto>), 500)]
    public async Task<ActionResult<ApiResponse<NiftyIndexDto>>> GetNifty50()
    {
        try
        {
            const string cacheKey = "index_nifty50";
            if (cache.TryGetValue(cacheKey, out NiftyIndexDto? cached) && cached != null)
            {
                return Ok(new ApiResponse<NiftyIndexDto> { Success = true, Data = cached });
            }

            // Prefer NSE indices endpoint to avoid Yahoo rate limiting.
            var request = new HttpRequestMessage(HttpMethod.Get, "https://www.nseindia.com/api/allIndices");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("NSE allIndices response missing data array");
            }

            JsonElement? nifty = null;
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("index", out var idx) && idx.GetString() == "NIFTY 50")
                {
                    nifty = item;
                    break;
                }
            }

            if (nifty == null)
            {
                throw new InvalidOperationException("NIFTY 50 not found in NSE allIndices response");
            }

            var last = nifty.Value.GetProperty("last").GetDecimal();
            var percent = nifty.Value.TryGetProperty("percentChange", out var pc) && pc.ValueKind == JsonValueKind.Number
                ? pc.GetDecimal()
                : 0;

            var change = nifty.Value.TryGetProperty("change", out var ch) && ch.ValueKind == JsonValueKind.Number
                ? ch.GetDecimal()
                : (percent != 0 ? last * percent / 100 : 0);

            var dto = new NiftyIndexDto
            {
                Symbol = "NIFTY 50",
                Value = last,
                Change = change,
                ChangePercent = percent,
                Timestamp = DateTime.UtcNow
            };

            cache.Set(cacheKey, dto, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            });

            return Ok(new ApiResponse<NiftyIndexDto> { Success = true, Data = dto });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching NIFTY 50 index data");
            if (cache.TryGetValue("index_nifty50", out NiftyIndexDto? cached) && cached != null)
            {
                return Ok(new ApiResponse<NiftyIndexDto> { Success = true, Data = cached });
            }
            return StatusCode(500, new ApiResponse<NiftyIndexDto>
            {
                Success = false,
                Error = new ErrorInfo { Code = "INTERNAL_ERROR", Message = "Failed to fetch NIFTY 50 data" }
            });
        }
    }
}

public class MarketStatusDto
{
    public bool IsOpen { get; set; }
    public string Timezone { get; set; } = string.Empty;
    public DateTime ServerTimeUtc { get; set; }
    public DateTime ServerTimeIst { get; set; }
    public string MarketOpenTime { get; set; } = string.Empty;
    public string MarketCloseTime { get; set; } = string.Empty;
}

public class NiftyIndexDto
{
    public string Symbol { get; set; } = "NIFTY 50";
    public decimal Value { get; set; }
    public decimal Change { get; set; }
    public decimal ChangePercent { get; set; }
    public DateTime Timestamp { get; set; }
}

