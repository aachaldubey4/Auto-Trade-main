# Implementation Guide - Phases 2, 3 & 4

This document provides detailed instructions for implementing the remaining phases of the Auto Trade intraday trading signal system.

---

## Table of Contents

1. [Phase 2: Data Integration](#phase-2-data-integration)
2. [Phase 3: Signal Generation Engine](#phase-3-signal-generation-engine)
3. [Phase 4: Zerodha Kite API Integration](#phase-4-zerodha-kite-api-integration)
4. [Prerequisites & Setup](#prerequisites--setup)
5. [Architecture Overview](#architecture-overview)
6. [Testing Strategy](#testing-strategy)

---

## Phase 2: Data Integration

### Overview
Integrate TrendRadar MCP server and Indian news sources to fetch real-time market news and sentiment data.

### Objectives
- Set up TrendRadar MCP server connection
- Integrate Indian financial news RSS feeds
- Create news aggregation and processing pipeline
- Implement sentiment analysis for news articles
- Map news to stock symbols

### What You Need to Do

#### 1. Set Up TrendRadar MCP Server

**Step 1: Install TrendRadar MCP Server**

```bash
# Option 1: If TrendRadar is available as npm package
npm install @trendradar/mcp-server

# Option 2: If it's a standalone server, you'll need to:
# - Clone the repository from GitHub
# - Follow their setup instructions
# - Run it as a separate service
```

**Step 2: Configure MCP Client**

You'll need to set up an MCP client to communicate with the TrendRadar server. Create a backend service:

```bash
cd /Users/hirenadmin/Documents/Prec/Auto-Trade
mkdir backend
cd backend
dotnet new webapi -n AutoTradeBackend
cd AutoTradeBackend
dotnet add package MongoDB.Driver
dotnet add package StackExchange.Redis
```

**Step 3: Environment Variables**

Create `appsettings.json` file in backend directory:

```json
{
  "TrendRadar": {
    "McpUrl": "http://localhost:8000",
    "ApiKey": "your_api_key_if_needed"
  },
  "NewsFeeds": {
    "MoneycontrolRss": "https://www.moneycontrol.com/rss/business.xml",
    "EconomicTimesRss": "https://economictimes.indiatimes.com/markets/rssfeeds/1977021501.cms",
    "LiveMintRss": "https://www.livemint.com/rss/markets"
  },
  "ConnectionStrings": {
    "MongoDB": "mongodb://localhost:27017/autotrade",
    "Redis": "localhost:6379"
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:3001"
      }
    }
  }
}
```

#### 2. Set Up Indian News Sources

**RSS Feed Integration**

You'll need to:
1. Subscribe to Indian financial news RSS feeds
2. Parse RSS XML data
3. Extract relevant stock mentions
4. Store news articles in a database

**Required Packages:**

```bash
cd backend/AutoTradeBackend
dotnet add package System.ServiceModel.Syndication
dotnet add package HtmlAgilityPack
dotnet add package Microsoft.AspNetCore.SignalR
```

**Key RSS Feeds to Integrate:**

| Source | RSS URL | Focus |
|--------|---------|-------|
| Moneycontrol | `https://www.moneycontrol.com/rss/business.xml` | General market news |
| Economic Times | `https://economictimes.indiatimes.com/markets/rssfeeds/1977021501.cms` | Market updates |
| LiveMint | `https://www.livemint.com/rss/markets` | Market analysis |
| Business Standard | `https://www.business-standard.com/rss/markets-106081.rss` | Market news |
| NDTV Profit | `https://www.ndtv.com/business/rss` | Business news |

#### 3. Implementation Structure

**Backend Directory Structure:**

```
backend/
├── AutoTradeBackend/
│   ├── Controllers/
│   │   ├── NewsController.cs         # News API endpoints
│   │   └── HealthController.cs       # Health check endpoints
│   ├── Services/
│   │   ├── TrendRadarClient.cs       # TrendRadar MCP client
│   │   ├── NewsAggregator.cs         # RSS feed aggregation
│   │   ├── SentimentAnalyzer.cs      # Sentiment analysis
│   │   └── StockMapper.cs            # Stock symbol mapping
│   ├── Models/
│   │   ├── ArticleDocument.cs        # Article data model
│   │   ├── StockDocument.cs          # Stock data model
│   │   └── ApiResponse.cs            # API response models
│   ├── Hubs/
│   │   └── NewsHub.cs                # SignalR hub
│   ├── Data/
│   │   └── MongoDbContext.cs         # Database context
│   ├── appsettings.json
│   └── Program.cs                    # Application entry point
└── AutoTradeBackend.Tests/
    ├── Controllers/
    ├── Services/
    └── Integration/
```

#### 4. Key Implementation Files

**TrendRadar MCP Client (`backend/AutoTradeBackend/Services/TrendRadarClient.cs`):**

```csharp
using System.Text.Json;

public interface ITrendRadarClient
{
    Task ConnectAsync();
    Task DisconnectAsync();
    Task<AnalysisResult> AnalyzeArticleAsync(RawArticle article);
    Task<TrendData> DetectTrendsAsync(List<RawArticle> articles);
    Task<List<MCPTool>> GetAvailableToolsAsync();
}

public class TrendRadarClient : ITrendRadarClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TrendRadarClient> _logger;
    
    public TrendRadarClient(HttpClient httpClient, IConfiguration configuration, ILogger<TrendRadarClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }
    
    public async Task ConnectAsync()
    {
        // Connect to TrendRadar MCP server
        // Use the tools: Get Latest News, Analyze Sentiment, Search News
        var mcpUrl = _configuration["TrendRadar:McpUrl"];
        // Implementation for MCP connection
    }
    
    public async Task<AnalysisResult> AnalyzeArticleAsync(RawArticle article)
    {
        // Call TrendRadar's sentiment analysis tools
        // Return structured analysis result
        return new AnalysisResult();
    }
    
    // Additional methods...
}
```

**RSS Parser (`backend/AutoTradeBackend/Services/NewsAggregator.cs`):**

```csharp
using System.ServiceModel.Syndication;
using System.Xml;

public interface INewsAggregator
{
    Task StartMonitoringAsync();
    Task StopMonitoringAsync();
    Task<List<RawArticle>> FetchFromSourceAsync(string sourceUrl);
    Task<bool> ValidateFeedAsync(string feedUrl);
}

public class NewsAggregator : INewsAggregator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NewsAggregator> _logger;
    private readonly Timer _timer;
    
    public NewsAggregator(HttpClient httpClient, ILogger<NewsAggregator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }
    
    public async Task<List<RawArticle>> FetchFromSourceAsync(string sourceUrl)
    {
        var articles = new List<RawArticle>();
        
        try
        {
            using var reader = XmlReader.Create(sourceUrl);
            var feed = SyndicationFeed.Load(reader);
            
            foreach (var item in feed.Items)
            {
                articles.Add(new RawArticle
                {
                    Title = item.Title?.Text ?? "",
                    Content = item.Summary?.Text ?? "",
                    Url = item.Links?.FirstOrDefault()?.Uri?.ToString() ?? "",
                    PublishedAt = item.PublishDate.DateTime,
                    Source = feed.Title?.Text ?? "",
                    ContentHash = ComputeHash(item.Title?.Text + item.Summary?.Text)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch RSS feed from {Url}", sourceUrl);
        }
        
        return articles;
    }
    
    private string ComputeHash(string content)
    {
        // Implementation for content hashing
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));
    }
}
```

**Stock Mapper (`backend/AutoTradeBackend/Services/StockMapper.cs`):**

```csharp
public interface IStockMapper
{
    Task<List<string>> MapArticleToStocksAsync(ProcessedArticle article);
    Task UpdateStockDatabaseAsync();
    Task<StockSymbol?> FindStockByNameAsync(string companyName);
    Task<List<StockSymbol>> GetStocksByCategoryAsync(MarketCategory category);
}

public class StockMapper : IStockMapper
{
    private readonly IMongoCollection<StockDocument> _stocksCollection;
    private readonly ILogger<StockMapper> _logger;
    
    // Stock name to NSE symbol mappings
    private static readonly Dictionary<string, string> StockMappings = new()
    {
        { "Reliance", "RELIANCE" },
        { "Reliance Industries", "RELIANCE" },
        { "TCS", "TCS" },
        { "Tata Consultancy", "TCS" },
        // ... more mappings
    };
    
    public async Task<List<string>> MapArticleToStocksAsync(ProcessedArticle article)
    {
        var stockSymbols = new List<string>();
        var content = $"{article.Title} {article.Content}".ToLowerInvariant();
        
        foreach (var mapping in StockMappings)
        {
            if (content.Contains(mapping.Key.ToLowerInvariant()))
            {
                stockSymbols.Add(mapping.Value);
            }
        }
        
        return stockSymbols.Distinct().ToList();
    }
}
```

#### 5. API Endpoints to Create

```csharp
// GET /api/news/latest
// Returns latest news from all sources

// GET /api/news/sentiment/{symbol}
// Returns sentiment analysis for a specific stock

// GET /api/news/search?query=reliance
// Search news by keyword

// POST /api/news/analyze
// Analyze sentiment for custom text
```

### Deliverables
- ✅ TrendRadar MCP integration working
- ✅ RSS feed parser fetching Indian news
- ✅ News aggregation service running
- ✅ Stock mapping from news to symbols
- ✅ API endpoints serving news data
- ✅ Frontend consuming news API

---

## Phase 3: Signal Generation Engine

### Overview
Implement the core signal generation logic that combines news sentiment with technical indicators to generate buy/sell signals.

### Objectives
- Implement technical indicators (EMA, RSI, MACD, Volume analysis)
- Create signal scoring algorithm
- Combine news sentiment with technical analysis
- Generate actionable trading signals
- Implement risk management rules

### What You Need to Do

#### 1. Technical Indicators Library

**Required Packages:**

```bash
cd backend/AutoTradeBackend
dotnet add package Microsoft.ML
dotnet add package Microsoft.ML.TensorFlow
```

**Indicators to Implement:**

| Indicator | Purpose | Parameters |
|-----------|---------|------------|
| EMA (Exponential Moving Average) | Trend direction | Period: 20, 50 |
| RSI (Relative Strength Index) | Overbought/Oversold | Period: 14 |
| MACD | Momentum | Fast: 12, Slow: 26, Signal: 9 |
| Volume Analysis | Volume confirmation | Compare to average |
| Support/Resistance | Price levels | Dynamic calculation |

#### 2. Signal Generation Logic

**Signal Scoring Algorithm:**

```csharp
// Signal Score = (News Score × 0.4) + (Technical Score × 0.6)

public class SignalScore
{
    public double NewsScore { get; set; }        // 0-100 from sentiment analysis
    public double TechnicalScore { get; set; }   // 0-100 from indicators
    public double CombinedScore { get; set; }    // Weighted combination
    public string Confidence { get; set; }       // "high", "medium", "low"
}
```

**Entry Conditions:**

```csharp
// BUY Signal Requirements:
// 1. News sentiment > 0.6 (positive)
// 2. Price above 20-EMA
// 3. RSI between 30-70 (not overbought/oversold)
// 4. Volume > 1.5x average
// 5. Combined score > 70

// SELL Signal Requirements:
// 1. News sentiment < 0.4 (negative)
// 2. Price below 20-EMA OR RSI > 70
// 3. Volume spike (potential distribution)
// 4. Combined score > 70
```

#### 3. Implementation Structure

```
backend/
├── AutoTradeBackend/
│   ├── Services/
│   │   ├── Indicators/
│   │   │   ├── EmaCalculator.cs
│   │   │   ├── RsiCalculator.cs
│   │   │   ├── MacdCalculator.cs
│   │   │   ├── VolumeAnalyzer.cs
│   │   │   └── SupportResistanceCalculator.cs
│   │   ├── Signals/
│   │   │   ├── SignalGenerator.cs    # Main signal generation
│   │   │   ├── SignalScorer.cs       # Scoring algorithm
│   │   │   ├── SignalValidator.cs    # Validate signals
│   │   │   └── SignalTypes.cs        # Type definitions
│   │   ├── Market/
│   │   │   ├── PriceDataService.cs   # Fetch price data
│   │   │   └── HistoricalDataService.cs # Historical OHLC
│   │   └── Risk/
│   │       ├── RiskManager.cs        # Risk management
│   │       └── PositionSizer.cs      # Position sizing
```

#### 4. Key Implementation Files

**Signal Generator (`backend/AutoTradeBackend/Services/Signals/SignalGenerator.cs`):**

```csharp
public interface ISignalGenerator
{
    Task<Signal?> GenerateSignalAsync(string symbol);
}

public class SignalGenerator : ISignalGenerator
{
    private readonly IPriceDataService _priceDataService;
    private readonly ISentimentAnalyzer _sentimentAnalyzer;
    private readonly ILogger<SignalGenerator> _logger;
    
    public SignalGenerator(
        IPriceDataService priceDataService,
        ISentimentAnalyzer sentimentAnalyzer,
        ILogger<SignalGenerator> logger)
    {
        _priceDataService = priceDataService;
        _sentimentAnalyzer = sentimentAnalyzer;
        _logger = logger;
    }
    
    public async Task<Signal?> GenerateSignalAsync(string symbol)
    {
        // 1. Fetch latest price data (1-min, 5-min, 15-min candles)
        var priceData = await _priceDataService.GetPriceDataAsync(symbol, "15min", 100);
        
        // 2. Calculate technical indicators
        var ema20 = CalculateEMA(priceData, 20);
        var rsi = CalculateRSI(priceData, 14);
        var volumeAvg = CalculateVolumeAverage(priceData, 20);
        
        // 3. Get news sentiment
        var newsSentiment = await _sentimentAnalyzer.GetNewsSentimentAsync(symbol);
        
        // 4. Calculate scores
        var technicalScore = CalculateTechnicalScore(new TechnicalData
        {
            Ema20 = ema20,
            Rsi = rsi,
            Volume = priceData.Last().Volume,
            VolumeAvg = volumeAvg
        });
        
        var newsScore = newsSentiment.Score * 100;
        
        // 5. Generate signal if conditions met
        if (ShouldGenerateSignal(newsScore, technicalScore))
        {
            return CreateSignal(new SignalData
            {
                Symbol = symbol,
                Type = DetermineSignalType(newsScore, technicalScore),
                EntryPrice = priceData.Last().Close,
                // ... calculate target and stop-loss
            });
        }
        
        return null;
    }
}
```

**Risk Manager (`backend/AutoTradeBackend/Services/Risk/RiskManager.cs`):**

```csharp
public interface IRiskManager
{
    bool ValidateSignal(Signal signal, List<Position> currentPositions);
}

public class RiskManager : IRiskManager
{
    public bool ValidateSignal(Signal signal, List<Position> currentPositions)
    {
        // 1. Check max trades per day (8)
        if (currentPositions.Count >= 8) return false;
        
        // 2. Check max capital per trade (e.g., 10% of capital)
        var maxCapitalPerTrade = GetMaxCapitalPerTrade();
        if (signal.EstimatedCapital > maxCapitalPerTrade) return false;
        
        // 3. Check stop-loss is within limits (2-3%)
        var stopLossPercent = CalculateStopLossPercent(signal);
        if (stopLossPercent < 0.02 || stopLossPercent > 0.03) return false;
        
        // 4. Check target is achievable (3-5%)
        var targetPercent = CalculateTargetPercent(signal);
        if (targetPercent < 0.03 || targetPercent > 0.05) return false;
        
        return true;
    }
}
```

#### 5. Signal Processing Pipeline

```csharp
// 1. Fetch market data every 1 minute using background service
// 2. For each stock in watchlist:
//    - Calculate indicators
//    - Check news sentiment
//    - Generate signal if conditions met
// 3. Validate signal through risk manager
// 4. Store signal in database
// 5. Push to frontend via SignalR
```

#### 6. API Endpoints

```csharp
// GET /api/signals/active
// Returns all active signals

// GET /api/signals/{symbol}
// Get signal for specific stock

// POST /api/signals/generate
// Manually trigger signal generation

// GET /api/signals/history
// Historical signals for backtesting
```

### Deliverables
- ✅ Technical indicators library implemented
- ✅ Signal generation algorithm working
- ✅ News + Technical scoring system
- ✅ Risk management rules enforced
- ✅ Signal validation pipeline
- ✅ Real-time signal updates to frontend

---

## Phase 4: Zerodha Kite API Integration

### Overview
Connect to Zerodha's Kite Connect API to fetch real-time market data and enable order placement (for future automation).

### Objectives
- Set up Zerodha Kite Connect authentication
- Fetch real-time market quotes
- Get historical OHLC data
- Implement WebSocket streaming for live prices
- Set up order placement infrastructure (for future use)

### What You Need to Do

#### 1. Zerodha Account Setup

**Step 1: Create Zerodha API Application**

1. Log in to [Zerodha Kite Connect](https://kite.trade/)
2. Go to **My Apps** → **Create new app**
3. Fill in application details:
   - App name: "Auto Trade"
   - Redirect URL: `http://localhost:3001/api/kite/callback`
   - App type: Trading API
4. Note down your **API Key** and **API Secret**

**Step 2: Get Access Token**

You'll need to implement OAuth flow:

1. Redirect user to Zerodha login
2. User authorizes application
3. Zerodha redirects with request token
4. Exchange request token for access token
5. Store access token securely

**Step 3: Environment Variables**

Add to `backend/.env`:

```env
# Zerodha Kite Connect
ZERODHA_API_KEY=your_api_key
ZERODHA_API_SECRET=your_api_secret
ZERODHA_REDIRECT_URL=http://localhost:3001/api/kite/callback

# Access Token (obtained after OAuth)
ZERODHA_ACCESS_TOKEN=your_access_token
```

#### 2. Required Packages

```bash
cd backend/AutoTradeBackend
dotnet add package KiteConnect
```

**Note:** Zerodha provides `KiteConnect` NuGet package for .NET.

#### 3. Implementation Structure

```
backend/
├── AutoTradeBackend/
│   ├── Services/
│   │   ├── Zerodha/
│   │   │   ├── KiteClient.cs          # Kite Connect client
│   │   │   ├── AuthService.cs         # OAuth flow
│   │   │   ├── MarketDataService.cs   # Fetch market data
│   │   │   ├── WebSocketService.cs    # WebSocket streaming
│   │   │   └── OrderService.cs        # Order placement (future)
│   │   └── Controllers/
│   │       └── KiteController.cs      # Kite API routes
```

#### 4. Key Implementation Files

**Kite Client (`backend/AutoTradeBackend/Services/Zerodha/KiteClient.cs`):**

```csharp
using KiteConnect;

public interface IKiteClient
{
    Task<Quote> GetQuoteAsync(string instrumentToken);
    Task<Dictionary<string, Quote>> GetQuotesAsync(string[] instrumentTokens);
    Task<List<Historical>> GetHistoricalDataAsync(string instrumentToken, DateTime from, DateTime to, string interval);
    Task<List<Instrument>> GetInstrumentsAsync(string exchange = "NSE");
}

public class KiteClient : IKiteClient
{
    private readonly Kite _kite;
    private readonly IConfiguration _configuration;
    
    public KiteClient(IConfiguration configuration)
    {
        _configuration = configuration;
        var apiKey = configuration["Zerodha:ApiKey"];
        var accessToken = configuration["Zerodha:AccessToken"];
        
        _kite = new Kite(apiKey, Debug: true);
        _kite.SetAccessToken(accessToken);
    }
    
    // Get quote for single instrument
    public async Task<Quote> GetQuoteAsync(string instrumentToken)
    {
        var quotes = await _kite.GetQuoteAsync(new string[] { instrumentToken });
        return quotes[instrumentToken];
    }
    
    // Get quotes for multiple instruments
    public async Task<Dictionary<string, Quote>> GetQuotesAsync(string[] instrumentTokens)
    {
        return await _kite.GetQuoteAsync(instrumentTokens);
    }
    
    // Get historical data
    public async Task<List<Historical>> GetHistoricalDataAsync(
        string instrumentToken,
        DateTime from,
        DateTime to,
        string interval)
    {
        return await _kite.GetHistoricalDataAsync(instrumentToken, from, to, interval);
    }
    
    // Get instruments list
    public async Task<List<Instrument>> GetInstrumentsAsync(string exchange = "NSE")
    {
        return await _kite.GetInstrumentsAsync(exchange);
    }
}
```

**WebSocket Streaming (`backend/src/zerodha/websocket.ts`):**

```typescript
import { KiteTicker } from 'kiteconnect';

export class KiteWebSocket {
  private ticker: KiteTicker;
  
  connect(apiKey: string, accessToken: string, instrumentTokens: string[]) {
    this.ticker = new KiteTicker({
      api_key: apiKey,
      access_token: accessToken,
    });
    
    this.ticker.on('ticks', (ticks) => {
      // Handle real-time price updates
      this.handleTicks(ticks);
    });
    
    this.ticker.on('connect', () => {
      // Subscribe to instruments
      this.ticker.setMode(this.ticker.modeFull, instrumentTokens);
    });
    
    this.ticker.connect();
  }
  
  private handleTicks(ticks: any[]) {
    // Process ticks and update database/cache
    // Emit to frontend via WebSocket or Server-Sent Events
  }
}
```

**OAuth Flow (`backend/AutoTradeBackend/Services/Zerodha/AuthService.cs`):**

```csharp
[ApiController]
[Route("api/kite")]
public class KiteController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KiteController> _logger;
    
    public KiteController(IConfiguration configuration, ILogger<KiteController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }
    
    // Step 1: Redirect to Zerodha login
    [HttpGet("login")]
    public IActionResult Login()
    {
        var apiKey = _configuration["Zerodha:ApiKey"];
        var kite = new Kite(apiKey);
        var loginUrl = kite.GetLoginURL();
        return Redirect(loginUrl);
    }
    
    // Step 2: Handle callback
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string request_token)
    {
        try
        {
            var apiKey = _configuration["Zerodha:ApiKey"];
            var apiSecret = _configuration["Zerodha:ApiSecret"];
            var kite = new Kite(apiKey);
            
            var user = await kite.GenerateSessionAsync(request_token, apiSecret);
            
            // Store access_token securely
            // user.AccessToken
            
            return Ok(new { success = true, message = "Authentication successful" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed");
            return StatusCode(500, new { error = "Authentication failed" });
        }
    }
}
```

#### 5. Instrument Token Mapping

You'll need to map NSE stock symbols to Zerodha instrument tokens:

```csharp
// Example mapping
private static readonly Dictionary<string, uint> InstrumentTokens = new()
{
    { "RELIANCE", 738561 },  // NSE:RELIANCE
    { "TCS", 2953217 },      // NSE:TCS
    { "INFY", 408065 },      // NSE:INFY
    // ... more mappings
};

// Or fetch dynamically:
public async Task<uint> GetInstrumentTokenAsync(string symbol)
{
    var instruments = await _kiteClient.GetInstrumentsAsync("NSE");
    var instrument = instruments.FirstOrDefault(i => i.TradingSymbol == symbol);
    return instrument?.InstrumentToken ?? 0;
}
```

#### 6. API Endpoints

```csharp
// GET /api/kite/login
// Redirects to Zerodha OAuth

// GET /api/kite/callback
// OAuth callback handler

// GET /api/kite/quote/{symbol}
// Get current quote for symbol

// GET /api/kite/historical/{symbol}
// Get historical OHLC data

// GET /api/kite/instruments
// Get list of all instruments

// SignalR Hub: /newsHub
// Real-time price streaming
```

#### 7. Frontend Integration

Update frontend to consume real-time data:

```typescript
// frontend/src/services/kiteService.ts
export async function getQuote(symbol: string) {
  const response = await fetch(`/api/kite/quote/${symbol}`);
  return response.json();
}

// SignalR connection for live updates
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl("/newsHub")
  .build();

connection.start().then(() => {
  connection.on("NewsUpdate", (article, type) => {
    // Update UI with real-time news
  });
});
```

### Important Notes

1. **Rate Limits**: Zerodha has rate limits on API calls. Implement caching and respect limits.

2. **Access Token Security**: 
   - Never commit access tokens to git
   - Store in environment variables
   - Consider token refresh mechanism

3. **WebSocket Reconnection**: Implement automatic reconnection logic for SignalR.

4. **Data Subscription**: You can subscribe to multiple instruments, but there are limits.

5. **Historical Data**: Free tier has limitations. Consider Zerodha Connect API (₹2000/month) for unlimited access.

### Deliverables
- ✅ Zerodha OAuth flow implemented
- ✅ Real-time quote fetching working
- ✅ Historical data API integrated
- ✅ SignalR streaming for live prices
- ✅ Frontend consuming real-time data
- ✅ Instrument token mapping complete

---

## Prerequisites & Setup

### System Requirements

1. **Node.js** 18+ and npm (for frontend only)
2. **.NET 8+ SDK** (for backend)
3. **MongoDB** or **PostgreSQL** (for storing news, signals, trades)
4. **Redis** (optional, for caching)
5. **Zerodha Account** with API access

### Installation Steps

```bash
# 1. Backend setup
cd backend/AutoTradeBackend
dotnet restore
dotnet build

# 2. Install database (if using MongoDB)
# Follow MongoDB installation guide

# 3. Set up configuration
# Edit appsettings.json with your credentials

# 4. Start backend server
dotnet run

# 5. Frontend already set up, just ensure it's running
cd ../frontend
npm run dev
```

### Required API Keys & Credentials

1. **TrendRadar MCP**: 
   - Check if API key is needed
   - Set up MCP server locally or use hosted version

2. **Zerodha Kite Connect**:
   - API Key (from Kite Connect dashboard)
   - API Secret (from Kite Connect dashboard)
   - Access Token (obtained via OAuth)

3. **News Sources**:
   - RSS feeds are free (no API keys needed)
   - Some sources may require scraping (check ToS)

---

## Architecture Overview

```
┌─────────────────┐
│   Frontend      │
│  (React + UI)   │
└────────┬────────┘
         │ HTTP/WebSocket
         │
┌────────▼─────────────────────────┐
│      Backend API Server          │
│  (Express + Node.js)             │
├──────────────────────────────────┤
│  ┌──────────┐  ┌──────────────┐ │
│  │  News    │  │   Signal     │ │
│  │ Service  │  │  Generator   │ │
│  └────┬─────┘  └──────┬───────┘ │
│       │               │          │
│  ┌────▼──────────┬────▼──────┐  │
│  │  TrendRadar   │  Zerodha  │  │
│  │  MCP Client   │  Kite API │  │
│  └───────────────┴───────────┘  │
└──────────────────────────────────┘
         │
┌────────▼────────┐
│    Database     │
│  (MongoDB/Postgres)│
└─────────────────┘
```

---

## Testing Strategy

### Phase 2 Testing

1. **Unit Tests**: Test RSS parser, news aggregator, sentiment analyzer
2. **Integration Tests**: Test TrendRadar MCP connection
3. **Manual Testing**: Verify news is being fetched and displayed

### Phase 3 Testing

1. **Indicator Tests**: Verify EMA, RSI, MACD calculations
2. **Signal Tests**: Test signal generation logic with mock data
3. **Backtesting**: Test signals against historical data
4. **Paper Trading**: Run signals in paper trading mode

### Phase 4 Testing

1. **API Tests**: Test Zerodha API calls (use sandbox if available)
2. **WebSocket Tests**: Verify real-time data streaming
3. **Integration Tests**: End-to-end flow from data to signals

---

## Timeline Estimate

| Phase | Estimated Time | Complexity |
|-------|---------------|------------|
| Phase 2 | 1-2 weeks | Medium |
| Phase 3 | 2-3 weeks | High |
| Phase 4 | 1-2 weeks | Medium |

**Total**: 4-7 weeks

---

## Next Steps

1. **Start with Phase 2**: Set up backend structure and TrendRadar MCP
2. **Test incrementally**: Don't move to next phase until current one works
3. **Use mock data first**: Get structure right before integrating real APIs
4. **Document as you go**: Keep notes on API responses, errors, etc.

---

## Support & Resources

- **Zerodha Kite Connect Docs**: https://kite.trade/docs/connect/v3/
- **TrendRadar GitHub**: Check MCP Market for setup instructions
- **Technical Indicators**: https://www.npmjs.com/package/technicalindicators

---

## Questions to Answer Before Starting

1. Do you have a Zerodha account with API access enabled?
2. Are you comfortable setting up a backend server?
3. Do you want to use MongoDB or PostgreSQL for data storage?
4. Will you deploy this locally or on a cloud server?
5. What's your budget for data APIs (Zerodha Connect is ₹2000/month)?

---

Good luck with the implementation! Start with Phase 2 and work incrementally. 🚀
