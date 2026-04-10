using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Bson;
using AutoTrade.Domain.Models;

namespace AutoTrade.Infrastructure.Data;

public class MongoDbContext(IConfiguration configuration, ILogger<MongoDbContext> logger)
{
    private readonly IMongoDatabase _database = InitializeDatabase(configuration, logger);

    public IMongoCollection<ArticleDocument> Articles => _database.GetCollection<ArticleDocument>("articles");
    public IMongoCollection<StockDocument> Stocks => _database.GetCollection<StockDocument>("stocks");
    public IMongoCollection<SignalDocument> Signals => _database.GetCollection<SignalDocument>("signals");

    private static IMongoDatabase InitializeDatabase(IConfiguration configuration, ILogger logger)
    {
        try
        {
            var connectionString = configuration.GetConnectionString("MongoDB") 
                ?? throw new ArgumentNullException("MongoDB connection string is required");
            
            var client = new MongoClient(connectionString);
            var databaseName = configuration["Database:DatabaseName"] ?? "autotrade";
            var database = client.GetDatabase(databaseName);
            
            // Test connection
            database.RunCommand<BsonDocument>(new BsonDocument("ping", 1));
            logger.LogInformation("Connected to MongoDB database: {DatabaseName}", databaseName);
            
            return database;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to MongoDB. The application will continue but database operations will fail.");
            throw;
        }
    }

    private async Task InitializeIndexesAsync()
    {
        try
        {
            await CreateArticleIndexesAsync();
            await CreateStockIndexesAsync();
            await CreateSignalIndexesAsync();
            logger.LogInformation("Database indexes initialized successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize database indexes");
            throw;
        }
    }

    private async Task CreateArticleIndexesAsync()
    {
        var articlesCollection = Articles;

        // Index on PublishedAt (descending for latest first)
        await articlesCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<ArticleDocument>(
                Builders<ArticleDocument>.IndexKeys.Descending(x => x.PublishedAt),
                new CreateIndexOptions { Name = "idx_publishedAt" }));

        // Compound index on StockSymbols and PublishedAt
        await articlesCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<ArticleDocument>(
                Builders<ArticleDocument>.IndexKeys
                    .Ascending(x => x.StockSymbols)
                    .Descending(x => x.PublishedAt),
                new CreateIndexOptions { Name = "idx_stockSymbols_publishedAt" }));

        // Compound index on MarketCategory and PublishedAt
        await articlesCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<ArticleDocument>(
                Builders<ArticleDocument>.IndexKeys
                    .Ascending(x => x.MarketCategory)
                    .Descending(x => x.PublishedAt),
                new CreateIndexOptions { Name = "idx_marketCategory_publishedAt" }));

        // Compound index on Sentiment.Overall and PublishedAt
        await articlesCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<ArticleDocument>(
                Builders<ArticleDocument>.IndexKeys
                    .Ascending("Sentiment.Overall")
                    .Descending(x => x.PublishedAt),
                new CreateIndexOptions { Name = "idx_sentiment_publishedAt" }));

        // Unique index on ContentHash for duplicate prevention
        await articlesCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<ArticleDocument>(
                Builders<ArticleDocument>.IndexKeys.Ascending(x => x.ContentHash),
                new CreateIndexOptions { Unique = true, Name = "idx_contentHash_unique" }));

        // Index on Keywords for text search
        await articlesCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<ArticleDocument>(
                Builders<ArticleDocument>.IndexKeys.Ascending(x => x.Keywords),
                new CreateIndexOptions { Name = "idx_keywords" }));

        // Index on ProcessingStatus for monitoring
        await articlesCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<ArticleDocument>(
                Builders<ArticleDocument>.IndexKeys.Ascending(x => x.ProcessingStatus),
                new CreateIndexOptions { Name = "idx_processingStatus" }));
    }

    private async Task CreateStockIndexesAsync()
    {
        var stocksCollection = Stocks;

        // Unique index on Symbol
        await stocksCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<StockDocument>(
                Builders<StockDocument>.IndexKeys.Ascending(x => x.Symbol),
                new CreateIndexOptions { Unique = true, Name = "idx_symbol_unique" }));

        // Index on SearchTerms for fuzzy matching
        await stocksCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<StockDocument>(
                Builders<StockDocument>.IndexKeys.Ascending(x => x.SearchTerms),
                new CreateIndexOptions { Name = "idx_searchTerms" }));

        // Index on Sector for category filtering
        await stocksCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<StockDocument>(
                Builders<StockDocument>.IndexKeys.Ascending(x => x.Sector),
                new CreateIndexOptions { Name = "idx_sector" }));

        // Index on IsActive for filtering active stocks
        await stocksCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<StockDocument>(
                Builders<StockDocument>.IndexKeys.Ascending(x => x.IsActive),
                new CreateIndexOptions { Name = "idx_isActive" }));
    }
    
    private async Task CreateSignalIndexesAsync()
    {
        var signalsCollection = Signals;

        // Compound index on Status and ExpiresAt for active signal queries
        await signalsCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<SignalDocument>(
                Builders<SignalDocument>.IndexKeys
                    .Ascending(x => x.Status)
                    .Ascending(x => x.ExpiresAt),
                new CreateIndexOptions { Name = "idx_status_expiresAt" }));

        // Compound index on Symbol and GeneratedAt for symbol-specific queries
        await signalsCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<SignalDocument>(
                Builders<SignalDocument>.IndexKeys
                    .Ascending(x => x.Symbol)
                    .Descending(x => x.GeneratedAt),
                new CreateIndexOptions { Name = "idx_symbol_generatedAt" }));

        // Compound index on Type and Status for overnight/intraday filtering
        await signalsCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<SignalDocument>(
                Builders<SignalDocument>.IndexKeys
                    .Ascending(x => x.Type)
                    .Ascending(x => x.Status),
                new CreateIndexOptions { Name = "idx_type_status" }));

        // Index on GeneratedAt for historical queries
        await signalsCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<SignalDocument>(
                Builders<SignalDocument>.IndexKeys.Descending(x => x.GeneratedAt),
                new CreateIndexOptions { Name = "idx_generatedAt" }));
    }
}