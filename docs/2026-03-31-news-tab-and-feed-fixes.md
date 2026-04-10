# News Tab + Feed Health Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix RSS 403/Yahoo 429 failures and add a News tab showing all aggregated articles plus red/green source health status.

**Architecture:** Backend tracks per-feed health state in NewsAggregator (in-memory); a new `/api/news/sources` endpoint exposes it. Frontend adds tab navigation to Dashboard and a NewsTab component that polls articles and source statuses.

**Tech Stack:** .NET 9 (C#), React + Vite, TailwindCSS/DaisyUI, React Query, TypeScript

---

### Task 1: Fix RSS 403 — rotate browser-like User-Agent per request

**Problem:** Moneycontrol, Business Standard, NDTV return 403 because .NET's default HttpClient sends a server-side User-Agent. Fix by sending browser headers per RSS request.

**Files:**
- Modify: `backend/src/AutoTrade.Infrastructure/Services/NewsAggregator.cs`

- [ ] **Step 1: Add per-request browser headers in `FetchFromSourceAsync`**

Replace the `using var response = await httpClient.GetAsync(sourceUrl);` call with a request message that includes browser headers:

```csharp
public async Task<List<RawArticle>> FetchFromSourceAsync(string sourceUrl)
{
    var articles = new List<RawArticle>();
    try
    {
        logger.LogDebug("Fetching RSS feed from {Url}", sourceUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");
        request.Headers.TryAddWithoutValidation("Referer", "https://www.google.com/");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        // ... rest unchanged
```

- [ ] **Step 2: Build and verify no 403 for the feeds that were working before**

```bash
cd backend/src/AutoTrade.WebAPI && dotnet build 2>&1 | tail -5
```
Expected: `Build succeeded.`

---

### Task 2: Fix Yahoo Finance 429 — throttle watchlist quote calls

**Problem:** WatchlistController fires concurrent Yahoo Finance calls for all 10 symbols. Yahoo rate-limits to ~2 req/s.

**Files:**
- Modify: `backend/src/AutoTrade.WebAPI/Controllers/WatchlistController.cs`

- [ ] **Step 1: Reduce concurrency in watchlist throttler from 3 → 1**

In `WatchlistController.cs`, change:
```csharp
var throttler = new SemaphoreSlim(3, 3);
```
to:
```csharp
var throttler = new SemaphoreSlim(1, 1);
```

- [ ] **Step 2: Add 300ms delay between quote fetches to stay under Yahoo rate limit**

Inside the `await throttler.WaitAsync();` block, after acquiring the semaphore and before calling `GetCurrentQuoteAsync`, add:

```csharp
await throttler.WaitAsync();
try
{
    await Task.Delay(300); // stay under Yahoo Finance rate limit
    MarketQuote quote;
    try
    {
        quote = await marketData.GetCurrentQuoteAsync(symbol);
    }
```

- [ ] **Step 3: Extend watchlist cache TTL from 30s → 60s**

```csharp
cache.Set(cacheKey, ordered, new MemoryCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
});
```

- [ ] **Step 4: Build**

```bash
dotnet build 2>&1 | tail -5
```
Expected: `Build succeeded.`

---

### Task 3: Track per-feed health state in NewsAggregator

**Problem:** No visibility into which RSS sources are healthy vs failing.

**Files:**
- Modify: `backend/src/AutoTrade.Infrastructure/Services/NewsAggregator.cs`
- Modify: `backend/src/AutoTrade.Application/Interfaces/INewsAggregator.cs`

- [ ] **Step 1: Add FeedHealthStatus model to NewsAggregator.cs (bottom of file)**

```csharp
public record FeedHealthStatus(
    string Name,
    string Url,
    bool IsHealthy,
    int LastStatusCode,
    DateTime? LastChecked,
    string? LastError
);
```

- [ ] **Step 2: Add health tracking dictionary to NewsAggregator class**

In the class body after `private bool _isMonitoring = false;`:

```csharp
private readonly Dictionary<string, FeedHealthStatus> _feedHealth = new();
```

- [ ] **Step 3: Update `FetchFromSourceAsync` to record health after each attempt**

Replace the catch block in `FetchFromSourceAsync`:

```csharp
try
{
    using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
    request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
    request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");
    request.Headers.TryAddWithoutValidation("Referer", "https://www.google.com/");

    using var response = await httpClient.SendAsync(request);

    _feedHealth[sourceUrl] = new FeedHealthStatus(
        Name: FeedConfigs.Values.FirstOrDefault(f => f.Url == sourceUrl)?.Name ?? sourceUrl,
        Url: sourceUrl,
        IsHealthy: response.IsSuccessStatusCode,
        LastStatusCode: (int)response.StatusCode,
        LastChecked: DateTime.UtcNow,
        LastError: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}"
    );

    response.EnsureSuccessStatusCode();
    // ... rest of parsing unchanged
}
catch (Exception ex)
{
    _feedHealth[sourceUrl] = new FeedHealthStatus(
        Name: FeedConfigs.Values.FirstOrDefault(f => f.Url == sourceUrl)?.Name ?? sourceUrl,
        Url: sourceUrl,
        IsHealthy: false,
        LastStatusCode: 0,
        LastChecked: DateTime.UtcNow,
        LastError: ex.Message.Length > 120 ? ex.Message[..120] : ex.Message
    );
    logger.LogError(ex, "Failed to fetch RSS feed from {Url}", sourceUrl);
}
```

- [ ] **Step 4: Add `GetFeedHealth()` method to NewsAggregator**

```csharp
public IReadOnlyList<FeedHealthStatus> GetFeedHealth()
{
    // Return status for all configured feeds, including ones not yet fetched
    var all = FeedConfigs.Values.Select(f =>
        _feedHealth.TryGetValue(f.Url, out var status)
            ? status
            : new FeedHealthStatus(f.Name, f.Url, false, 0, null, "Not yet fetched")
    ).ToList();
    return all;
}
```

- [ ] **Step 5: Add `GetFeedHealth` to `INewsAggregator` interface**

In `backend/src/AutoTrade.Application/Interfaces/INewsAggregator.cs`, add:

```csharp
IReadOnlyList<FeedHealthStatus> GetFeedHealth();
```

Note: `FeedHealthStatus` is defined in the Infrastructure layer. Move it to Domain or add a DTO — add to `backend/src/AutoTrade.Domain/Models/NewsModels.cs` (create if not exists):

```csharp
namespace AutoTrade.Domain.Models;

public record FeedHealthStatus(
    string Name,
    string Url,
    bool IsHealthy,
    int LastStatusCode,
    DateTime? LastChecked,
    string? LastError
);
```

Then reference it from NewsAggregator.cs (remove the local record definition).

- [ ] **Step 6: Build**

```bash
dotnet build 2>&1 | tail -5
```
Expected: `Build succeeded.`

---

### Task 4: Add `/api/news/sources` endpoint

**Files:**
- Modify: `backend/src/AutoTrade.WebAPI/Controllers/NewsController.cs`

- [ ] **Step 1: Add sources endpoint to NewsController**

Add this action to `NewsController`:

```csharp
/// <summary>Get health status of all RSS feed sources</summary>
[HttpGet("sources")]
[ProducesResponseType(typeof(ApiResponse<List<FeedHealthStatus>>), 200)]
public ActionResult<ApiResponse<List<FeedHealthStatus>>> GetSources()
{
    var health = newsAggregator.GetFeedHealth();
    return Ok(new ApiResponse<List<FeedHealthStatus>>
    {
        Success = true,
        Data = health.ToList()
    });
}
```

- [ ] **Step 2: Inject `INewsAggregator` into NewsController**

Change the primary constructor from:
```csharp
public class NewsController(
    IArticleStorageService articleStorage,
    ISentimentAnalyzer sentimentAnalyzer,
    INewsProcessingService newsProcessing,
    ILogger<NewsController> logger)
```
to:
```csharp
public class NewsController(
    IArticleStorageService articleStorage,
    ISentimentAnalyzer sentimentAnalyzer,
    INewsProcessingService newsProcessing,
    INewsAggregator newsAggregator,
    ILogger<NewsController> logger)
```

- [ ] **Step 3: Build and test endpoint**

```bash
dotnet build 2>&1 | tail -5
# then start server and test:
curl -s http://localhost:5265/api/news/sources | jq '.data[] | {name, isHealthy, lastStatusCode}'
```
Expected: JSON array with each feed's name, isHealthy bool, and status code.

---

### Task 5: Add tab navigation to Dashboard

**Files:**
- Modify: `frontend/src/components/Dashboard.tsx`
- Create: `frontend/src/components/NewsTab.tsx`

- [ ] **Step 1: Add tab state to Dashboard.tsx**

Add `activeTab` state and tab navigation above the main grid:

```tsx
const [activeTab, setActiveTab] = useState<'dashboard' | 'news'>('dashboard');
```

Replace the outer return JSX content with:

```tsx
return (
  <div className="min-h-screen bg-base-100">
    <Header />
    <div className="container mx-auto p-2 sm:p-4">
      <div role="tablist" className="tabs tabs-bordered mb-4">
        <button
          role="tab"
          className={`tab ${activeTab === 'dashboard' ? 'tab-active' : ''}`}
          onClick={() => setActiveTab('dashboard')}
        >
          Dashboard
        </button>
        <button
          role="tab"
          className={`tab ${activeTab === 'news' ? 'tab-active' : ''}`}
          onClick={() => setActiveTab('news')}
        >
          News & Sources
        </button>
      </div>

      {activeTab === 'dashboard' && (
        <motion.div
          className="grid grid-cols-1 lg:grid-cols-3 gap-2 sm:gap-4"
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.2 }}
        >
          <div className="lg:col-span-2 space-y-2 sm:space-y-4">
            <SignalsPanel />
            <Portfolio />
            <div className="grid grid-cols-1 md:grid-cols-2 gap-2 sm:gap-4">
              <Watchlist onStockSelect={setSelectedStock} selectedSymbol={selectedStock} />
              <NewsFeed symbol={selectedStock} />
            </div>
          </div>
          <div className="lg:col-span-1">
            <StockChart symbol={selectedStock} />
          </div>
        </motion.div>
      )}

      {activeTab === 'news' && (
        <motion.div
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.2 }}
        >
          <NewsTab />
        </motion.div>
      )}
    </div>
  </div>
);
```

Also add import at top:
```tsx
import NewsTab from './NewsTab';
```

---

### Task 6: Build NewsTab component

**Files:**
- Create: `frontend/src/components/NewsTab.tsx`
- Modify: `frontend/src/services/api.ts`
- Modify: `frontend/src/hooks/useNews.ts`
- Modify: `frontend/src/types/api.ts`

- [ ] **Step 1: Add `FeedSource` type to `frontend/src/types/api.ts`**

```ts
export interface FeedSource {
  name: string;
  url: string;
  isHealthy: boolean;
  lastStatusCode: number;
  lastChecked: string | null;
  lastError: string | null;
}
```

- [ ] **Step 2: Add `sources` call to `frontend/src/services/api.ts`**

Inside the `news` object in `api`:
```ts
async sources(): Promise<FeedSource[]> {
  const { data } = await client.get<ApiResponse<FeedSource[]>>('/news/sources');
  return unwrapApiResponse(data, 'Feed sources');
},
```

- [ ] **Step 3: Add `useFeedSources` hook to `frontend/src/hooks/useNews.ts`**

```ts
export const useFeedSources = () => {
  return useQuery({
    queryKey: ['news', 'sources'],
    queryFn: () => api.news.sources(),
    refetchInterval: 30_000,
  });
};
```

- [ ] **Step 4: Create `frontend/src/components/NewsTab.tsx`**

```tsx
import { useState } from 'react';
import { useFeedSources, useLatestNews } from '../hooks/useNews';
import type { FeedSource } from '../types/api';

function SourceBadge({ source }: { source: FeedSource }) {
  return (
    <div className="flex items-center gap-2 p-2 rounded-lg bg-base-200">
      <div
        className={`w-2.5 h-2.5 rounded-full flex-shrink-0 ${
          source.isHealthy ? 'bg-success' : 'bg-error'
        }`}
        title={source.isHealthy ? 'Connected' : source.lastError ?? 'Failed'}
      />
      <div className="min-w-0">
        <div className="text-sm font-medium truncate">{source.name}</div>
        <div className="text-xs opacity-50 truncate">
          {source.isHealthy
            ? `HTTP ${source.lastStatusCode}`
            : source.lastError ?? `HTTP ${source.lastStatusCode || '—'}`}
        </div>
      </div>
    </div>
  );
}

export default function NewsTab() {
  const sources = useFeedSources();
  const [page, setPage] = useState(1);
  const news = useLatestNews({ page, limit: 20 });

  const articles = news.data?.articles ?? [];
  const totalPages = news.data
    ? Math.ceil(news.data.totalCount / 20)
    : 1;

  return (
    <div className="space-y-4">
      {/* Source health */}
      <div className="card bg-base-200 shadow">
        <div className="card-body py-3 px-4">
          <h2 className="card-title text-base">RSS Feed Sources</h2>
          {sources.isLoading && <div className="text-sm opacity-60">Loading sources...</div>}
          {sources.data && (
            <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-5 gap-2">
              {sources.data.map((s) => (
                <SourceBadge key={s.url} source={s} />
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Article list */}
      <div className="card bg-base-200 shadow">
        <div className="card-body py-3 px-4">
          <h2 className="card-title text-base">Latest Articles ({news.data?.totalCount ?? 0})</h2>
          {news.isLoading && <div className="text-sm opacity-60">Loading articles...</div>}
          {news.isError && <div className="text-sm text-error">Failed to load articles</div>}
          <div className="divide-y divide-base-300">
            {articles.map((article) => (
              <div key={article.id} className="py-3">
                <div className="flex items-start gap-3">
                  <div
                    className={`mt-1 w-2 h-2 rounded-full flex-shrink-0 ${
                      article.sentiment === 'positive'
                        ? 'bg-success'
                        : article.sentiment === 'negative'
                        ? 'bg-error'
                        : 'bg-warning'
                    }`}
                  />
                  <div className="min-w-0 flex-1">
                    <a
                      href={article.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="text-sm font-medium hover:text-primary line-clamp-2"
                    >
                      {article.title}
                    </a>
                    <div className="flex flex-wrap gap-2 mt-1 text-xs opacity-60">
                      <span>{article.source}</span>
                      <span>·</span>
                      <span>{new Date(article.publishedAt).toLocaleString('en-IN')}</span>
                      {article.relatedStocks?.length > 0 && (
                        <>
                          <span>·</span>
                          <span>{article.relatedStocks.slice(0, 3).join(', ')}</span>
                        </>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            ))}
          </div>

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="flex justify-center gap-2 pt-2">
              <button
                className="btn btn-sm"
                disabled={page === 1}
                onClick={() => setPage((p) => p - 1)}
              >
                «
              </button>
              <span className="btn btn-sm btn-disabled">{page} / {totalPages}</span>
              <button
                className="btn btn-sm"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
              >
                »
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Check article type fields match what the API returns**

```bash
curl -s "http://localhost:5265/api/news/latest?limit=1" | jq '.data.articles[0] | keys'
```

If `relatedStocks` is named differently in the response (e.g. `stocks`, `mappedStocks`), update the field reference in NewsTab.tsx accordingly.

- [ ] **Step 6: Start frontend and verify News tab renders**

```bash
cd frontend && npm run dev
```

Open http://localhost:5173, click "News & Sources" tab.
Expected:
- Source grid with green/red dots per feed
- Article list with pagination

---

## Self-Review

**Spec coverage:**
- ✅ Fix RSS 403 → Task 1 (browser headers per request)
- ✅ Fix Yahoo 429 → Task 2 (serial throttle + longer cache)
- ✅ Feed health tracking → Task 3
- ✅ `/api/news/sources` endpoint → Task 4
- ✅ News tab navigation → Task 5
- ✅ News tab UI with source status + articles → Task 6

**Known limitations:**
- Some feeds (Moneycontrol, Business Standard) may still 403 — they use aggressive bot detection beyond User-Agent. This is an external limitation; the health indicator will correctly show them as red.
- `FeedHealthStatus` record needs to be in Domain layer (not Infrastructure) so the Application interface can reference it — Task 3 Step 5 handles this.
