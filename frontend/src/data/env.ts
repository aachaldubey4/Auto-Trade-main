const requireString = (key: string, fallback?: string): string => {
  const value = (import.meta.env[key] as string | undefined) ?? fallback;
  if (!value || value.trim().length === 0) {
    throw new Error(`Missing required environment variable: ${key}`);
  }
  return value;
};

const parseBool = (value: string | undefined, fallback: boolean): boolean => {
  if (value == null) return fallback;
  const normalised = value.trim().toLowerCase();
  if (['1', 'true', 'yes', 'y', 'on'].includes(normalised)) return true;
  if (['0', 'false', 'no', 'n', 'off'].includes(normalised)) return false;
  return fallback;
};

const parseIntSafe = (value: string | undefined, fallback: number): number => {
  if (value == null) return fallback;
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? parsed : fallback;
};

export const env = {
  apiUrl: requireString('VITE_API_URL', 'http://localhost:5265/api'),
  apiTimeoutMs: parseIntSafe(import.meta.env.VITE_API_TIMEOUT as string | undefined, 10_000),
  enableNotifications: parseBool(
    import.meta.env.VITE_ENABLE_NOTIFICATIONS as string | undefined,
    true,
  ),
} as const;

