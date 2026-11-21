'use client';

import React, { createContext, useContext, useState, useEffect, useCallback, useMemo } from 'react';
import { UserSettings, SystemSettings } from './types';
import { useAuth } from './auth-context';
import { AppLanguage, TranslationKey, normalizeLanguage, translate } from './i18n';
import {
  buildBrandPalette,
  DEFAULT_BRAND_HEX,
  DensityMode,
  normalizeDensityMode,
  normalizePinnedApps,
  resolveEffectiveBrandColor,
} from './color-theme';

export interface UnreadSummary {
  unreadChatCount: number;
  pendingTaskCount: number;
  unreadNotifCount: number;
}

const DEFAULT_USER_SETTINGS: UserSettings = {
  userId: '',
  themeMode: 'light',
  accentColor: 'brand',
  compactMode: false,
  densityMode: 'comfortable',
  pinnedApps: [],
  language: 'vi',
  notifyInApp: true,
  notifySound: true,
  notifyDesktop: false,
  notifyChat: true,
  notifyTaskAssigned: true,
  notifyApproval: true,
  notifyMentions: true,
  emailDigest: 'INSTANT',
  chatEnterToSend: true,
  notifyEmail: false,
  notifyTelegram: false,
  telegramChatId: '',
  notifyZalo: false,
  zaloUserId: '',
};

const DEFAULT_SYSTEM_SETTINGS: SystemSettings = {
  companyName: 'SmartApps Enterprise',
  workspaceTitle: 'SmartApps Enterprise — Không gian làm việc Đa ứng dụng',
  brandColor: DEFAULT_BRAND_HEX,
  logoUrl: '',
  brandIcon: '',
  faviconUrl: '',
  announcementBanner: '',
  allowChatFileUpload: true,
  allowTaskFromChat: true,
  defaultTaskAppCode: 'OPS',
  sessionTimeoutDays: 7,
};

interface SettingsContextValue {
  userSettings: UserSettings;
  systemSettings: SystemSettings;
  unreadSummary: UnreadSummary;
  language: AppLanguage;
  resolvedThemeMode: 'light' | 'dark';
  effectiveBrandColor: string;
  densityMode: DensityMode;
  pinnedApps: string[];
  setLanguage: (lang: AppLanguage) => Promise<void>;
  setDensityMode: (mode: DensityMode) => Promise<void>;
  togglePinnedApp: (appCode: string) => Promise<void>;
  t: (key: TranslationKey, vars?: Record<string, string | number>) => string;
  refreshSettings: () => Promise<void>;
  refreshUnreadSummary: () => Promise<void>;
  saveUserSettings: (next: UserSettings) => Promise<boolean>;
  saveSystemSettings: (next: SystemSettings) => Promise<boolean>;
  playNotificationSound: () => void;
  showDesktopNotification: (title: string, body: string, linkUrl?: string | null) => void;
}

const SettingsContext = createContext<SettingsContextValue>({
  userSettings: DEFAULT_USER_SETTINGS,
  systemSettings: DEFAULT_SYSTEM_SETTINGS,
  unreadSummary: { unreadChatCount: 0, pendingTaskCount: 0, unreadNotifCount: 0 },
  language: 'vi',
  resolvedThemeMode: 'light',
  effectiveBrandColor: DEFAULT_BRAND_HEX,
  densityMode: 'comfortable',
  pinnedApps: [],
  setLanguage: async () => {},
  setDensityMode: async () => {},
  togglePinnedApp: async () => {},
  t: (key, vars) => translate('vi', key, vars),
  refreshSettings: async () => {},
  refreshUnreadSummary: async () => {},
  saveUserSettings: async () => false,
  saveSystemSettings: async () => false,
  playNotificationSound: () => {},
  showDesktopNotification: () => {},
});

export function SettingsProvider({ children }: { children: React.ReactNode }) {
  const { user, loadingAuth } = useAuth();
  const [userSettings, setUserSettings] = useState<UserSettings>(DEFAULT_USER_SETTINGS);
  const [systemSettings, setSystemSettings] = useState<SystemSettings>(DEFAULT_SYSTEM_SETTINGS);
  const [osPrefersDark, setOsPrefersDark] = useState(false);
  const [unreadSummary, setUnreadSummary] = useState<UnreadSummary>({
    unreadChatCount: 0,
    pendingTaskCount: 0,
    unreadNotifCount: 0,
  });
  const [localLang, setLocalLang] = useState<AppLanguage>(() => {
    if (typeof window === 'undefined') return 'vi';
    return normalizeLanguage(localStorage.getItem('smartapps_lang'));
  });

  // Track OS prefers-color-scheme changes for themeMode === 'auto'
  useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return;
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const sync = () => setOsPrefersDark(media.matches);
    sync();
    media.addEventListener('change', sync);
    return () => media.removeEventListener('change', sync);
  }, []);

  const language: AppLanguage = user ? normalizeLanguage(userSettings.language || localLang) : localLang;
  const resolvedThemeMode: 'light' | 'dark' =
    userSettings.themeMode === 'dark' || (userSettings.themeMode === 'auto' && osPrefersDark) ? 'dark' : 'light';
  const densityMode: DensityMode = normalizeDensityMode(userSettings.densityMode, userSettings.compactMode);
  const pinnedApps = useMemo(() => normalizePinnedApps(userSettings.pinnedApps), [userSettings.pinnedApps]);
  const effectiveBrandColor = useMemo(
    () => resolveEffectiveBrandColor(userSettings.accentColor, systemSettings.brandColor),
    [userSettings.accentColor, systemSettings.brandColor],
  );

  useEffect(() => {
    if (typeof document !== 'undefined') {
      document.documentElement.lang = language;
    }
    if (typeof window !== 'undefined') {
      localStorage.setItem('smartapps_lang', language);
    }
  }, [language]);

  const t = useCallback(
    (key: TranslationKey, vars?: Record<string, string | number>) => translate(language, key, vars),
    [language],
  );

  // Sync visual tokens, color-scheme, density, and dynamic brand palette to :root & body
  useEffect(() => {
    if (typeof document === 'undefined') return;
    const root = document.documentElement;
    const body = document.body;
    const isDark = resolvedThemeMode === 'dark';

    root.classList.toggle('theme-dark', isDark);
    body.classList.toggle('theme-dark', isDark);
    root.style.colorScheme = isDark ? 'dark' : 'light';

    body.classList.toggle('density-compact', densityMode === 'compact');
    body.classList.toggle('density-spacious', densityMode === 'spacious');

    const palette = buildBrandPalette(effectiveBrandColor, isDark);
    root.style.setProperty('--accent-primary', palette.primary);
    root.style.setProperty('--accent-primary-hover', palette.hover);
    root.style.setProperty('--accent-primary-active', palette.active);
    root.style.setProperty('--accent-on-primary', palette.onPrimary);
    root.style.setProperty('--accent-primary-container', palette.container);
    root.style.setProperty('--accent-on-container', palette.onContainer);
    root.style.setProperty('--border-focus', palette.primary);
    root.style.setProperty('--border-glow', palette.ring);
  }, [resolvedThemeMode, densityMode, effectiveBrandColor]);

  const refreshUnreadSummary = useCallback(async () => {
    if (!user) {
      setUnreadSummary({ unreadChatCount: 0, pendingTaskCount: 0, unreadNotifCount: 0 });
      return;
    }
    try {
      const res = await fetch('/api/chat/unread-summary', { credentials: 'include' });
      if (!res.ok) return;
      const json = await res.json();
      if (json.success && json.data) {
        setUnreadSummary({
          unreadChatCount: json.data.unreadChatCount ?? 0,
          pendingTaskCount: json.data.pendingTaskCount ?? 0,
          unreadNotifCount: json.data.unreadNotifCount ?? 0,
        });
      }
    } catch {
      // ignore
    }
  }, [user]);

  const refreshSettings = useCallback(async () => {
    if (!user) {
      setUserSettings(DEFAULT_USER_SETTINGS);
      return;
    }
    try {
      const [meRes, sysRes] = await Promise.all([
        fetch('/api/settings/me', { credentials: 'include' }),
        fetch('/api/settings/system', { credentials: 'include' }),
      ]);
      if (meRes.ok) {
        const j = await meRes.json();
        if (j.success && j.data?.settings) {
          const s = j.data.settings as UserSettings;
          const nextDensity = normalizeDensityMode(s.densityMode, s.compactMode);
          setUserSettings({
            ...DEFAULT_USER_SETTINGS,
            ...s,
            densityMode: nextDensity,
            compactMode: nextDensity === 'compact',
            pinnedApps: normalizePinnedApps(s.pinnedApps),
          });
        }
      }
      if (sysRes.ok) {
        const j = await sysRes.json();
        if (j.success && j.data) {
          setSystemSettings({ ...DEFAULT_SYSTEM_SETTINGS, ...j.data });
        }
      }
    } catch {
      // ignore
    }
  }, [user]);

  useEffect(() => {
    if (loadingAuth) return;
    let cancelled = false;
    Promise.resolve().then(() => {
      if (cancelled) return;
      void refreshSettings();
      void refreshUnreadSummary();
    });
    return () => {
      cancelled = true;
    };
  }, [loadingAuth, user?.username, refreshSettings, refreshUnreadSummary]);

  useEffect(() => {
    const onRealtime = (e: Event) => {
      const detail = (e as CustomEvent<{ type?: string; data?: SystemSettings }>).detail;
      if (!detail) return;
      if (detail.type === 'system_settings_updated' && detail.data) {
        setSystemSettings({ ...DEFAULT_SYSTEM_SETTINGS, ...detail.data });
      }
      if (
        detail.type === 'chat_message' ||
        detail.type === 'task_updated' ||
        detail.type === 'notification'
      ) {
        refreshUnreadSummary();
      }
    };
    window.addEventListener('smartapps_realtime', onRealtime);
    return () => window.removeEventListener('smartapps_realtime', onRealtime);
  }, [refreshUnreadSummary]);

  useEffect(() => {
    const iconUrl = systemSettings.faviconUrl?.trim();
    if (!iconUrl || typeof document === 'undefined') return;
    let link = document.querySelector<HTMLLinkElement>("link[rel~='icon']");
    if (!link) {
      link = document.createElement('link');
      link.rel = 'icon';
      document.head.appendChild(link);
    }
    link.href = iconUrl;
  }, [systemSettings.faviconUrl]);

  const saveUserSettings = useCallback(async (next: UserSettings) => {
    const normalizedDensity = normalizeDensityMode(next.densityMode, next.compactMode);
    const payload: UserSettings = {
      ...next,
      densityMode: normalizedDensity,
      compactMode: normalizedDensity === 'compact',
      pinnedApps: normalizePinnedApps(next.pinnedApps),
    };
    setUserSettings(payload);
    try {
      const res = await fetch('/api/settings/me', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(payload),
      }).then((r) => r.json());
      if (res.success && res.data) {
        const d = res.data as UserSettings;
        const dMode = normalizeDensityMode(d.densityMode, d.compactMode);
        setUserSettings({
          ...DEFAULT_USER_SETTINGS,
          ...d,
          densityMode: dMode,
          compactMode: dMode === 'compact',
          pinnedApps: normalizePinnedApps(d.pinnedApps),
        });
        return true;
      }
      return false;
    } catch {
      return false;
    }
  }, []);

  const saveSystemSettings = useCallback(async (next: SystemSettings) => {
    try {
      const res = await fetch('/api/settings/system', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(next),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setSystemSettings({ ...DEFAULT_SYSTEM_SETTINGS, ...res.data });
        return true;
      }
      return false;
    } catch {
      return false;
    }
  }, []);

  const setDensityMode = useCallback(
    async (mode: DensityMode) => {
      const updated: UserSettings = {
        ...userSettings,
        densityMode: mode,
        compactMode: mode === 'compact',
      };
      if (user) {
        await saveUserSettings(updated);
      } else {
        setUserSettings(updated);
      }
    },
    [user, userSettings, saveUserSettings],
  );

  const togglePinnedApp = useCallback(
    async (appCode: string) => {
      const code = appCode.trim().toUpperCase();
      if (!code) return;
      const current = normalizePinnedApps(userSettings.pinnedApps);
      const nextPinned = current.includes(code)
        ? current.filter((c) => c !== code)
        : normalizePinnedApps([code, ...current]);
      const updated: UserSettings = {
        ...userSettings,
        pinnedApps: nextPinned,
      };
      if (user) {
        await saveUserSettings(updated);
      } else {
        setUserSettings(updated);
      }
    },
    [user, userSettings, saveUserSettings],
  );

  const playNotificationSound = useCallback(() => {
    if (!userSettings.notifySound || typeof window === 'undefined') return;
    try {
      const AudioCtx = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
      if (!AudioCtx) return;
      const ctx = new AudioCtx();
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(740, ctx.currentTime);
      osc.frequency.exponentialRampToValueAtTime(988, ctx.currentTime + 0.08);
      gain.gain.setValueAtTime(0.06, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.22);
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.start();
      osc.stop(ctx.currentTime + 0.22);
    } catch {
      // ignore WebAudio restrictions
    }
  }, [userSettings.notifySound]);

  const showDesktopNotification = useCallback(
    (title: string, body: string, linkUrl?: string | null) => {
      if (!userSettings.notifyDesktop || typeof window === 'undefined' || !('Notification' in window)) return;
      if (Notification.permission !== 'granted') return;
      try {
        const n = new Notification(title, { body });
        if (linkUrl) {
          n.onclick = () => {
            window.focus();
            window.location.href = linkUrl;
          };
        }
      } catch {
        // ignore
      }
    },
    [userSettings.notifyDesktop],
  );

  return (
    <SettingsContext.Provider
      value={{
        userSettings,
        systemSettings,
        unreadSummary,
        language,
        resolvedThemeMode,
        effectiveBrandColor,
        densityMode,
        pinnedApps,
        setLanguage: async (nextLang: AppLanguage) => {
          const normalized = normalizeLanguage(nextLang);
          setLocalLang(normalized);
          if (typeof window !== 'undefined') {
            localStorage.setItem('smartapps_lang', normalized);
          }
          if (typeof document !== 'undefined') {
            document.documentElement.lang = normalized;
          }
          const updated = { ...userSettings, language: normalized };
          setUserSettings(updated);
          if (user) {
            await saveUserSettings(updated);
          }
        },
        setDensityMode,
        togglePinnedApp,
        t,
        refreshSettings,
        refreshUnreadSummary,
        saveUserSettings,
        saveSystemSettings,
        playNotificationSound,
        showDesktopNotification,
      }}
    >
      {children}
    </SettingsContext.Provider>
  );
}

export function useSettings() {
  return useContext(SettingsContext);
}
