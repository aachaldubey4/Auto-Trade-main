import { useQuery } from '@tanstack/react-query';
import { api } from '../services/api';
import { queryKeys } from './queryKeys';

export const useNiftyIndex = () => {
  return useQuery({
    queryKey: queryKeys.market.nifty50,
    queryFn: () => api.market.nifty50(),
    retry: 0,
    refetchInterval: (query) => (query.state.data ? 60_000 : false),
  });
};

