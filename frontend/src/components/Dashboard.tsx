import { useEffect, useMemo, useRef, useState } from 'react';
import { motion } from 'framer-motion';
import toast from 'react-hot-toast';
import Header from './Header';
import SignalsPanel from './SignalsPanel';
import Portfolio from './Portfolio';
import Watchlist from './Watchlist';
import NewsFeed from './NewsFeed';
import NewsTab from './NewsTab';
import StockChart from './StockChart';
import { useSignals } from '../hooks/useSignals';
import { useUserSettings } from '../hooks/useUserSettings';
import type { SignalAction } from '../types/api';

const normaliseAction = (action: SignalAction): 'BUY' | 'SELL' => {
  if (action === 'BUY' || action === 'SELL') return action;
  return action === 0 ? 'BUY' : 'SELL';
};

const notifiedKey = 'auto-trade-notified-signal-ids';
const readNotifiedIds = (): Set<string> => {
  try {
    const raw = window.localStorage.getItem(notifiedKey);
    if (!raw) return new Set();
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return new Set();
    return new Set(parsed.filter((x): x is string => typeof x === 'string'));
  } catch {
    return new Set();
  }
};

const writeNotifiedIds = (ids: Set<string>) => {
  const list = Array.from(ids).slice(-200);
  window.localStorage.setItem(notifiedKey, JSON.stringify(list));
};

export default function Dashboard() {
  const [selectedStock, setSelectedStock] = useState<string>('RELIANCE');
  const [activeTab, setActiveTab] = useState<'dashboard' | 'news'>('dashboard');
  const { settings } = useUserSettings();
  const activeSignals = useSignals('active');

  const notifiedRef = useRef<Set<string> | null>(null);
  if (notifiedRef.current == null && typeof window !== 'undefined') {
    notifiedRef.current = readNotifiedIds();
  }

  const newHighStrengthSignals = useMemo(() => {
    const threshold = settings.signalStrengthThreshold;
    const all = activeSignals.data?.signals ?? [];
    return all.filter((s) => s.signalStrength >= threshold);
  }, [activeSignals.data?.signals, settings.signalStrengthThreshold]);

  useEffect(() => {
    if (!settings.enableNotifications) return;
    if (typeof window === 'undefined' || !('Notification' in window)) return;
    if (!activeSignals.data) return;

    const notified = notifiedRef.current ?? new Set<string>();
    const fresh = newHighStrengthSignals.filter((s) => !notified.has(s.id));
    if (fresh.length === 0) return;

    if (Notification.permission !== 'granted') {
      // Avoid spamming permission prompts; settings screen requests when enabled.
      return;
    }

    fresh.slice(0, 3).forEach((s) => {
      const side = normaliseAction(s.action);
      const title = `${side} ${s.symbol} (${Math.round(s.signalStrength)}%)`;
      const body = `Entry ₹${s.entryPrice.toFixed(2)} • Target ₹${s.targetPrice.toFixed(2)} • SL ₹${s.stopLoss.toFixed(2)}`;

      try {
        new Notification(title, { body });
      } catch {
        // ignore
      }

      toast(title, { duration: 4000 });
      notified.add(s.id);
    });

    writeNotifiedIds(notified);
    notifiedRef.current = notified;
  }, [activeSignals.data, newHighStrengthSignals, settings.enableNotifications]);

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
}
