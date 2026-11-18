'use client';

import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';
import { SysApp, EngineStatus } from './types';
import { useAuth } from './auth-context';

interface PlatformContextValue {
  apps: SysApp[];
  loadingApps: boolean;
  engineStatus: EngineStatus | null;
  refreshApps: () => Promise<void>;
}

const PlatformContext = createContext<PlatformContextValue>({
  apps: [],
  loadingApps: true,
  engineStatus: null,
  refreshApps: async () => {},
});

export function PlatformProvider({ children }: { children: React.ReactNode }) {
  const { user, loadingAuth } = useAuth();
  const [apps, setApps] = useState<SysApp[]>([]);
  const [loadingApps, setLoadingApps] = useState(true);
  const [engineStatus, setEngineStatus] = useState<EngineStatus | null>(null);

  const refreshApps = useCallback(async () => {
    if (!user) {
      setApps([]);
      setLoadingApps(false);
      return;
    }
    setLoadingApps(true);
    try {
      const [appsRes, statusRes] = await Promise.all([
        fetch('/api/apps', { credentials: 'include' }),
        fetch('/api/apps/engine-status', { credentials: 'include' }).catch(() => null),
      ]);
      if (appsRes.ok) {
        const appsJson = await appsRes.json();
        if (appsJson.success && Array.isArray(appsJson.data)) {
          setApps(appsJson.data);
        }
      } else {
        setApps([]);
      }
      if (statusRes && statusRes.ok) {
        const statusJson = await statusRes.json();
        if (statusJson.success) setEngineStatus(statusJson.data);
      }
    } catch (e) {
      console.error('Failed to load platform apps:', e);
    } finally {
      setLoadingApps(false);
    }
  }, [user]);

  useEffect(() => {
    if (!loadingAuth) {
      refreshApps();
    }
  }, [loadingAuth, user?.username, user?.roleCode, refreshApps]);

  return (
    <PlatformContext.Provider value={{ apps, loadingApps, engineStatus, refreshApps }}>
      {children}
    </PlatformContext.Provider>
  );
}

export function usePlatform() {
  return useContext(PlatformContext);
}
