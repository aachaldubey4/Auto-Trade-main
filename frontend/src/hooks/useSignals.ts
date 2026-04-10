import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ExecuteSignalRequest, SignalsResponse, SignalStatus } from '../types/api';
import { api } from '../services/api';
import { queryKeys } from './queryKeys';

export type SignalsKind = 'active' | 'overnight' | 'intraday';

const fetchSignals = async (kind: SignalsKind): Promise<SignalsResponse> => {
  switch (kind) {
    case 'active':
      return api.signals.active();
    case 'overnight':
      return api.signals.overnight();
    case 'intraday':
      return api.signals.intraday();
  }
};

export const useSignals = (kind: SignalsKind) => {
  const queryKey =
    kind === 'active'
      ? queryKeys.signals.active
      : kind === 'overnight'
        ? queryKeys.signals.overnight
        : queryKeys.signals.intraday;

  return useQuery({
    queryKey,
    queryFn: () => fetchSignals(kind),
    refetchInterval: 30_000,
  });
};

export const useUpdateSignalStatus = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (args: { signalId: string; status: SignalStatus }) =>
      api.signals.updateStatus(args.signalId, { status: args.status }),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.signals.active }),
        queryClient.invalidateQueries({ queryKey: queryKeys.signals.overnight }),
        queryClient.invalidateQueries({ queryKey: queryKeys.signals.intraday }),
      ]);
    },
  });
};

export const useExecuteSignal = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (args: { signalId: string; payload: ExecuteSignalRequest }) =>
      api.signals.execute(args.signalId, args.payload),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.signals.active }),
        queryClient.invalidateQueries({ queryKey: queryKeys.signals.overnight }),
        queryClient.invalidateQueries({ queryKey: queryKeys.signals.intraday }),
        queryClient.invalidateQueries({ queryKey: ['signals', 'history'] }),
      ]);
    },
  });
};

