import { useState } from 'react';
import { useFeedSources, useLatestNews, useSentimentSummary } from '../hooks/useNews';
import type { FeedSource, StockSentiment } from '../types/api';

function SourceBadge({ source }: { source: FeedSource }) {
  const statusText = source.isHealthy
    ? `HTTP ${source.lastStatusCode}`
    : source.lastError ?? `HTTP ${source.lastStatusCode || '—'}`;

  const checkedAt = source.lastChecked
    ? new Date(source.lastChecked).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' })
    : 'Never';

  return (
    <div className="flex items-center gap-2 p-2 rounded-lg bg-base-300" title={statusText}>
      <div
        className={`w-2.5 h-2.5 rounded-full flex-shrink-0 ${
          source.isHealthy ? 'bg-success' : 'bg-error'
        }`}
      />
      <div className="min-w-0">
        <div className="text-sm font-medium truncate">{source.name}</div>
        <div className="text-xs opacity-50 truncate">{statusText} · {checkedAt}</div>
      </div>
    </div>
  );
}

function SentimentBar({ s }: { s: StockSentiment }) {
  const pct = Math.round((s.score + 1) * 50); // map -1..1 to 0..100
  const color = s.overall === 'positive' ? 'bg-success' : s.overall === 'negative' ? 'bg-error' : 'bg-warning';
  return (
    <div className="flex items-center gap-2 p-2 rounded-lg bg-base-300">
      <div className="w-12 text-xs font-bold flex-shrink-0">{s.symbol}</div>
      <div className="flex-1 bg-base-100 rounded-full h-2 overflow-hidden">
        <div className={`h-2 rounded-full ${color}`} style={{ width: `${pct}%` }} />
      </div>
      <div className={`text-xs font-semibold w-14 text-right ${
        s.overall === 'positive' ? 'text-success' : s.overall === 'negative' ? 'text-error' : 'text-warning'
      }`}>
        {s.score >= 0 ? '+' : ''}{s.score.toFixed(2)}
      </div>
    </div>
  );
}

export default function NewsTab() {
  const sources = useFeedSources();
  const sentiment = useSentimentSummary();
  const [page, setPage] = useState(1);
  const news = useLatestNews({ page, limit: 20, hours: 48 });

  const articles = news.data?.articles ?? [];
  const totalCount = news.data?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / 20));

  const healthySources = sources.data?.filter((s) => s.isHealthy).length ?? 0;
  const totalSources = sources.data?.length ?? 0;

  return (
    <div className="space-y-4">
      {/* Source health panel */}
      <div className="card bg-base-200 shadow">
        <div className="card-body py-3 px-4">
          <div className="flex items-center justify-between mb-2">
            <h2 className="card-title text-base">RSS Feed Sources</h2>
            {sources.data && (
              <span className={`badge ${healthySources === totalSources ? 'badge-success' : healthySources === 0 ? 'badge-error' : 'badge-warning'}`}>
                {healthySources}/{totalSources} online
              </span>
            )}
          </div>
          {sources.isLoading && <div className="text-sm opacity-60">Checking sources...</div>}
          {sources.data && (
            <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-5 gap-2">
              {sources.data.map((s) => (
                <SourceBadge key={s.url} source={s} />
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Per-stock sentiment */}
      <div className="card bg-base-200 shadow">
        <div className="card-body py-3 px-4">
          <h2 className="card-title text-base">Stock Sentiment Scores</h2>
          <p className="text-xs opacity-50 mb-2">Score = positive − negative confidence (−1 bearish → +1 bullish). Signals need score &gt; 0.45 to fire BUY.</p>
          {sentiment.isLoading && <div className="text-sm opacity-60">Loading…</div>}
          {sentiment.data && (
            <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-2">
              {sentiment.data.map((s) => <SentimentBar key={s.symbol} s={s} />)}
            </div>
          )}
        </div>
      </div>

      {/* Article list */}
      <div className="card bg-base-200 shadow">
        <div className="card-body py-3 px-4">
          <h2 className="card-title text-base">Latest Articles ({totalCount})</h2>
          {news.isLoading && <div className="text-sm opacity-60">Loading articles...</div>}
          {news.isError && <div className="text-sm text-error">Failed to load articles — is the backend running?</div>}
          <div className="divide-y divide-base-300">
            {articles.map((article) => {
              const overall = typeof article.sentiment === 'object' ? article.sentiment?.overall : article.sentiment;
              return (
                <div key={article.contentHash} className="py-3">
                  <div className="flex items-start gap-3">
                    <div
                      className={`mt-1.5 w-2 h-2 rounded-full flex-shrink-0 ${
                        overall === 'positive' ? 'bg-success' : overall === 'negative' ? 'bg-error' : 'bg-warning'
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
                        {article.stockSymbols?.length > 0 && (
                          <>
                            <span>·</span>
                            <span className="text-primary">{article.stockSymbols.slice(0, 4).join(', ')}</span>
                          </>
                        )}
                      </div>
                    </div>
                  </div>
                </div>
              );
            })}
            {articles.length === 0 && !news.isLoading && (
              <div className="py-8 text-center text-sm opacity-50">
                No articles yet — backend is collecting news every 5 minutes.
              </div>
            )}
          </div>

          {totalPages > 1 && (
            <div className="flex justify-center gap-2 pt-3">
              <button className="btn btn-sm" disabled={page === 1} onClick={() => setPage((p) => p - 1)}>«</button>
              <span className="btn btn-sm btn-disabled no-animation">{page} / {totalPages}</span>
              <button className="btn btn-sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>»</button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
