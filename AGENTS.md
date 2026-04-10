# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Project Overview

Auto-Trade is a full-stack intraday trading signal system for Indian NSE stocks. It combines technical analysis (EMA, RSI, MACD) with news sentiment scoring to generate buy/sell signals, stored in MongoDB and surfaced via a React dashboard.

## Development Commands

### Backend (.NET 9)

```bash
# Start MongoDB (required before running backend)
cd backend
docker-compose up -d mongodb

# Run the API
cd backend/src/AutoTrade.WebAPI
dotnet restore
dotnet run
# API runs on http://localhost:5265
# Swagger UI at http://localhost:5265/swagger
```

### Frontend (React + Vite)

```bash
cd frontend
npm install
npm run dev       # http://localhost:5173
npm run build     # Production build to dist/
npm run lint      # ESLint
npm run preview   # Preview production build
```

### Environment Variables (Frontend)

Copy `.env` and adjust:
- `VITE_API_URL` — backend base URL (default: `http://localhost:5265/api`)
- `VITE_API_TIMEOUT` — request timeout in ms (default: `10000`)
- `VITE_ENABLE_NOTIFICATIONS` — toast notifications toggle

## Architecture

### Backend: 4-Layer Clean Architecture

```
Domain → Application → Infrastructure → WebAPI
```

- **Domain** (`AutoTrade.Domain`): Pure entities with no external dependencies — `ArticleDocument`, `SignalDocument`, `StockDocument`, `TradingSignal`, `TradingSignalsConfig`.
- **Application** (`AutoTrade.Application`): Interfaces only — `ISignalGenerator`, `ITechnicalAnalyzer`, `ISentimentAnalyzer`, `ILoughranMcDonaldAnalyzer`, `IHeadlineHeuristicAnalyzer`, `IRiskManager`, `INewsAggregator`, `IStockMapper`, `IStockMatcher`, `IWatchlistManager`.
- **Infrastructure** (`AutoTrade.Infrastructure`): All concrete implementations. Two sub-namespaces:
  - `Services/` — news pipeline: `NewsAggregator` (RSS), `LoughranMcDonaldAnalyzer` (financial lexicon + Indian phrases), `HeadlineHeuristicAnalyzer` (regex-based headline scoring), `SentimentAnalyzer` (blends L-M + headline), `AhoCorasickStockMatcher` (O(n) company name matching for 2000+ NSE stocks), `StockMapper` (delegates to AhoCorasick), `NseStockRefreshService` (daily NSE stock refresh), `ArticleStorageService` (MongoDB dedup)
  - `Services/SignalGeneration/` — signal pipeline: `SignalGenerator`, `TechnicalAnalyzer`, `MarketDataProvider` (NSE primary / Yahoo Finance fallback), `RiskManager`, `SignalSchedulerService` (background `IHostedService`)
- **WebAPI** (`AutoTrade.WebAPI`): Controllers with rate limiting — `HealthController`, `NewsController`, `SignalsController`, `MarketController`, `WatchlistController`.

### Signal Generation Flow

```
SignalSchedulerService (every 15 min)
  → MarketDataProvider (NSE API → Yahoo Finance fallback)
  → TechnicalAnalyzer (EMA20, RSI14, MACD, Volume ratio)
  → SentimentProvider (from MongoDB news cache)
  → SignalGenerator scores: Technical (70%) + Sentiment (30%)
  → RiskManager validates (min strength 70/100, max 8 concurrent)
  → SignalStorage → MongoDB
  → Frontend polls /api/signals/active every 30s via React Query
```

### News Sentiment Pipeline

```
RSS Feeds (9 sources, every 5 min)
  → NewsAggregator (fetch + SHA256 dedup + HTML cleanup)
  → SentimentAnalyzer
      ├─ LoughranMcDonaldAnalyzer: scores article.Content (if >50 chars), else article.Title
      │   Uses: Loughran-McDonald financial lexicon (~235 words) + Indian financial phrases
      └─ HeadlineHeuristicAnalyzer: regex patterns on article.Title (surge/fall/bags order/etc.)
      → Blended score: 70% L-M + 30% headline (with content); 40% L-M + 60% headline (title only)
  → StockMapper → AhoCorasickStockMatcher: O(n) scan for 2000+ NSE company names/aliases
  → ArticleStorageService → MongoDB
```

### NSE Stock Data

- **Static fallback**: `backend/data/nse-stocks.json` (~2000+ equities, seeded on first run if DB is empty)
- **Daily refresh**: `NseStockRefreshService` fetches from NSE API at 06:00 IST, upserts MongoDB, rebuilds Aho-Corasick trie

### Frontend Architecture

React Query (`@tanstack/react-query`) handles all server state with 15s stale time and 2 retries. Zustand is wired up for client state. All API calls go through `src/services/api.ts`. Custom hooks in `src/hooks/` encapsulate React Query calls (one hook per domain concept).

### Data Storage

- **MongoDB** (port 27018 via docker-compose): Articles, signals, watchlist, stocks
- **In-memory cache** (`IMemoryCache`): Market quotes, 1-minute TTL
- **Redis** (configured in `appsettings.json` but optional): Not required for development

## Key Configuration (`backend/src/AutoTrade.WebAPI/appsettings.json`)

Signal thresholds most likely to need tuning:
- `MinimumStrength: 70` — minimum signal score (0–100) to emit a signal
- `TechnicalWeight: 0.7` / `SentimentWeight: 0.3` — scoring mix
- `BuyConditions.MinSentimentScore: 0.6` / `SellConditions.MaxSentimentScore: 0.4`
- `StopLossPercentageMin/Max: 2–3%`, `TargetPercentageMin/Max: 2–5%`
- `MaxConcurrentSignals: 8`, `PositionSizePercentage: 12.5`
- `OvernightAnalysisTime: "20:00"`, `IntradayIntervalMinutes: 15`

Sentiment blending weights (tunable without recompile):
- `SentimentAnalysis.LexiconWeightWithContent: 0.7` — L-M weight when article has content
- `SentimentAnalysis.HeadlineWeightHeadlineOnly: 0.6` — headline heuristic weight when title-only
- `SentimentAnalysis.IndianPhraseMultiplier: 2.0` — weight multiplier for Indian financial phrases

Default watchlist (10 stocks): RELIANCE, TCS, INFY, HDFCBANK, ICICIBANK, SBIN, HINDUNILVR, ITC, KOTAKBANK, BHARTIARTL

## External Integrations

- **NSE API** — primary market data source (intraday OHLC) and daily stock list refresh
- **Yahoo Finance** (`YahooFinanceApi`) — fallback when NSE is unavailable
- **RSS Feeds** — 9 sources: Moneycontrol, Economic Times, LiveMint, Business Standard, NDTV Profit, Financial Express, CNBC TV18, Hindu BusinessLine (+ LiveMint Companies)

## Dependency Injection Entry Point

`backend/src/AutoTrade.WebAPI/Program.cs` wires all services. When adding new infrastructure services, register them here and declare the interface in Application layer. CORS is pre-configured for `http://localhost:5173`.
