using Microsoft.AspNetCore.Mvc;
using AutoTrade.Domain.Models;
using AutoTrade.Application.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace AutoTrade.WebAPI.Controllers;

/// <summary>
/// News Controller - Provides endpoints for financial news aggregation and analysis
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class NewsController(
    IArticleStorageService articleStorage,
    ISentimentAnalyzer sentimentAnalyzer,
    INewsProcessingService newsProcessing,
    INewsAggregator newsAggregator,
    IWatchlistManager watchlistManager,
    ILogger<NewsController> logger)
    : ControllerBase
{
    [HttpGet("sources")]
    [ProducesResponseType(typeof(ApiResponse<List<FeedHealthStatus>>), 200)]
    public ActionResult<ApiResponse<List<FeedHealthStatus>>> GetSources()
    {
        var health = newsAggregator.GetFeedHealth();
        return Ok(new ApiResponse<List<FeedHealthStatus>> { Success = true, Data = health.ToList() });
    }

    [HttpGet("sentiment-summary")]
    [ProducesResponseType(typeof(ApiResponse<List<StockSentimentDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<StockSentimentDto>>>> GetSentimentSummary()
    {
        try
        {
            var stocks = await watchlistManager.GetActiveStocksAsync();
            var results = new List<StockSentimentDto>();
            foreach (var stock in stocks)
            {
                try
                {
                    var score = await sentimentAnalyzer.GetNewsSentimentAsync(stock.Symbol);
                    results.Add(new StockSentimentDto
                    {
                        Symbol = stock.Symbol,
                        Score = Math.Round(score.Positive - score.Negative, 3),
                        Overall = score.Overall,
                        Confidence = Math.Round(score.Confidence, 3)
                    });
                }
                catch
                {
                    results.Add(new StockSentimentDto { Symbol = stock.Symbol, Score = 0, Overall = "neutral", Confidence = 0 });
                }
            }
            return Ok(new ApiResponse<List<StockSentimentDto>> { Success = true, Data = results });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching sentiment summary");
            return StatusCode(500, new ApiResponse<List<StockSentimentDto>> { Success = false });
        }
    }

    /// <summary>
    /// Get latest financial news with optional filtering
    /// </summary>
    /// <param name="page">Page number (default: 1)</param>
    /// <param name="limit">Number of articles per page (1-100, default: 20)</param>
    /// <param name="stock">Filter by stock symbol (e.g., RELIANCE, TCS)</param>
    /// <param name="sentiment">Filter by sentiment (positive, negative, neutral)</param>
    /// <param name="hours">Articles from last N hours (1-168, default: 24)</param>
    /// <returns>Paginated list of news articles with sentiment analysis and stock mappings</returns>
    /// <response code="200">Returns the paginated news articles</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("latest")]
    [ProducesResponseType(typeof(ApiResponse<NewsResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<NewsResponse>), 500)]
    public async Task<ActionResult<ApiResponse<NewsResponse>>> GetLatestNews(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int limit = 20,
        [FromQuery] string? stock = null,
        [FromQuery] string? sentiment = null,
        [FromQuery, Range(1, 168)] int hours = 24)
    {
        try
        {
            // Validate parameters
            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 20;
            if (hours < 1 || hours > 168) hours = 24; // Max 1 week

            var articles = await articleStorage.GetArticlesAsync(page, limit, stock, sentiment, hours);
            var totalCount = await articleStorage.GetTotalCountAsync(stock, sentiment, hours);

            var response = new NewsResponse
            {
                Articles = articles,
                TotalCount = totalCount,
                Filters = new AppliedFilters
                {
                    Stock = stock,
                    Sentiment = sentiment,
                    Hours = hours
                }
            };

            var pagination = new PaginationInfo
            {
                Page = page,
                Limit = limit,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / limit),
                HasNext = page * limit < totalCount,
                HasPrevious = page > 1
            };

            return Ok(new ApiResponse<NewsResponse>
            {
                Success = true,
                Data = response,
                Pagination = pagination
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting latest news");
            return StatusCode(500, new ApiResponse<NewsResponse>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An error occurred while fetching news"
                }
            });
        }
    }

    /// <summary>
    /// Get a specific news article by its unique identifier
    /// </summary>
    /// <param name="id">The unique article identifier</param>
    /// <returns>The requested news article with full details</returns>
    /// <response code="200">Returns the requested article</response>
    /// <response code="404">Article not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<MappedArticle>), 200)]
    [ProducesResponseType(typeof(ApiResponse<MappedArticle>), 404)]
    [ProducesResponseType(typeof(ApiResponse<MappedArticle>), 500)]
    public async Task<ActionResult<ApiResponse<MappedArticle>>> GetNewsById([Required] string id)
    {
        try
        {
            var article = await articleStorage.GetArticleByIdAsync(id);

            if (article == null)
            {
                return NotFound(new ApiResponse<MappedArticle>
                {
                    Success = false,
                    Error = new ErrorInfo
                    {
                        Code = "NOT_FOUND",
                        Message = "Article not found"
                    }
                });
            }

            return Ok(new ApiResponse<MappedArticle>
            {
                Success = true,
                Data = article
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting article by ID: {Id}", id);
            return StatusCode(500, new ApiResponse<MappedArticle>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An error occurred while fetching the article"
                }
            });
        }
    }

    /// <summary>
    /// Get news articles related to a specific stock symbol
    /// </summary>
    /// <param name="symbol">NSE stock symbol (e.g., RELIANCE, TCS, HDFCBANK)</param>
    /// <param name="page">Page number (default: 1)</param>
    /// <param name="limit">Number of articles per page (1-50, default: 10)</param>
    /// <returns>Paginated list of news articles for the specified stock</returns>
    /// <response code="200">Returns news articles for the stock</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("by-stock/{symbol}")]
    [ProducesResponseType(typeof(ApiResponse<NewsResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<NewsResponse>), 500)]
    public async Task<ActionResult<ApiResponse<NewsResponse>>> GetNewsByStock(
        [Required] string symbol,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 50)] int limit = 10)
    {
        try
        {
            if (page < 1) page = 1;
            if (limit < 1 || limit > 50) limit = 10;

            var articles = await articleStorage.GetArticlesByStockAsync(symbol, page, limit);
            var totalCount = await articleStorage.GetTotalCountAsync(symbol);

            var response = new NewsResponse
            {
                Articles = articles,
                TotalCount = totalCount,
                Filters = new AppliedFilters
                {
                    Stock = symbol
                }
            };

            var pagination = new PaginationInfo
            {
                Page = page,
                Limit = limit,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / limit),
                HasNext = page * limit < totalCount,
                HasPrevious = page > 1
            };

            return Ok(new ApiResponse<NewsResponse>
            {
                Success = true,
                Data = response,
                Pagination = pagination
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting news by stock: {Symbol}", symbol);
            return StatusCode(500, new ApiResponse<NewsResponse>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An error occurred while fetching news for the stock"
                }
            });
        }
    }

    /// <summary>
    /// Get aggregated sentiment analysis for a specific stock symbol
    /// </summary>
    /// <param name="symbol">NSE stock symbol (e.g., RELIANCE, TCS, HDFCBANK)</param>
    /// <returns>Sentiment analysis scores and overall sentiment for the stock</returns>
    /// <response code="200">Returns sentiment analysis for the stock</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("sentiment/{symbol}")]
    [ProducesResponseType(typeof(ApiResponse<SentimentScore>), 200)]
    [ProducesResponseType(typeof(ApiResponse<SentimentScore>), 500)]
    public async Task<ActionResult<ApiResponse<SentimentScore>>> GetSentimentByStock([Required] string symbol)
    {
        try
        {
            var sentiment = await sentimentAnalyzer.GetNewsSentimentAsync(symbol);

            return Ok(new ApiResponse<SentimentScore>
            {
                Success = true,
                Data = sentiment
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting sentiment for stock: {Symbol}", symbol);
            return StatusCode(500, new ApiResponse<SentimentScore>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An error occurred while analyzing sentiment"
                }
            });
        }
    }

    /// <summary>
    /// Search news articles by keyword or phrase
    /// </summary>
    /// <param name="query">Search query (keywords or phrases)</param>
    /// <param name="page">Page number (default: 1)</param>
    /// <param name="limit">Number of articles per page (1-50, default: 20)</param>
    /// <returns>Paginated list of news articles matching the search query</returns>
    /// <response code="200">Returns search results</response>
    /// <response code="400">Invalid search query</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("search")]
    [ProducesResponseType(typeof(ApiResponse<NewsResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<NewsResponse>), 400)]
    [ProducesResponseType(typeof(ApiResponse<NewsResponse>), 500)]
    public async Task<ActionResult<ApiResponse<NewsResponse>>> SearchNews(
        [FromQuery, Required] string query,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 50)] int limit = 20)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest(new ApiResponse<NewsResponse>
                {
                    Success = false,
                    Error = new ErrorInfo
                    {
                        Code = "INVALID_QUERY",
                        Message = "Search query is required"
                    }
                });
            }

            if (page < 1) page = 1;
            if (limit < 1 || limit > 50) limit = 20;

            // For now, return empty results as we haven't implemented full-text search
            // This would require additional indexing and search implementation
            var response = new NewsResponse
            {
                Articles = new List<MappedArticle>(),
                TotalCount = 0,
                Filters = new AppliedFilters()
            };

            return Ok(new ApiResponse<NewsResponse>
            {
                Success = true,
                Data = response,
                Pagination = new PaginationInfo
                {
                    Page = page,
                    Limit = limit,
                    TotalCount = 0,
                    TotalPages = 0,
                    HasNext = false,
                    HasPrevious = false
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching news with query: {Query}", query);
            return StatusCode(500, new ApiResponse<NewsResponse>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An error occurred while searching news"
                }
            });
        }
    }

    /// <summary>
    /// Manually trigger news processing and analysis
    /// </summary>
    /// <returns>Processing result with count of articles processed</returns>
    /// <response code="200">News processing completed successfully</response>
    /// <response code="409">News processing is already in progress</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("process")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 409)]
    [ProducesResponseType(typeof(ApiResponse<object>), 500)]
    public async Task<ActionResult<ApiResponse<object>>> ProcessNews()
    {
        try
        {
            if (newsProcessing.IsProcessing)
            {
                return Conflict(new ApiResponse<object>
                {
                    Success = false,
                    Error = new ErrorInfo
                    {
                        Code = "PROCESSING_IN_PROGRESS",
                        Message = "News processing is already in progress"
                    }
                });
            }

            var processedArticles = await newsProcessing.ProcessNewsAsync();

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = new
                {
                    ProcessedCount = processedArticles.Count,
                    Message = $"Successfully processed {processedArticles.Count} articles"
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error manually processing news");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An error occurred while processing news"
                }
            });
        }
    }
}

public class StockSentimentDto
{
    public string Symbol { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Overall { get; set; } = "neutral";
    public double Confidence { get; set; }
}
