using AutoTrade.Infrastructure.Data;
using AutoTrade.Domain.Models;
using AutoTrade.Application.Interfaces;
using AutoTrade.Infrastructure.Services;
using AutoTrade.Infrastructure.Services.SignalGeneration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "AutoTrade News API",
        Version = "v1",
        Description = "Indian financial news aggregation and sentiment analysis API"
    });
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000") // React dev server
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add HttpClient factory for dependency injection
builder.Services.AddHttpClient();

// Add Memory Cache for market data
builder.Services.AddMemoryCache();

// Add MongoDB context
builder.Services.AddSingleton<MongoDbContext>();

// Add Trading Signals Configuration
var tradingSignalsConfig = builder.Configuration.GetSection("TradingSignals").Get<TradingSignalsConfig>() 
    ?? new TradingSignalsConfig();
builder.Services.AddSingleton(tradingSignalsConfig);

// Add News Processing Services
builder.Services.AddSingleton<ILoughranMcDonaldAnalyzer, LoughranMcDonaldAnalyzer>();
builder.Services.AddSingleton<IHeadlineHeuristicAnalyzer, HeadlineHeuristicAnalyzer>();
builder.Services.AddSingleton<ISentimentAnalyzer, SentimentAnalyzer>();
builder.Services.AddSingleton<INewsAggregator, NewsAggregator>();
builder.Services.AddSingleton<IStockMatcher, AhoCorasickStockMatcher>();
builder.Services.AddSingleton<IStockMapper, StockMapper>();
builder.Services.AddSingleton<IArticleStorageService, ArticleStorageService>();
builder.Services.AddSingleton<INewsProcessingService, NewsProcessingService>();

// NSE Stock Refresh — seeds DB on startup and refreshes daily
builder.Services.AddSingleton<INseStockRefreshService, NseStockRefreshService>();
builder.Services.AddHostedService(sp => (NseStockRefreshService)sp.GetRequiredService<INseStockRefreshService>());

// Add Signal Generation Services
builder.Services.AddScoped<IMarketDataProvider, MarketDataProvider>();
builder.Services.AddScoped<ITechnicalAnalyzer, TechnicalAnalyzer>();
builder.Services.AddScoped<ISentimentProvider, SentimentProvider>();
builder.Services.AddScoped<IRiskManager, RiskManager>();
builder.Services.AddSingleton<IWatchlistManager, WatchlistManager>();
builder.Services.AddScoped<ISignalStorage, SignalStorage>();
builder.Services.AddScoped<ISignalGenerator, SignalGenerator>();

// Add Signal Scheduler as Hosted Service
builder.Services.AddHostedService<SignalSchedulerService>();

// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AutoTrade News API v1");
        c.RoutePrefix = "swagger"; // Set Swagger UI at /swagger
        c.DocumentTitle = "AutoTrade API Documentation";
        c.DefaultModelsExpandDepth(-1); // Hide models section by default
        c.DisplayRequestDuration();
    });
}

app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

// Start news processing service (only if services are available)
try
{
    var newsProcessingService = app.Services.GetService<INewsProcessingService>();
    if (newsProcessingService != null)
    {
        await newsProcessingService.StartProcessingAsync();
        
        // Graceful shutdown
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(async () =>
        {
            await newsProcessingService.StopProcessingAsync();
        });
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Failed to start news processing service");
}

app.Run();