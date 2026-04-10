# RSS News Sentiment Pipeline Redesign

## Context

The current news sentiment pipeline depends on TrendRadar MCP server for AI-powered article analysis, but TrendRadar is not a good fit — it requires a separate server process and adds complexity without proportional value. The existing Loughran-McDonald financial lexicon fallback already handles sentiment reasonably well. Meanwhile, the stock mapper only knows ~40 hardcoded companies, missing most NSE-listed stocks in news articles.

This redesign removes TrendRadar entirely, expands stock coverage to the full NSE list (~2000+ equities), and enhances sentiment analysis with Indian market-specific phrases and headline pattern scoring.

## Changes Overview

1. **Remove TrendRadar** — delete client, interface, config, all documentation references
2. **Full NSE stock list** — static JSON fallback + daily API refresh, stored in MongoDB
3. **Aho-Corasick stock matching** — O(n) text scanning for 2000+ company names per article
4. **Enhanced sentiment** — Indian financial phrases, headline heuristic scoring, use article description when available
5. **More RSS feeds** — add 5 new Indian financial news sources
6. **Configurable sentiment weights** — tune L-M vs headline heuristic ratios in appsettings.json

---

## 1. TrendRadar Removal

### Files to delete
- `backend/src/AutoTrade.Application/Interfaces/ITrendRadarClient.cs`
- `backend/src/AutoTrade.Infrastructure/Services/TrendRadarClient.cs`

### Files to modify
- `backend/src/AutoTrade.Domain/Models/SystemConfig.cs` — remove `TrendRadarConfig` class
- `backend/src/AutoTrade.WebAPI/Program.cs` — remove DI registration for `ITrendRadarClient`
- `backend/src/AutoTrade.WebAPI/appsettings.json` — remove `TrendRadar` config section
- `backend/src/AutoTrade.WebAPI/appsettings.Development.json` — remove `TrendRadar` config section
- `backend/src/AutoTrade.Infrastructure/Services/SentimentAnalyzer.cs` — remove `ITrendRadarClient` dependency, rewrite to use L-M + headline heuristic
- `backend/src/AutoTrade.WebAPI/Controllers/HealthController.cs` — remove TrendRadar health check fields

### Documentation to update
- `README.md` (root) — remove Phase 2 TrendRadar mention
- `backend/README.md` — remove all TrendRadar references
- `CLAUDE.md` — remove TrendRadar from external integrations
- `IMPLEMENTATION_GUIDE.md` — remove TrendRadar setup sections
- `.kiro/specs/trendradar-news-integration/` — remove or archive this directory

---

## 2. Full NSE Stock Data Pipeline

### Static fallback file
- Path: `backend/data/nse-stocks.json`
- Format: array of `{ symbol, companyName, isin, series, sector }`
- Contains ~2000+ NSE equities
- Committed to repo, updated periodically via manual download from NSE website

### NseStockRefreshService (new IHostedService)
- Path: `backend/src/AutoTrade.Infrastructure/Services/NseStockRefreshService.cs`
- Interface: `backend/src/AutoTrade.Application/Interfaces/INseStockRefreshService.cs`
- Runs daily at 06:00 IST (before market open)
- On startup: if MongoDB `stocks` collection has <100 entries, seed from static JSON
- Daily: fetch from NSE API endpoints, upsert into MongoDB
- After refresh: trigger Aho-Corasick trie rebuild
- NSE endpoints: `https://www.nseindia.com/api/equity-stockIndices?index=SECURITIES%20IN%20F%26O` and similar
- Handles NSE rate limiting (500ms delay between requests, proper headers/cookies)

### StockDocument model additions
Add to existing `backend/src/AutoTrade.Domain/Models/StockDocument.cs`:
- `ISIN` (string) — unique NSE identifier
- `Series` (string) — EQ, BE, SM, etc.

---

## 3. Aho-Corasick Stock Matcher

### Interface
- Path: `backend/src/AutoTrade.Application/Interfaces/IStockMatcher.cs`
```csharp
public interface IStockMatcher
{
    List<string> FindMentionedStocks(string text);
    Task RebuildIndexAsync();
}
```

### Implementation
- Path: `backend/src/AutoTrade.Infrastructure/Services/AhoCorasickStockMatcher.cs`
- NuGet dependency: a suitable Aho-Corasick library (e.g., `Aho-Corasick-dotnet`)
- On construction: load all StockDocument from MongoDB, build trie from symbol + companyName + all aliases (lowercased)
- Each pattern maps back to its NSE symbol
- `FindMentionedStocks(text)`: returns deduplicated list of matched symbols
- `RebuildIndexAsync()`: builds new trie, swaps atomically via `Interlocked.Exchange`
- Thread-safe: readers use the current trie reference, rebuild creates a new one and swaps

### StockMapper refactor
- `backend/src/AutoTrade.Infrastructure/Services/StockMapper.cs`
- `MapArticleToStocksAsync` delegates to `IStockMatcher.FindMentionedStocks()` instead of the current 4-step approach
- The hardcoded dictionaries, aliases, and regex extraction logic are replaced by the Aho-Corasick matcher
- `FindStockByNameAsync` and other lookup methods remain for direct queries

---

## 4. Enhanced Sentiment Analysis

### Indian financial phrases
Added to `LoughranMcDonaldAnalyzer`:

**Positive phrases**: "beats estimates", "bags order", "record profit", "RBI rate cut", "strong quarterly results", "FII inflow", "upgrade rating", "market outperform", "order win", "revenue growth", "margin expansion", "dividend declared", "buyback approved", "expansion plan"

**Negative phrases**: "misses estimates", "promoter pledge", "SEBI penalty", "NPA rises", "downgrade rating", "FII outflow", "debt default", "rights issue", "fraud allegation", "revenue decline", "margin pressure", "stake sale by promoter", "regulatory action", "import duty hike"

**Uncertainty phrases**: "SEBI review", "RBI policy review", "merger speculation", "delisting rumor", "management change"

Phrases are checked via case-insensitive string containment on the full article text. Each phrase match is weighted at `IndianPhraseMultiplier` (default: 2.0x) relative to single-word matches.

### Text source priority
The analyzer scores the best available text:
1. `article.Content` (description/summary) if present and length > `MinContentLengthForFullAnalysis` (default: 50 chars)
2. `article.Title` as fallback

### HeadlineHeuristicAnalyzer (new)
- Path: `backend/src/AutoTrade.Infrastructure/Services/HeadlineHeuristicAnalyzer.cs`
- Interface: `backend/src/AutoTrade.Application/Interfaces/IHeadlineHeuristicAnalyzer.cs`
- Returns a sentiment adjustment value (-1.0 to +1.0)

Regex patterns:
| Pattern | Sentiment Adjustment |
|---------|---------------------|
| `surges?\|soars?\|rallies\|jumps?\s+\d+%` | +0.3 |
| `falls?\|drops?\|slumps?\|crashes?\s+\d+%` | -0.3 |
| `bags?\s+order\|wins?\s+contract` | +0.2 |
| `reports?\s+loss\|net loss` | -0.2 |
| `hits?\s+(52-week\|all.time)\s+high` | +0.2 |
| `hits?\s+(52-week\|all.time)\s+low` | -0.2 |
| `beats?\s+estimates?\|above\s+expectations?` | +0.25 |
| `misses?\s+estimates?\|below\s+expectations?` | -0.25 |
| `upgrade[ds]?\s+(to\|by)` | +0.15 |
| `downgrade[ds]?\s+(to\|by)` | -0.15 |

### Rewritten SentimentAnalyzer
- Depends on: `ILoughranMcDonaldAnalyzer`, `IHeadlineHeuristicAnalyzer`
- No TrendRadar dependency
- Flow:
  1. Determine text source (content vs title)
  2. Run L-M analysis on best available text
  3. Run headline heuristic on title
  4. Blend scores based on text availability:
     - **With content**: 70% L-M + 30% headline heuristic
     - **Headline only**: 40% L-M + 60% headline heuristic
  5. Produce `SentimentScore` (same output format as before)

### Configuration
New section in `appsettings.json`:
```json
"SentimentAnalysis": {
  "LexiconWeightWithContent": 0.7,
  "HeadlineWeightWithContent": 0.3,
  "LexiconWeightHeadlineOnly": 0.4,
  "HeadlineWeightHeadlineOnly": 0.6,
  "IndianPhraseMultiplier": 2.0,
  "MinContentLengthForFullAnalysis": 50
}
```

---

## 5. Expanded RSS Feed Sources

Add to `NewsFeeds` config in `appsettings.json`:

| Source | URL | Priority |
|--------|-----|----------|
| Financial Express | `financialexpress.com/market/rss` | 7 |
| Mint Companies | `livemint.com/rss/companies` | 8 |
| Reuters India | `reuters.com/rssFeed/INbusinessNews` | 6 |
| CNBC TV18 | `cnbctv18.com/commonfeeds/v1/cne/rss/market.xml` | 8 |
| Hindu BusinessLine | `thehindubusinessline.com/markets/feeder/default.rss` | 5 |

The existing `NewsAggregator` already supports any number of feeds — no code changes needed, only config additions.

---

## 6. Data Flow After Redesign

```
RSS Feeds (10 sources, every 5 min)
    → NewsAggregator (fetch + dedup + HTML cleanup)
    → NewsProcessingService
        → SentimentAnalyzer
            ├─ LoughranMcDonaldAnalyzer (lexicon + Indian phrases)
            │   Uses: article.Content if available, else article.Title
            └─ HeadlineHeuristicAnalyzer (regex patterns on title)
            → Blended SentimentScore (configurable weights)
        → StockMapper
            → AhoCorasickStockMatcher (O(n) scan for 2000+ patterns)
        → ArticleStorageService (MongoDB)

NseStockRefreshService (daily at 06:00 IST)
    → Fetch from NSE API
    → Upsert MongoDB stocks collection
    → Rebuild Aho-Corasick trie

Static fallback: backend/data/nse-stocks.json (seeds DB on first run)
```

---

## Verification

1. **Build**: `cd backend/src/AutoTrade.WebAPI && dotnet build` — no TrendRadar compilation errors
2. **Startup**: `dotnet run` — verify NseStockRefreshService seeds from JSON, Aho-Corasick trie builds
3. **Health endpoint**: `GET /api/health` — no TrendRadar fields in response
4. **News fetch**: `GET /api/news/latest` — articles have sentiment scores and stock symbols
5. **Stock matching**: Verify articles mentioning "Reliance Industries" map to RELIANCE symbol
6. **Sentiment**: Verify headline "TCS surges 5% on strong Q3 results" gets positive sentiment
7. **Signal generation**: Verify signals still generate with the new sentiment pipeline
