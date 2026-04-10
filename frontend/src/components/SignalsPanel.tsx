import { useMemo, useState } from 'react';
import toast from 'react-hot-toast';
import { useSignals, useUpdateSignalStatus, type SignalsKind } from '../hooks/useSignals';
import type { SignalAction, TradingSignal } from '../types/api';
import SignalExecutionModal from './SignalExecutionModal';
import { api } from '../services/api';
import { useQueryClient } from '@tanstack/react-query';

const normaliseAction = (action: SignalAction): 'BUY' | 'SELL' => {
  if (action === 'BUY' || action === 'SELL') return action;
  return action === 0 ? 'BUY' : 'SELL';
};

const getTimeAgo = (timestamp: string) => {
    const now = new Date();
    const signalTime = new Date(timestamp);
    const diffMs = now.getTime() - signalTime.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    
    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    const hours = Math.floor(diffMins / 60);
    return `${hours}h ${diffMins % 60}m ago`;
  };

const getSignalStrengthColor = (strength: number) => {
    if (strength >= 80) return 'badge-success';
    if (strength >= 60) return 'badge-warning';
    return 'badge-error';
  };

const calculateProfitPercent = (entry: number, target: number) => {
    return ((target - entry) / entry) * 100;
  };

const calculateLossPercent = (entry: number, stopLoss: number) => {
    return ((entry - stopLoss) / entry) * 100;
  };

const kindLabel: Record<SignalsKind, string> = {
  active: 'Active',
  overnight: 'Overnight',
  intraday: 'Intraday',
};

export default function SignalsPanel() {
  const [kind, setKind] = useState<SignalsKind>('active');
  const [executingSignal, setExecutingSignal] = useState<TradingSignal | null>(null);
  const [generating, setGenerating] = useState(false);
  const query = useSignals(kind);
  const updateStatus = useUpdateSignalStatus();
  const queryClient = useQueryClient();

  const onGenerate = async () => {
    setGenerating(true);
    try {
      const result = await api.signals.generate('Intraday');
      toast.success(`Generated ${result.count ?? 0} signal(s)`);
      queryClient.invalidateQueries({ queryKey: ['signals'] });
    } catch {
      toast.error('Signal generation failed — check backend logs');
    } finally {
      setGenerating(false);
    }
  };

  const signals = useMemo(() => query.data?.signals ?? [], [query.data?.signals]);

  const onUpdateStatus = async (signal: TradingSignal, status: 'expired') => {
    try {
      await updateStatus.mutateAsync({ signalId: signal.id, status });
      toast.success(`Signal ${signal.symbol} marked as ${status}`);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'Failed to update signal status');
    }
  };

  return (
    <>
      <div className="card bg-base-200 shadow-xl">
      <div className="card-body">
        <div className="flex items-center justify-between gap-3 flex-wrap">
          <h2 className="card-title text-2xl">Trading Signals</h2>
          <button
            className={`btn btn-sm btn-primary ${generating ? 'loading' : ''}`}
            onClick={onGenerate}
            disabled={generating}
          >
            {generating ? 'Generating…' : '⚡ Generate Signals'}
          </button>
          <div role="tablist" className="tabs tabs-boxed">
            {(['active', 'overnight', 'intraday'] as const).map((k) => (
              <button
                key={k}
                role="tab"
                className={`tab ${kind === k ? 'tab-active' : ''}`}
                onClick={() => setKind(k)}
                type="button"
              >
                {kindLabel[k]}
              </button>
            ))}
          </div>
        </div>
        <div className="overflow-x-auto">
          <table className="table table-zebra table-xs sm:table-sm">
            <thead>
              <tr>
                <th className="hidden sm:table-cell">Stock</th>
                <th>Signal</th>
                <th>Entry</th>
                <th className="hidden md:table-cell">Target</th>
                <th className="hidden md:table-cell">Stop Loss</th>
                <th className="hidden lg:table-cell">Strength</th>
                <th className="hidden sm:table-cell">Time</th>
                <th className="hidden md:table-cell">Actions</th>
              </tr>
            </thead>
            <tbody>
              {query.isError ? (
                <tr>
                  <td colSpan={8}>
                    <div className="alert alert-error">
                      <span>Failed to load signals.</span>
                    </div>
                  </td>
                </tr>
              ) : query.isLoading ? (
                Array.from({ length: 5 }).map((_, idx) => (
                  <tr key={idx}>
                    <td className="hidden sm:table-cell">
                      <div className="skeleton h-4 w-24" />
                      <div className="skeleton h-3 w-16 mt-2 hidden md:block" />
                    </td>
                    <td>
                      <div className="skeleton h-6 w-16" />
                      <div className="skeleton h-3 w-12 mt-2 sm:hidden" />
                    </td>
                    <td>
                      <div className="skeleton h-4 w-20" />
                    </td>
                    <td className="hidden md:table-cell">
                      <div className="skeleton h-4 w-20" />
                      <div className="skeleton h-3 w-14 mt-2" />
                    </td>
                    <td className="hidden md:table-cell">
                      <div className="skeleton h-4 w-20" />
                      <div className="skeleton h-3 w-14 mt-2" />
                    </td>
                    <td className="hidden lg:table-cell">
                      <div className="skeleton h-5 w-16" />
                    </td>
                    <td className="hidden sm:table-cell">
                      <div className="skeleton h-3 w-16" />
                    </td>
                    <td className="hidden md:table-cell">
                      <div className="skeleton h-7 w-28" />
                    </td>
                  </tr>
                ))
              ) : (
                signals.map((signal) => {
                  const action = normaliseAction(signal.action);
                  const canUpdate = signal.status === 'active';
                  return (
                    <tr key={signal.id}>
                      <td className="hidden sm:table-cell">
                        <div>
                          <div className="font-bold text-xs sm:text-sm">{signal.symbol}</div>
                          <div className="text-xs opacity-70 hidden md:block">
                            {signal.type === 0 ? 'Overnight' : signal.type === 1 ? 'Intraday' : signal.type}
                          </div>
                        </div>
                      </td>
                      <td>
                        <div
                          className={`badge ${action === 'BUY' ? 'badge-success' : 'badge-error'} badge-sm sm:badge-md`}
                        >
                          {action}
                        </div>
                        <div className="text-xs sm:hidden mt-1">{signal.symbol}</div>
                      </td>
                      <td>
                        <div className="font-semibold text-xs sm:text-sm">₹{signal.entryPrice.toFixed(2)}</div>
                      </td>
                      <td className="hidden md:table-cell">
                        <div>
                          <div className="font-semibold text-success text-xs sm:text-sm">
                            ₹{signal.targetPrice.toFixed(2)}
                          </div>
                          <div className="text-xs text-success">
                            +{calculateProfitPercent(signal.entryPrice, signal.targetPrice).toFixed(2)}%
                          </div>
                        </div>
                      </td>
                      <td className="hidden md:table-cell">
                        <div>
                          <div className="font-semibold text-error text-xs sm:text-sm">₹{signal.stopLoss.toFixed(2)}</div>
                          <div className="text-xs text-error">
                            -{calculateLossPercent(signal.entryPrice, signal.stopLoss).toFixed(2)}%
                          </div>
                        </div>
                      </td>
                      <td className="hidden lg:table-cell">
                        <div className={`badge badge-sm ${getSignalStrengthColor(signal.signalStrength)}`}>
                          {signal.signalStrength}%
                        </div>
                      </td>
                      <td className="hidden sm:table-cell">
                        <div className="text-xs opacity-70">{getTimeAgo(signal.generatedAt)}</div>
                      </td>
                      <td className="hidden md:table-cell">
                        <div className="flex gap-2">
                          <button
                            className="btn btn-xs btn-success"
                            type="button"
                            disabled={!canUpdate || updateStatus.isPending}
                            onClick={() => setExecutingSignal(signal)}
                          >
                            Execute
                          </button>
                          <button
                            className="btn btn-xs btn-outline"
                            type="button"
                            disabled={!canUpdate || updateStatus.isPending}
                            onClick={() => onUpdateStatus(signal, 'expired')}
                          >
                            Expire
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
        <div className="card-actions justify-end mt-4">
          <div className="text-sm opacity-70">
            {query.isLoading
              ? 'Loading…'
              : query.isError
                ? 'Failed to load signals'
                : `${signals.length} signal${signals.length !== 1 ? 's' : ''}`}
          </div>
        </div>
      </div>
    </div>
      {executingSignal ? (
        <SignalExecutionModal signal={executingSignal} onClose={() => setExecutingSignal(null)} />
      ) : null}
    </>
  );
}
