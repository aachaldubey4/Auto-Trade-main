import { useHealth } from '../hooks/useHealth';
import { useMarketStatus } from '../hooks/useMarketStatus';
import { useNiftyIndex } from '../hooks/useNiftyIndex';
import { useTheme } from '../hooks/useTheme';
import { Moon, Settings as SettingsIcon, Sun } from 'lucide-react';
import { useState } from 'react';
import Settings from './Settings';

export default function Header() {
  const health = useHealth();
  const market = useMarketStatus();
  const nifty = useNiftyIndex();
  const { theme, toggle } = useTheme();
  const [settingsOpen, setSettingsOpen] = useState(false);

  const formatTime = (timestamp: string) => {
    return new Date(timestamp).toLocaleTimeString('en-IN', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  };

  const isMarketOpen = market.data?.isOpen ?? false;

  return (
    <div className="navbar bg-base-200 shadow-lg sticky top-0 z-20">
      <div className="flex-1">
        <a className="btn btn-ghost text-xl font-bold">Auto Trade</a>
      </div>
      <div className="flex-none gap-2 sm:gap-4">
        <div className="flex flex-col sm:flex-row items-end sm:items-center gap-2 sm:gap-4">
          <div className="text-right hidden sm:block">
            <div className="text-xs opacity-70">Nifty 50</div>
            <div className="flex items-center gap-2">
              <span className="font-bold text-base sm:text-lg">
                {nifty.data
                  ? nifty.data.value.toLocaleString('en-IN', {
                      minimumFractionDigits: 2,
                      maximumFractionDigits: 2,
                    })
                  : '—'}
              </span>
              <span
                className={`text-xs sm:text-sm font-semibold ${
                  (nifty.data?.change ?? 0) >= 0 ? 'text-success' : 'text-error'
                }`}
              >
                {nifty.data ? (
                  <>
                    {nifty.data.change >= 0 ? '+' : ''}
                    {nifty.data.change.toFixed(2)} (
                    {nifty.data.changePercent >= 0 ? '+' : ''}
                    {nifty.data.changePercent.toFixed(2)}%)
                  </>
                ) : (
                  '—'
                )}
              </span>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <div
              className={`badge badge-sm sm:badge-md ${
                isMarketOpen ? 'badge-success' : 'badge-error'
              }`}
            >
              {isMarketOpen ? 'Open' : 'Closed'}
            </div>
            <div
              className={`badge badge-sm sm:badge-md ${
                health.isError ? 'badge-error' : health.isLoading ? 'badge-warning' : 'badge-success'
              }`}
            >
              {health.isError ? 'Backend down' : health.isLoading ? 'Connecting' : 'Backend OK'}
            </div>
            <button className="btn btn-ghost btn-sm" type="button" onClick={toggle} aria-label="Toggle theme">
              {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
            </button>
            <button
              className="btn btn-ghost btn-sm"
              type="button"
              onClick={() => setSettingsOpen(true)}
              aria-label="Open settings"
            >
              <SettingsIcon size={18} />
            </button>
          </div>
          <div className="text-xs opacity-70 hidden sm:block">
            Last: {formatTime(market.data?.serverTimeIst ?? new Date().toISOString())}
          </div>
        </div>
      </div>
      <Settings open={settingsOpen} onClose={() => setSettingsOpen(false)} />
    </div>
  );
}
