import { useEffect, useState } from 'react';
import { env } from '../config/env';

export type UserSettings = {
  enableNotifications: boolean;
  signalStrengthThreshold: number;
};

const storageKey = 'auto-trade-settings';

const readSettings = (): UserSettings => {
  if (typeof window === 'undefined') {
    return { enableNotifications: env.enableNotifications, signalStrengthThreshold: 80 };
  }

  try {
    const raw = window.localStorage.getItem(storageKey);
    if (!raw) return { enableNotifications: env.enableNotifications, signalStrengthThreshold: 80 };
    const parsed = JSON.parse(raw) as Partial<UserSettings>;
    return {
      enableNotifications: typeof parsed.enableNotifications === 'boolean' ? parsed.enableNotifications : env.enableNotifications,
      signalStrengthThreshold:
        typeof parsed.signalStrengthThreshold === 'number' && Number.isFinite(parsed.signalStrengthThreshold)
          ? parsed.signalStrengthThreshold
          : 80,
    };
  } catch {
    return { enableNotifications: env.enableNotifications, signalStrengthThreshold: 80 };
  }
};

export const useUserSettings = () => {
  const [settings, setSettings] = useState<UserSettings>(() => readSettings());

  useEffect(() => {
    window.localStorage.setItem(storageKey, JSON.stringify(settings));
  }, [settings]);

  return { settings, setSettings };
};

