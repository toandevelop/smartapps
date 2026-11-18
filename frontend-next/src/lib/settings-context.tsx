'use client';

import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';
import { UserSettings, SystemSettings } from './types';
import { useAuth } from './auth-context';
import { AppLanguage, TranslationKey, normalizeLanguage, translate } from './i18n';

export interface UnreadSummary {
  unreadChatCount: number;
  pendingTaskCount: number;
  unreadNotifCount: number;
}

const DEFAULT_USER_SETTINGS: UserSettings = {
  userId: '',
  themeMode: 'light',
  accentColor: '#1a73e8',
  compactMode: false,
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
  setLanguage: (lang: AppLanguage) => Promise<void>;
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
  setLanguage: async () => {},
  t: (key, vars) => translate('vi', key, vars),
  refreshSettings: async () => {},
  refreshUnreadSummary: async () => {},
  saveUserSettings: async () => false,
  saveSystemSettings: async () => false,
  playNotificationSound: () => {},
  showDesktopNotification: () => {},
});

function darkenHex(hex: string, amount = 24): string {
  const clean = hex.replace('#', '');
  if (clean.length !== 6) return '#1557b0';
  const r = Math.max(0, parseInt(clean.slice(0, 2), 16) - amount);
  const g = Math.max(0, parseInt(clean.slice(2, 4), 16) - amount);
  const b = Math.max(0, parseInt(clean.slice(4, 6), 16) - amount);
  return `#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}${b.toString(16).padStart(2, '0')}`;
}

export function SettingsProvider({ children }: { children: React.ReactNode }) {
  const { user, loadingAuth } = useAuth();
  const [userSettings, setUserSettings] = useState<UserSettings>(DEFAULT_USER_SETTINGS);
  const [systemSettings, setSystemSettings] = useState<SystemSettings>(DEFAULT_SYSTEM_SETTINGS);
  const [unreadSummary, setUnreadSummary] = useState<UnreadSummary>({
    unreadChatCount: 0,
    pendingTaskCount: 0,
    unreadNotifCount: 0,
  });
  const [localLang, setLocalLang] = useState<AppLanguage>(() => {
    if (typeof window === 'undefined') return 'vi';
    return normalizeLanguage(localStorage.getItem('smartapps_lang'));
  });

  const language: AppLanguage = user ? normalizeLanguage(userSettings.language || localLang) : localLang;

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
    [language]
  );

  const applyVisualTheme = useCallback((settings: UserSettings) => {
    if (typeof document === 'undefined') return;
    const root = document.documentElement;
    const body = document.body;

    let isDark = settings.themeMode === 'dark';
    if (settings.themeMode === 'auto' && typeof window !== 'undefined') {
      isDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    }

    body.classList.toggle('theme-dark', isDark);
    body.classList.toggle('density-compact', Boolean(settings.compactMode));

    const accent = settings.accentColor || '#1a73e8';
    root.style.setProperty('--accent-primary', accent);
    root.style.setProperty('--accent-primary-hover', darkenHex(accent, 22));
    root.style.setProperty('--accent-primary-container', isDark ? `${accent}29` : `${accent}1a`);
    root.style.setProperty('--border-focus', accent);
  }, []);

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
      applyVisualTheme(DEFAULT_USER_SETTINGS);
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
          setUserSettings(j.data.settings);
          applyVisualTheme(j.data.settings);
        }
      }
      if (sysRes.ok) {
        const j = await sysRes.json();
        if (j.success && j.data) {
          setSystemSettings(j.data);
        }
      }
    } catch {
      // ignore
    }
  }, [user, applyVisualTheme]);

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
        setSystemSettings(detail.data);
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

  const saveUserSettings = useCallback(
    async (next: UserSettings) => {
      setUserSettings(next);
      applyVisualTheme(next);
      const res = await fetch('/api/settings/me', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(next),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setUserSettings(res.data);
        applyVisualTheme(res.data);
        return true;
      }
      return false;
    },
    [applyVisualTheme]
  );

  const saveSystemSettings = useCallback(async (next: SystemSettings) => {
    const res = await fetch('/api/settings/system', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(next),
    }).then((r) => r.json());
    if (res.success && res.data) {
      setSystemSettings(res.data);
      return true;
    }
    return false;
  }, []);

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
    [userSettings.notifyDesktop]
  );

  return (
    <SettingsContext.Provider
      value={{
        userSettings,
        systemSettings,
        unreadSummary,
        language,
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
