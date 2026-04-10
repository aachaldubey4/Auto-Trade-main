import { useWatchlist } from '../hooks/useWatchlist';

interface WatchlistProps {
  onStockSelect: (symbol: string) => void;
  selectedSymbol?: string;
}

export default function Watchlist({ onStockSelect, selectedSymbol }: WatchlistProps) {
  const query = useWatchlist();

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

  const formatVolume = (volume: number) => {
    if (volume >= 1000000) {
      return `${(volume / 1000000).toFixed(2)}M`;
    }
    if (volume >= 1000) {
      return `${(volume / 1000).toFixed(2)}K`;
    }
    return volume.toString();
  };

  return (
    <div className="card bg-base-200 shadow-xl">
      <div className="card-body">
        <h2 className="card-title text-xl">Watchlist</h2>
        <div className="overflow-x-auto">
          <table className="table table-zebra table-sm">
            <thead>
              <tr>
                <th>Symbol</th>
                <th>Price</th>
                <th>Change</th>
                <th>Sentiment</th>
                <th>Volume</th>
              </tr>
            </thead>
            <tbody>
              {query.isError ? (
                <tr>
                  <td colSpan={5}>
                    <div className="alert alert-error">
                      <span>Failed to load watchlist.</span>
                    </div>
                  </td>
                </tr>
              ) : query.isLoading ? (
                Array.from({ length: 6 }).map((_, idx) => (
                  <tr key={idx}>
                    <td>
                      <div className="skeleton h-4 w-24" />
                      <div className="skeleton h-3 w-40 mt-2" />
                    </td>
                    <td>
                      <div className="skeleton h-4 w-20" />
                    </td>
                    <td>
                      <div className="skeleton h-4 w-28" />
                    </td>
                    <td>
                      <div className="skeleton h-6 w-20" />
                    </td>
                    <td>
                      <div className="skeleton h-4 w-16" />
                    </td>
                  </tr>
                ))
              ) : (
                (query.data ?? []).map((stock) => (
                  <tr
                    key={stock.symbol}
                    className={`cursor-pointer hover:bg-base-300 ${
                      selectedSymbol === stock.symbol ? 'bg-base-300' : ''
                    }`}
                    onClick={() => onStockSelect(stock.symbol)}
                  >
                    <td>
                      <div>
                        <div className="font-bold">{stock.symbol}</div>
                        <div className="text-xs opacity-70">{stock.name}</div>
                      </div>
                    </td>
                    <td>
                      <div className="font-semibold">₹{stock.price.toFixed(2)}</div>
                    </td>
                    <td>
                      <div className={`font-semibold ${stock.change >= 0 ? 'text-success' : 'text-error'}`}>
                        {stock.change >= 0 ? '+' : ''}
                        {stock.change.toFixed(2)} ({stock.changePercent >= 0 ? '+' : ''}
                        {stock.changePercent.toFixed(2)}%)
                      </div>
                    </td>
                    <td>
                      <div className={`badge ${getSentimentBadge(stock.sentiment)}`}>{stock.sentiment}</div>
                    </td>
                    <td>
                      <div className="text-sm">{formatVolume(stock.volume)}</div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        <div className="text-sm opacity-70 mt-2">
          {query.isLoading
            ? 'Loading…'
            : query.isError
              ? 'Failed to load watchlist'
              : `${(query.data ?? []).length} stock${(query.data ?? []).length !== 1 ? 's' : ''}`}
        </div>
      </div>
    </div>
  );
}
