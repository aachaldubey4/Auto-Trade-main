export const queryKeys = {
  health: ['health'] as const,
  market: {
    status: ['market', 'status'] as const,
    nifty50: ['market', 'index', 'nifty50'] as const,
    history: (symbol: string, days: number) => ['market', 'history', symbol, days] as const,
  },
  watchlist: ['watchlist'] as const,
  signals: {
    active: ['signals', 'active'] as const,
    overnight: ['signals', 'overnight'] as const,
    intraday: ['signals', 'intraday'] as const,
    history: (params: { from?: string; to?: string; symbol?: string; action?: string; status?: string }) =>
      ['signals', 'history', params] as const,
  },
  news: {
    latest: (params: { page: number; limit: number; stock?: string; sentiment?: string; hours?: number }) =>
      ['news', 'latest', params] as const,
    byStock: (symbol: string, params: { page: number; limit: number }) => ['news', 'byStock', symbol, params] as const,
    sentiment: (symbol: string) => ['news', 'sentiment', symbol] as const,
  },
} as const;

