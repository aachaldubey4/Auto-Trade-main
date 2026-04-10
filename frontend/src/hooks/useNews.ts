import { useQuery } from '@tanstack/react-query';
import type { NewsResponse, FeedSource, StockSentiment } from '../types/api';
import { api } from '../services/api';
import { queryKeys } from './queryKeys';

export interface LatestNewsParams {
  page: number;
  limit: number;
  stock?: string;
  sentiment?: string;
  hours?: number;
}

export const useLatestNews = (params: LatestNewsParams) => {
  return useQuery({
    queryKey: queryKeys.news.latest(params),
    queryFn: async (): Promise<NewsResponse> => {
      const { response } = await api.news.latest(params);
      return response;
    },
    refetchInterval: 120_000,
  });
};

export const useNewsByStock = (symbol: string, params: { page: number; limit: number }) => {
  return useQuery({
    queryKey: queryKeys.news.byStock(symbol, params),
    queryFn: () => api.news.byStock(symbol, params),
    enabled: symbol.trim().length > 0,
    refetchInterval: 120_000,
  });
};

export const useFeedSources = () => {
  return useQuery<FeedSource[]>({
    queryKey: ['news', 'sources'],
    queryFn: () => api.news.sources(),
    refetchInterval: 30_000,
  });
};

export const useSentimentSummary = () => {
  return useQuery<StockSentiment[]>({
    queryKey: ['news', 'sentiment-summary'],
    queryFn: () => api.news.sentimentSummary(),
    refetchInterval: 60_000,
  });
};
