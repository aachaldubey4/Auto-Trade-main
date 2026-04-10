# AutoTrade Backend - News Sentiment Pipeline

A .NET 9 ASP.NET Core backend service for Indian financial news aggregation, sentiment analysis, and trading signal generation.

## Features

- **RSS News Aggregation**: Monitors 9 Indian financial news sources every 5 minutes
- **Full NSE Stock Coverage**: Aho-Corasick text matching against 2000+ NSE-listed companies
- **Sentiment Analysis**: Loughran-McDonald financial lexicon + Indian financial phrases + headline heuristics
- **Stock Symbol Mapping**: Maps news articles to NSE stock symbols
- **Signal Generation**: Combines technical indicators (70%) and news sentiment (30%)
- **MongoDB Storage**: Efficient storage with deduplication
- **Docker Support**: Containerized MongoDB for easy development

## Quick Start

### Prerequisites

- .NET 9 SDK
- Docker and Docker Compose

### Setup

1. **Start MongoDB**:
   ```bash
   cd backend
   docker-compose up -d mongodb
   ```

2. **Run the Application**:
   ```bash
   cd backend/src/AutoTrade.WebAPI
   dotnet run
   ```

3. **Access the API**:
   - Health Check: http://localhost:5265/api/health
   - Swagger UI: http://localhost:5265/swagger
   - Latest News: http://localhost:5265/api/news/latest
   - MongoDB UI: http://localhost:8081 (optional)

## API Endpoints

### Health Check
```
GET /api/health
GET /api/health/status
```

### News Endpoints
```
GET /api/news/latest?page=1&limit=20&stock=RELIANCE&sentiment=positive&hours=24
GET /api/news/{id}
GET /api/news/by-stock/{symbol}
GET /api/news/sentiment/{symbol}
GET /api/news/search?query=keyword
POST /api/news/process
```

### Signals Endpoints
```
GET /api/signals/active
GET /api/signals/overnight
GET /api/signals/intraday
```

### Market Endpoints
```
GET /api/market/status
GET /api/market/quote/{symbol}
GET /api/market/history/{symbol}
```

### Watchlist Endpoints
```
GET /api/watchlist
POST /api/watchlist
DELETE /api/watchlist/{symbol}
```

## Configuration (appsettings.json)

```json
{
  "NewsFeeds": {
    "MoneycontrolRss": "https://www.moneycontrol.com/rss/business.xml",
    "EconomicTimesRss": "https://economictimes.indiatimes.com/markets/rssfeeds/1977021501.cms"
  },
  "SentimentAnalysis": {
    "LexiconWeightWithContent": 0.7,
    "HeadlineWeightWithContent": 0.3,
    "LexiconWeightHeadlineOnly": 0.4,
    "HeadlineWeightHeadlineOnly": 0.6,
    "IndianPhraseMultiplier": 2.0,
    "MinContentLengthForFullAnalysis": 50
  },
  "ConnectionStrings": {
    "MongoDB": "mongodb://autotradeuser:autotradepass@localhost:27018/autotrade?authSource=autotrade"
  }
}
```

## System Architecture

### Core Services

1. **NewsProcessingService**: Orchestrates the news processing pipeline
2. **NewsAggregator**: Fetches and parses RSS feeds from 9 financial news sources
3. **SentimentAnalyzer**: Blends Loughran-McDonald lexicon + Indian phrases + headline heuristics
4. **LoughranMcDonaldAnalyzer**: Financial word lexicon with 235+ domain-specific words
5. **HeadlineHeuristicAnalyzer**: Regex-based scoring for common financial headline patterns
6. **AhoCorasickStockMatcher**: O(n) text scanning for 2000+ NSE company names
7. **NseStockRefreshService**: Daily NSE stock list refresh with static JSON fallback
8. **StockMapper**: Maps articles to NSE stock symbols
9. **ArticleStorageService**: MongoDB storage with SHA256-based deduplication

### Data Flow

```
RSS Feeds (9 sources, every 5 min)
    → NewsAggregator (fetch + dedup + HTML cleanup)
    → SentimentAnalyzer
        ├─ LoughranMcDonaldAnalyzer (lexicon + Indian phrases, uses article content > title)
        └─ HeadlineHeuristicAnalyzer (regex patterns on title)
    → StockMapper → AhoCorasickStockMatcher
    → ArticleStorageService → MongoDB
    → SignalGenerator (technical 70% + sentiment 30%)
```

### NSE Stock Data

- **Static fallback**: `backend/data/nse-stocks.json` (~2000+ equities, seeded on first run)
- **Daily refresh**: `NseStockRefreshService` updates from NSE API at 06:00 IST

## Troubleshooting

1. **MongoDB Connection**: Ensure Docker container is running on port 27018
2. **RSS Feed Errors**: Individual feed failures don't stop processing
3. **Port Conflicts**: Application runs on port 5265 by default
4. Check application logs for detailed error information
