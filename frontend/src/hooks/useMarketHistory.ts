import { useQuery } from '@tanstack/react-query';
import { api } from '../services/api';
import { queryKeys } from './queryKeys';

export const useMarketHistory = (symbol: string, days: number) => {
  return useQuery({
    queryKey: queryKeys.market.history(symbol, days),
    queryFn: () => api.market.history(symbol, days),
    enabled: symbol.trim().length > 0 && days > 0,
  });
};

