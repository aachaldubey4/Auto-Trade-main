import { useQuery } from '@tanstack/react-query';
import { api } from '../services/api';
import { queryKeys } from './queryKeys';

export const useHealth = () => {
  return useQuery({
    queryKey: queryKeys.health,
    queryFn: () => api.health.get(),
    refetchInterval: 60_000,
  });
};

