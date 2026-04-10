using AutoTrade.Application.Interfaces;
using AutoTrade.Domain.Models;
using AutoTrade.Infrastructure.Data;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Bson;

namespace AutoTrade.Infrastructure.Services;

public class ArticleStorageService(MongoDbContext dbContext, ILogger<ArticleStorageService> logger)
    : IArticleStorageService
{
    public async Task<string> StoreArticleAsync(MappedArticle article)
    {
        try
        {
            // Check if article already exists
            if (await ArticleExistsAsync(article.ContentHash))
            {
                logger.LogDebug("Article already exists with hash: {Hash}", article.ContentHash);
                return string.Empty;
            }

            var articleDoc = MapToArticleDocument(article);

            await dbContext.Articles.InsertOneAsync(articleDoc);

            logger.LogDebug("Stored article: {Title} with ID: {Id}", article.Title, articleDoc.Id);
            return articleDoc.Id.ToString();
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            logger.LogDebug("Duplicate article detected: {Title}", article.Title);
            return string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error storing article: {Title}", article.Title);
            throw;
        }
    }

    public async Task<List<string>> StoreBatchAsync(List<MappedArticle> articles)
    {
        var storedIds = new List<string>();
        var batchSize = 100; // Process in batches to avoid memory issues

        logger.LogInformation("Starting batch storage of {Count} articles", articles.Count);

        for (int i = 0; i < articles.Count; i += batchSize)
        {
            var batch = articles.Skip(i).Take(batchSize).ToList();
            var batchDocs = new List<ArticleDocument>();

            // Filter out duplicates within the batch and against existing data
            foreach (var article in batch)
            {
                try
                {
                    if (!await ArticleExistsAsync(article.ContentHash))
                    {
                        batchDocs.Add(MapToArticleDocument(article));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error checking duplicate for article: {Title}", article.Title);
                }
            }

            if (batchDocs.Any())
            {
                try
                {
                    await dbContext.Articles.InsertManyAsync(batchDocs, new InsertManyOptions
                    {
                        IsOrdered = false // Continue inserting even if some fail
                    });

                    storedIds.AddRange(batchDocs.Select(d => d.Id.ToString()));

                    logger.LogInformation("Stored batch {BatchNumber}: {Count} articles",
                        (i / batchSize) + 1, batchDocs.Count);
                }
                catch (MongoBulkWriteException ex)
                {
                    // Handle partial success in bulk insert
                    var successCount = batchDocs.Count - ex.WriteErrors.Count;
                    logger.LogWarning("Batch insert partially failed. Success: {Success}, Errors: {Errors}",
                        successCount, ex.WriteErrors.Count);

                    // Add successful inserts to result
                    storedIds.AddRange(batchDocs.Take(successCount).Select(d => d.Id.ToString()));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in batch insert for batch {BatchNumber}", (i / batchSize) + 1);
                }
            }

            // Small delay between batches
            await Task.Delay(50);
        }

        logger.LogInformation("Batch storage completed. Stored {Count} articles", storedIds.Count);
        return storedIds;
    }

    public async Task<MappedArticle?> GetArticleByIdAsync(string id)
    {
        try
        {
            if (!ObjectId.TryParse(id, out var objectId))
            {
                return null;
            }

            var filter = Builders<ArticleDocument>.Filter.Eq(x => x.Id, objectId);
            var articleDoc = await dbContext.Articles.Find(filter).FirstOrDefaultAsync();

            return articleDoc != null ? MapToMappedArticle(articleDoc) : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting article by ID: {Id}", id);
            return null;
        }
    }

    public async Task<List<MappedArticle>> GetArticlesAsync(int page, int limit, string? stock = null, string? sentiment = null, int hours = 24)
    {
        try
        {
            var filterBuilder = Builders<ArticleDocument>.Filter;
            var filters = new List<FilterDefinition<ArticleDocument>>();

            // Time filter
            var cutoffTime = DateTime.UtcNow.AddHours(-hours);
            filters.Add(filterBuilder.Gte(x => x.PublishedAt, cutoffTime));

            // Stock filter
            if (!string.IsNullOrEmpty(stock))
            {
                filters.Add(filterBuilder.AnyEq(x => x.StockSymbols, stock.ToUpperInvariant()));
            }

            // Sentiment filter
            if (!string.IsNullOrEmpty(sentiment))
            {
                filters.Add(filterBuilder.Eq("Sentiment.Overall", sentiment.ToLowerInvariant()));
            }

            // Only processed articles
            filters.Add(filterBuilder.Eq(x => x.ProcessingStatus, "processed"));

            var combinedFilter = filterBuilder.And(filters);

            var articles = await dbContext.Articles
                .Find(combinedFilter)
                .SortByDescending(x => x.PublishedAt)
                .Skip((page - 1) * limit)
                .Limit(limit)
                .ToListAsync();

            return articles.Select(MapToMappedArticle).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting articles with filters");
            return new List<MappedArticle>();
        }
    }

    public async Task<List<MappedArticle>> GetArticlesByStockAsync(string symbol, int page, int limit)
    {
        try
        {
            var filter = Builders<ArticleDocument>.Filter.And(
                Builders<ArticleDocument>.Filter.AnyEq(x => x.StockSymbols, symbol.ToUpperInvariant()),
                Builders<ArticleDocument>.Filter.Eq(x => x.ProcessingStatus, "processed")
            );

            var articles = await dbContext.Articles
                .Find(filter)
                .SortByDescending(x => x.PublishedAt)
                .Skip((page - 1) * limit)
                .Limit(limit)
                .ToListAsync();

            return articles.Select(MapToMappedArticle).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting articles by stock: {Symbol}", symbol);
            return new List<MappedArticle>();
        }
    }

    public async Task<int> GetTotalCountAsync(string? stock = null, string? sentiment = null, int hours = 24)
    {
        try
        {
            var filterBuilder = Builders<ArticleDocument>.Filter;
            var filters = new List<FilterDefinition<ArticleDocument>>();

            // Time filter
            var cutoffTime = DateTime.UtcNow.AddHours(-hours);
            filters.Add(filterBuilder.Gte(x => x.PublishedAt, cutoffTime));

            // Stock filter
            if (!string.IsNullOrEmpty(stock))
            {
                filters.Add(filterBuilder.AnyEq(x => x.StockSymbols, stock.ToUpperInvariant()));
            }

            // Sentiment filter
            if (!string.IsNullOrEmpty(sentiment))
            {
                filters.Add(filterBuilder.Eq("Sentiment.Overall", sentiment.ToLowerInvariant()));
            }

            // Only processed articles
            filters.Add(filterBuilder.Eq(x => x.ProcessingStatus, "processed"));

            var combinedFilter = filterBuilder.And(filters);

            return (int)await dbContext.Articles.CountDocumentsAsync(combinedFilter);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting total count");
            return 0;
        }
    }

    public async Task CleanupOldArticlesAsync(int retentionDays = 30)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
            var filter = Builders<ArticleDocument>.Filter.Lt(x => x.PublishedAt, cutoffDate);

            var result = await dbContext.Articles.DeleteManyAsync(filter);

            logger.LogInformation("Cleaned up {Count} old articles older than {Days} days",
                result.DeletedCount, retentionDays);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cleaning up old articles");
            throw;
        }
    }

    public async Task<bool> ArticleExistsAsync(string contentHash)
    {
        try
        {
            var filter = Builders<ArticleDocument>.Filter.Eq(x => x.ContentHash, contentHash);
            var count = await dbContext.Articles.CountDocumentsAsync(filter);
            return count > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if article exists: {Hash}", contentHash);
            return false;
        }
    }

    private ArticleDocument MapToArticleDocument(MappedArticle article)
    {
        return new ArticleDocument
        {
            Id = ObjectId.GenerateNewId(),
            Title = article.Title,
            Content = article.Content,
            Url = article.Url,
            Source = article.Source,
            PublishedAt = article.PublishedAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ContentHash = article.ContentHash,
            Sentiment = article.Sentiment,
            Keywords = article.Keywords,
            Entities = article.Entities,
            MarketCategory = article.MarketCategory,
            MarketRelevance = article.MarketRelevance,
            StockSymbols = article.StockSymbols,
            IsGeneralMarket = article.IsGeneralMarket,
            ProcessingStatus = "processed",
            ProcessingError = null,
            SearchVector = null // Could be populated later for semantic search
        };
    }

    private MappedArticle MapToMappedArticle(ArticleDocument doc)
    {
        return new MappedArticle
        {
            Title = doc.Title,
            Content = doc.Content,
            Url = doc.Url,
            Source = doc.Source,
            PublishedAt = doc.PublishedAt,
            ContentHash = doc.ContentHash,
            Sentiment = doc.Sentiment,
            Keywords = doc.Keywords,
            Entities = doc.Entities,
            MarketCategory = doc.MarketCategory,
            MarketRelevance = doc.MarketRelevance,
            ProcessedAt = doc.UpdatedAt,
            StockSymbols = doc.StockSymbols,
            IsGeneralMarket = doc.IsGeneralMarket
        };
    }
}
