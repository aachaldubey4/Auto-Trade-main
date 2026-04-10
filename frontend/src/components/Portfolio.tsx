import { useMemo } from 'react';
import { useQueries } from '@tanstack/react-query';
import { api } from '../services/api';
import { useSignalHistory } from '../hooks/useSignalHistory';
import type { SignalAction } from '../types/api';

const normaliseAction = (action: SignalAction): 'BUY' | 'SELL' => {
  if (action === 'BUY' || action === 'SELL') return action;
  return action === 0 ? 'BUY' : 'SELL';
};

export default function Portfolio() {
  const from = useMemo(() => new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString(), []);
  const history = useSignalHistory({ from, status: 'executed' });

  const signals = useMemo(() => history.data?.signals ?? [], [history.data?.signals]);
  const symbols = useMemo(
    () => Array.from(new Set(signals.map((s) => s.symbol))).sort(),
    [signals],
  );

  const quotes = useQueries({
    queries: symbols.map((symbol) => ({
      queryKey: ['market', 'quote', symbol] as const,
      queryFn: () => api.market.quote(symbol),
      staleTime: 30_000,
      enabled: symbols.length > 0,
    })),
  });

  const quoteBySymbol = useMemo(() => {
    const map = new Map<string, number>();
    quotes.forEach((q, idx) => {
      const sym = symbols[idx];
      if (sym && q.data) map.set(sym, q.data.lastPrice);
    });
    return map;
  }, [quotes, symbols]);

  const rows = useMemo(() => {
    return signals.map((s) => {
      const current = quoteBySymbol.get(s.symbol);
      const action = normaliseAction(s.action);
      const pnlPercent =
        current == null
          ? null
          : action === 'BUY'
            ? ((current - s.entryPrice) / s.entryPrice) * 100
            : ((s.entryPrice - current) / s.entryPrice) * 100;

      return { signal: s, current, pnlPercent };
    });
  }, [signals, quoteBySymbol]);

  const overall = useMemo(() => {
    const pnls = rows.map((r) => r.pnlPercent).filter((v): v is number => typeof v === 'number' && Number.isFinite(v));
    if (pnls.length === 0) return null;
    return pnls.reduce((a, b) => a + b, 0) / pnls.length;
  }, [rows]);

  const isLoading = history.isLoading || quotes.some((q) => q.isLoading);
  const isError = history.isError || quotes.some((q) => q.isError);

  return (
    <div className="card bg-base-200 shadow-xl">
      <div className="card-body">
        <div className="flex items-center justify-between gap-3 flex-wrap">
          <h2 className="card-title text-xl">Portfolio</h2>
          <div className="text-sm opacity-70">
            {overall == null ? '—' : `${overall >= 0 ? '+' : ''}${overall.toFixed(2)}% avg`}
          </div>
        </div>

        {isError ? (
          <div className="alert alert-error">
            <span>Failed to load portfolio.</span>
          </div>
        ) : isLoading ? (
          <div className="space-y-3">
            <div className="skeleton h-4 w-40" />
            <div className="skeleton h-4 w-full" />
            <div className="skeleton h-4 w-full" />
          </div>
        ) : rows.length === 0 ? (
          <div className="text-sm opacity-70">No executed signals yet.</div>
        ) : (
          <div className="overflow-x-auto">
            <table className="table table-zebra table-sm">
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>Side</th>
                  <th>Entry</th>
                  <th>Now</th>
                  <th>P&amp;L</th>
                </tr>
              </thead>
              <tbody>
                {rows.map(({ signal, current, pnlPercent }) => {
                  const action = normaliseAction(signal.action);
                  return (
                    <tr key={signal.id}>
                      <td className="font-bold">{signal.symbol}</td>
                      <td>
                        <span className={`badge badge-sm ${action === 'BUY' ? 'badge-success' : 'badge-error'}`}>
                          {action}
                        </span>
                      </td>
                      <td>₹{signal.entryPrice.toFixed(2)}</td>
                      <td>{current == null ? '—' : `₹${current.toFixed(2)}`}</td>
                      <td className={pnlPercent != null && pnlPercent >= 0 ? 'text-success' : 'text-error'}>
                        {pnlPercent == null ? '—' : `${pnlPercent >= 0 ? '+' : ''}${pnlPercent.toFixed(2)}%`}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

