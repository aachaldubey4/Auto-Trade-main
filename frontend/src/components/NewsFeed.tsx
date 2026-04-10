import { useLatestNews, useNewsByStock } from '../hooks/useNews';

interface NewsFeedProps {
  symbol?: string;
}

export default function NewsFeed({ symbol }: NewsFeedProps) {
  const latestQuery = useLatestNews({ page: 1, limit: 20, hours: 24 });
  const byStockQuery = useNewsByStock(symbol ?? '', { page: 1, limit: 20 });
  const query = symbol ? byStockQuery : latestQuery;

  const getTimeAgo = (timestamp: string) => {
    const now = new Date();
    const newsTime = new Date(timestamp);
    const diffMs = now.getTime() - newsTime.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    
    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    const hours = Math.floor(diffMins / 60);
    return `${hours}h ago`;
  };

  const getSentimentBadge = (sentiment: string) => {
    switch (sentiment) {
      case 'positive':
        return 'badge-success';
      case 'negative':
        return 'badge-error';
      default:
        return 'badge-neutral';
    }
  };

  return (
    <div className="card bg-base-200 shadow-xl">
      <div className="card-body">
        <h2 className="card-title text-xl">{symbol ? `${symbol} News` : 'Market News'}</h2>
        <div className="space-y-4 max-h-96 overflow-y-auto">
          {query.isError ? (
            <div className="alert alert-error">
              <span>Failed to load news.</span>
            </div>
          ) : query.isLoading ? (
            Array.from({ length: 6 }).map((_, idx) => (
              <div key={idx} className="border-b border-base-300 pb-4 last:border-0">
                <div className="flex items-start justify-between gap-2">
                  <div className="flex-1">
                    <div className="skeleton h-4 w-full max-w-xs" />
                    <div className="flex items-center gap-2 flex-wrap mt-2">
                      <div className="skeleton h-3 w-20" />
                      <div className="skeleton h-3 w-14" />
                      <div className="skeleton h-3 w-24" />
                    </div>
                  </div>
                  <div className="skeleton h-6 w-20" />
                </div>
              </div>
            ))
          ) : (
            (query.data?.articles ?? []).map((item) => (
              <div key={item.url || item.contentHash} className="border-b border-base-300 pb-4 last:border-0">
                <div className="flex items-start justify-between gap-2">
                  <div className="flex-1">
                    <a
                      className="font-semibold text-sm mb-1 link link-hover inline-block"
                      href={item.url}
                      target="_blank"
                      rel="noreferrer"
                    >
                      {item.title}
                    </a>
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-xs opacity-70">{item.source}</span>
                      <span className="text-xs opacity-50">•</span>
                      <span className="text-xs opacity-70">{getTimeAgo(item.publishedAt)}</span>
                      {item.stockSymbols.length > 0 && (
                        <>
                          <span className="text-xs opacity-50">•</span>
                          <div className="flex gap-1 flex-wrap">
                            {item.stockSymbols.map((stock) => (
                              <span key={stock} className="badge badge-xs badge-outline">
                                {stock}
                              </span>
                            ))}
                          </div>
                        </>
                      )}
                    </div>
                  </div>
                  <div className={`badge ${getSentimentBadge(item.sentiment.overall)} badge-sm`}>
                    {item.sentiment.overall}
                  </div>
                </div>
              </div>
            ))
          )}
        </div>
        <div className="text-sm opacity-70 mt-2">
          {query.isLoading
            ? 'Loading…'
            : query.isError
              ? 'Failed to load news'
              : `${(query.data?.articles ?? []).length} article${(query.data?.articles ?? []).length !== 1 ? 's' : ''}`}
        </div>
      </div>
    </div>
  );
}
