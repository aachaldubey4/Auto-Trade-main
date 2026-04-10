import { useQuery } from '@tanstack/react-query';
import { api } from '../services/api';
import { queryKeys } from './queryKeys';

export const useMarketStatus = () => {
  return useQuery({
    queryKey: queryKeys.market.status,
    queryFn: () => api.market.status(),
    refetchInterval: 60_000,
  });
};

