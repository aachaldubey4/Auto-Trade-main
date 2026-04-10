import { useQuery } from '@tanstack/react-query';
import { api } from '../services/api';
import { queryKeys } from './queryKeys';

export const useWatchlist = () => {
  return useQuery({
    queryKey: queryKeys.watchlist,
    queryFn: () => api.watchlist.get(),
    refetchInterval: 60_000,
    retry: 0,
  });
};

