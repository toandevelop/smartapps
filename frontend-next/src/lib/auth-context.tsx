'use client';

import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import { SysUser, DemoAccount } from './types';

interface AuthContextValue {
  user: SysUser | null;
  allDemoUsers: DemoAccount[];
  loadingAuth: boolean;
  authServerOnline: boolean;
  hasPermission: (perm: string) => boolean;
  hasAppPermission: (appCode: string | undefined | null, perm: string) => boolean;
  isAppAdmin: (appCode: string | undefined | null) => boolean;
  canPerformTableAction: (
    appCode: string | undefined | null,
    tableId: string | undefined | null,
    action: 'view' | 'create' | 'edit' | 'delete' | 'export' | 'approve'
  ) => boolean;
  canAccessApp: (appCode: string) => boolean;
  login: (username: string, password: string, returnUrl?: string) => Promise<{ success: boolean; error?: string }>;
  switchDemoUser: (username: string, returnUrl?: string) => Promise<{ success: boolean; error?: string }>;
  logout: () => Promise<void>;
  refreshAuth: () => Promise<void>;
}

export function getSafeReturnUrl(raw?: string | null): string {
  if (!raw) return '/';
  const trimmed = raw.trim();
  if (!trimmed.startsWith('/') || trimmed.startsWith('//') || trimmed.startsWith('/login')) {
    return '/';
  }
  return trimmed;
}

const AuthContext = createContext<AuthContextValue>({
  user: null,
  allDemoUsers: [],
  loadingAuth: true,
  authServerOnline: true,
  hasPermission: () => false,
  hasAppPermission: () => false,
  isAppAdmin: () => false,
  canPerformTableAction: () => false,
  canAccessApp: () => false,
  login: async () => ({ success: false }),
  switchDemoUser: async () => ({ success: false }),
  logout: async () => {},
  refreshAuth: async () => {},
});

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname() || '/';
  const [user, setUser] = useState<SysUser | null>(null);
  const [allDemoUsers, setAllDemoUsers] = useState<DemoAccount[]>([]);
  const [loadingAuth, setLoadingAuth] = useState(true);
  const [authServerOnline, setAuthServerOnline] = useState(true);

  const refreshAuth = useCallback(async () => {
    try {
      const [meRes, demoRes] = await Promise.all([
        fetch('/api/auth/me', { credentials: 'include' }),
        fetch('/api/auth/demo-accounts', { credentials: 'include' }),
      ]);
      const reachable = meRes.status < 500 || demoRes.status < 500;
      setAuthServerOnline(reachable);

      if (meRes.ok) {
        const meJson = await meRes.json();
        if (meJson.success && meJson.data) setUser(meJson.data);
        else setUser(null);
      } else {
        setUser(null);
      }
      if (demoRes.ok) {
        const demoJson = await demoRes.json();
        if (demoJson.success && Array.isArray(demoJson.data)) setAllDemoUsers(demoJson.data);
      }
    } catch {
      setUser(null);
      setAuthServerOnline(false);
    } finally {
      setLoadingAuth(false);
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    Promise.resolve().then(() => {
      if (!cancelled) void refreshAuth();
    });
    return () => {
      cancelled = true;
    };
  }, [refreshAuth]);

  useEffect(() => {
    if (!loadingAuth && !user && pathname !== '/login' && !pathname.startsWith('/forms/')) {
      const target =
        typeof window !== 'undefined'
          ? `${window.location.pathname}${window.location.search}`
          : pathname;
      const loginUrl =
        target && target !== '/' && !target.startsWith('/login')
          ? `/login?returnUrl=${encodeURIComponent(target)}`
          : '/login';
      router.replace(loginUrl);
    }
  }, [loadingAuth, user, pathname, router]);

  const isAdmin = Boolean(
    user &&
      (user.isAdmin ||
        user.roleCode?.toUpperCase() === 'ADMIN' ||
        user.roleCodes?.some((r) => r.toUpperCase() === 'ADMIN'))
  );

  const hasPermission = useCallback(
    (perm: string) => {
      if (!user) return false;
      if (isAdmin) return true;
      const sys = user.systemPermissions || [];
      return (
        sys.some((p) => p.toLowerCase() === perm.toLowerCase()) ||
        (user.permissions || []).some((p) => p.toLowerCase() === perm.toLowerCase())
      );
    },
    [user, isAdmin]
  );

  const canAccessApp = useCallback(
    (appCode: string) => {
      if (!user || !appCode) return false;
      if (isAdmin || (user.allowedApps || []).includes('*')) return true;
      if (user.appPermissions) {
        const entry = Object.entries(user.appPermissions).find(
          ([k]) => k.toUpperCase() === appCode.toUpperCase()
        );
        if (entry && entry[1].length > 0) return true;
      }
      return (user.allowedApps || []).some((a) => a.toUpperCase() === appCode.toUpperCase());
    },
    [user, isAdmin]
  );

  const hasAppPermission = useCallback(
    (appCode: string | undefined | null, perm: string) => {
      if (!user) return false;
      if (isAdmin) return true;
      if (!appCode) return hasPermission(perm);

      if (user.appPermissions && Object.keys(user.appPermissions).length > 0) {
        const entry = Object.entries(user.appPermissions).find(
          ([k]) => k.toUpperCase() === appCode.toUpperCase()
        );
        if (entry) {
          return entry[1].some((p) => p.toLowerCase() === perm.toLowerCase());
        }
        if ((user.allowedApps || []).includes('*')) {
          return hasPermission(perm);
        }
        return false;
      }

      return canAccessApp(appCode) && hasPermission(perm);
    },
    [user, isAdmin, hasPermission, canAccessApp]
  );

  const isAppAdmin = useCallback(
    (appCode: string | undefined | null) => {
      if (!user) return false;
      if (isAdmin || hasPermission('studio.manage')) return true;
      return hasAppPermission(appCode, 'app.admin');
    },
    [user, isAdmin, hasPermission, hasAppPermission]
  );

  const canPerformTableAction = useCallback(
    (
      appCode: string | undefined | null,
      tableId: string | undefined | null,
      action: 'view' | 'create' | 'edit' | 'delete' | 'export' | 'approve'
    ) => {
      if (!user) return false;
      if (isAdmin) return true;
      if (appCode && !canAccessApp(appCode)) return false;

      if (tableId && user.tablePermissions) {
        const tp = user.tablePermissions[tableId];
        if (tp) {
          switch (action) {
            case 'view':
              return tp.canView;
            case 'create':
              return tp.canCreate;
            case 'edit':
              return tp.canEdit;
            case 'delete':
              return tp.canDelete;
            case 'export':
              return tp.canExport;
            case 'approve':
              return tp.canApprove;
          }
        }
      }

      return hasAppPermission(appCode, `record.${action}`);
    },
    [user, isAdmin, canAccessApp, hasAppPermission]
  );

  const login = useCallback(
    async (username: string, password: string, returnUrl?: string) => {
      try {
        const res = await fetch('/api/auth/login', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({ username, password }),
        });
        const json = await res.json().catch(() => ({}));
        if (res.ok && json.success && json.data?.user) {
          setAuthServerOnline(true);
          setUser(json.data.user);
          router.replace(getSafeReturnUrl(returnUrl));
          return { success: true };
        }
        return {
          success: false,
          error: json.message || json.error || 'Sai tên đăng nhập hoặc mật khẩu.',
        };
      } catch {
        setAuthServerOnline(false);
        return { success: false, error: 'Không thể kết nối máy chủ xác thực' };
      }
    },
    [router]
  );

  const switchDemoUser = useCallback(
    async (username: string, returnUrl?: string) => {
      try {
        const response = await fetch('/api/auth/demo-switch', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({ username }),
        });
        const res = await response.json().catch(() => ({}));
        if (response.ok && res.success && res.data?.user) {
          setAuthServerOnline(true);
          setUser(res.data.user);
          if (pathname === '/login') {
            router.replace(getSafeReturnUrl(returnUrl));
          } else {
            router.refresh();
          }
          return { success: true };
        }
        return {
          success: false,
          error: res.message || res.error || 'Không thể chuyển tài khoản nhanh.',
        };
      } catch {
        setAuthServerOnline(false);
        return { success: false, error: 'Không thể kết nối máy chủ xác thực' };
      }
    },
    [pathname, router]
  );

  const logout = useCallback(async () => {
    try {
      await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' });
    } finally {
      setUser(null);
      router.replace('/login');
    }
  }, [router]);

  return (
    <AuthContext.Provider
      value={{
        user,
        allDemoUsers,
        loadingAuth,
        authServerOnline,
        hasPermission,
        hasAppPermission,
        isAppAdmin,
        canPerformTableAction,
        canAccessApp,
        login,
        switchDemoUser,
        logout,
        refreshAuth,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  return useContext(AuthContext);
}

export function PermissionGate({
  permission,
  fallback = null,
  children,
}: {
  permission: string;
  fallback?: React.ReactNode;
  children: React.ReactNode;
}) {
  const { hasPermission } = useAuth();
  return hasPermission(permission) ? <>{children}</> : <>{fallback}</>;
}
