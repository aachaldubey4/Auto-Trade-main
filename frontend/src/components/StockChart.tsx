import { useMemo, useState } from 'react';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
} from 'recharts';
import { useMarketHistory } from '../hooks/useMarketHistory';

interface StockChartProps {
  symbol: string;
}

export default function StockChart({ symbol }: StockChartProps) {
  const [days, setDays] = useState<number>(22);
  const query = useMarketHistory(symbol, days);

  const chartData = useMemo(() => {
    const data = query.data ?? [];
    return data.map((item) => ({
      time: new Date(item.date).toLocaleDateString('en-IN', { day: '2-digit', month: 'short' }),
      price: item.close,
    }));
  }, [query.data]);

  return (
    <div className="card bg-base-200 shadow-xl">
      <div className="card-body">
        <div className="flex items-center justify-between gap-3 flex-wrap">
          <h2 className="card-title text-xl">{symbol} - Price Chart</h2>
          <select
            className="select select-sm select-bordered"
            value={days}
            onChange={(e) => setDays(Number(e.target.value))}
          >
            <option value={5}>1W</option>
            <option value={22}>1M</option>
            <option value={66}>3M</option>
          </select>
        </div>
        <div className="h-48 sm:h-64 w-full">
          {query.isError ? (
            <div className="alert alert-error">
              <span>Failed to load price history.</span>
            </div>
          ) : query.isLoading ? (
            <div className="w-full h-full flex flex-col gap-3">
              <div className="skeleton h-4 w-40" />
              <div className="skeleton flex-1 w-full" />
              <div className="skeleton h-4 w-52" />
            </div>
          ) : (
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={chartData}>
                <CartesianGrid strokeDasharray="3 3" stroke="#374151" />
                <XAxis
                  dataKey="time"
                  stroke="#9CA3AF"
                  style={{ fontSize: '10px' }}
                  interval="preserveStartEnd"
                />
                <YAxis
                  stroke="#9CA3AF"
                  style={{ fontSize: '10px' }}
                  domain={['dataMin - 10', 'dataMax + 10']}
                  width={50}
                />
                <Tooltip
                  contentStyle={{
                    backgroundColor: '#1F2937',
                    border: '1px solid #374151',
                    borderRadius: '8px',
                    fontSize: '12px',
                  }}
                  labelStyle={{ color: '#F3F4F6' }}
                />
                <Line
                  type="monotone"
                  dataKey="price"
                  stroke="#3B82F6"
                  strokeWidth={2}
                  dot={false}
                  activeDot={{ r: 4 }}
                />
              </LineChart>
            </ResponsiveContainer>
          )}
        </div>
        <div className="text-xs opacity-70 mt-2">
          {query.isLoading
            ? 'Loading…'
            : query.isError
              ? 'Failed to load price history'
              : `${chartData.length} points • Click on watchlist to view different stocks`}
        </div>
      </div>
    </div>
  );
}
