using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace AutoTrade.Infrastructure.Services;

public class NewsProcessingService(
    INewsAggregator newsAggregator,
    ISentimentAnalyzer sentimentAnalyzer,
    IStockMapper stockMapper,
    IArticleStorageService articleStorage,
    ILogger<NewsProcessingService> logger)
    : INewsProcessingService, IDisposable
{
    private Timer? _processingTimer;
    private bool _isProcessing = false;
    private readonly SemaphoreSlim _processingSemaphore = new(1, 1);

    public bool IsProcessing => _isProcessing;

    public async Task StartProcessingAsync()
    {
        if (_isProcessing)
        {
            logger.LogWarning("News processing is already running");
            return;
        }

        logger.LogInformation("Starting news processing service");

        // Initialize stock database if needed
        await stockMapper.InitializeStockDatabaseAsync();

        // Start news aggregation monitoring
        await newsAggregator.StartMonitoringAsync();

        // Set up periodic processing (every 5 minutes)
        var interval = TimeSpan.FromMinutes(5);
        _processingTimer = new Timer(async _ => await ProcessNewsAsync(), null, TimeSpan.Zero, interval);

        _isProcessing = true;
        logger.LogInformation("News processing service started");
    }

    public async Task StopProcessingAsync()
    {
        if (!_isProcessing)
        {
            return;
        }

        logger.LogInformation("Stopping news processing service");

        _processingTimer?.Dispose();
        _processingTimer = null;

        await newsAggregator.StopMonitoringAsync();

        _isProcessing = false;
        logger.LogInformation("News processing service stopped");
    }

    public async Task<List<MappedArticle>> ProcessNewsAsync()
    {
        if (!await _processingSemaphore.WaitAsync(TimeSpan.FromSeconds(1)))
        {
            logger.LogDebug("News processing already in progress, skipping this cycle");
            return new List<MappedArticle>();
        }

        try
        {
            logger.LogInformation("Starting news processing cycle");
            var processedArticles = new List<MappedArticle>();

            // Step 1: Fetch raw articles from all sources
            var rawArticles = await newsAggregator.FetchAllSourcesAsync();
            
            if (!rawArticles.Any())
            {
                logger.LogInformation("No new articles to process");
                return processedArticles;
            }

            logger.LogInformation("Processing {Count} raw articles", rawArticles.Count);

            // Step 2: Filter out articles that already exist
            var newArticles = new List<RawArticle>();
            foreach (var article in rawArticles)
            {
                if (!await articleStorage.ArticleExistsAsync(article.ContentHash))
                {
                    newArticles.Add(article);
                }
            }

            logger.LogInformation("Found {Count} new articles to process", newArticles.Count);

            if (!newArticles.Any())
            {
                return processedArticles;
            }

            // Step 3: Process articles in batches
            var batchSize = 10;
            for (int i = 0; i < newArticles.Count; i += batchSize)
            {
                var batch = newArticles.Skip(i).Take(batchSize).ToList();
                var batchResults = new List<MappedArticle>();

                foreach (var rawArticle in batch)
                {
                    try
                    {
                        var mappedArticle = await ProcessSingleArticleAsync(rawArticle);
                        batchResults.Add(mappedArticle);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing article: {Title}", rawArticle.Title);
                    }
                }

                // Step 4: Store processed articles
                if (batchResults.Any())
                {
                    await articleStorage.StoreBatchAsync(batchResults);
                    processedArticles.AddRange(batchResults);
                }

                logger.LogInformation("Processed batch {BatchNumber}/{TotalBatches}: {Count} articles",
                    (i / batchSize) + 1, (newArticles.Count + batchSize - 1) / batchSize, batchResults.Count);

                // Small delay between batches
                await Task.Delay(100);
            }

            logger.LogInformation("News processing cycle completed. Processed {Count} articles", processedArticles.Count);
            return processedArticles;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in news processing cycle");
            return new List<MappedArticle>();
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }

    public async Task<MappedArticle> ProcessSingleArticleAsync(RawArticle rawArticle)
    {
        try
        {
            logger.LogDebug("Processing article: {Title}", rawArticle.Title);

            // Step 1: Sentiment analysis and categorization
            var processedArticle = await sentimentAnalyzer.AnalyzeArticleAsync(rawArticle);

            // Step 2: Stock symbol mapping
            var stockSymbols = await stockMapper.MapArticleToStocksAsync(processedArticle);

            // Step 3: Create mapped article
            var mappedArticle = new MappedArticle
            {
                Title = processedArticle.Title,
                Content = processedArticle.Content,
                Url = processedArticle.Url,
                PublishedAt = processedArticle.PublishedAt,
                Source = processedArticle.Source,
                ContentHash = processedArticle.ContentHash,
                Sentiment = processedArticle.Sentiment,
                Keywords = processedArticle.Keywords,
                Entities = processedArticle.Entities,
                MarketCategory = processedArticle.MarketCategory,
                MarketRelevance = processedArticle.MarketRelevance,
                ProcessedAt = processedArticle.ProcessedAt,
                StockSymbols = stockSymbols,
                IsGeneralMarket = !stockSymbols.Any()
            };

            logger.LogDebug("Article processed successfully: {Title} -> Stocks: [{Stocks}], Sentiment: {Sentiment}",
                mappedArticle.Title, string.Join(", ", stockSymbols), mappedArticle.Sentiment.Overall);

            return mappedArticle;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing single article: {Title}", rawArticle.Title);
            
            // Return a basic mapped article with fallback values
            return new MappedArticle
            {
                Title = rawArticle.Title,
                Content = rawArticle.Content,
                Url = rawArticle.Url,
                PublishedAt = rawArticle.PublishedAt,
                Source = rawArticle.Source,
                ContentHash = rawArticle.ContentHash,
                Sentiment = new SentimentScore
                {
                    Positive = 0.33,
                    Negative = 0.33,
                    Neutral = 0.34,
                    Overall = "neutral",
                    Confidence = 0.5
                },
                Keywords = new List<string>(),
                Entities = new List<EntityData>(),
                MarketCategory = MarketCategory.GeneralMarket,
                MarketRelevance = 0.5,
                ProcessedAt = DateTime.UtcNow,
                StockSymbols = new List<string>(),
                IsGeneralMarket = true
            };
        }
    }

    public void Dispose()
    {
        _processingTimer?.Dispose();
        _processingSemaphore?.Dispose();
    }
}