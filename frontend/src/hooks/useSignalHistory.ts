import { useQuery } from '@tanstack/react-query';
import { api } from '../services/api';
import { queryKeys } from './queryKeys';

export const useSignalHistory = (params: {
  from?: string;
  to?: string;
  symbol?: string;
  action?: string;
  status?: string;
}) => {
  return useQuery({
    queryKey: queryKeys.signals.history(params),
    queryFn: () => api.signals.history(params),
  });
};

