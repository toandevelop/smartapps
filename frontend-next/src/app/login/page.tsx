'use client';

import React, { useState, useEffect, useMemo, useRef, Suspense, useCallback } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import PersonRoundedIcon from '@mui/icons-material/PersonRounded';
import LoginRoundedIcon from '@mui/icons-material/LoginRounded';
import AdminPanelSettingsRoundedIcon from '@mui/icons-material/AdminPanelSettingsRounded';
import KeyRoundedIcon from '@mui/icons-material/KeyRounded';
import VisibilityRoundedIcon from '@mui/icons-material/VisibilityRounded';
import VisibilityOffRoundedIcon from '@mui/icons-material/VisibilityOffRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import ErrorOutlineRoundedIcon from '@mui/icons-material/ErrorOutlineRounded';
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded';
import HelpOutlineRoundedIcon from '@mui/icons-material/HelpOutlineRounded';
import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded';
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded';
import DesktopWindowsRoundedIcon from '@mui/icons-material/DesktopWindowsRounded';
import EditNoteRoundedIcon from '@mui/icons-material/EditNoteRounded';
import VerifiedUserRoundedIcon from '@mui/icons-material/VerifiedUserRounded';
import WifiOffRoundedIcon from '@mui/icons-material/WifiOffRounded';
import { useAuth, getSafeReturnUrl } from '@/lib/auth-context';
import { SsoProviderPublic, DemoAccount } from '@/lib/types';
import { isTauriDesktop, getDesktopServerConfig, setDesktopServerConfig } from '@/lib/tauri-bridge';

const REMEMBER_USER_KEY = 'smartapps_remembered_username';
const LOGIN_THEME_KEY = 'smartapps_login_theme';

function GoogleBrandIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true">
      <path
        fill="#4285F4"
        d="M23.745 12.27c0-.7-.06-1.4-.19-2.07H12v4.51h6.6c-.29 1.52-1.14 2.82-2.4 3.68v3.05h3.88c2.27-2.09 3.665-5.17 3.665-9.17Z"
      />
      <path
        fill="#34A853"
        d="M12 24c3.24 0 5.95-1.08 7.93-2.91l-3.88-3.05c-1.08.72-2.45 1.16-4.05 1.16-3.12 0-5.77-2.11-6.72-4.96H1.29v3.14C3.26 21.3 7.31 24 12 24Z"
      />
      <path
        fill="#FBBC05"
        d="M5.28 14.24c-.24-.72-.38-1.49-.38-2.24s.14-1.52.38-2.24V6.62H1.29C.47 8.24 0 10.06 0 12s.47 3.76 1.29 5.38l3.99-3.14Z"
      />
      <path
        fill="#EA4335"
        d="M12 4.75c1.77 0 3.35.61 4.6 1.8l3.42-3.42C17.95 1.19 15.24 0 12 0 7.31 0 3.26 2.7 1.29 6.62l3.99 3.14c.95-2.85 3.6-4.96 6.72-4.96Z"
      />
    </svg>
  );
}

function MicrosoftBrandIcon() {
  return (
    <svg width="17" height="17" viewBox="0 0 21 21" aria-hidden="true">
      <rect x="1" y="1" width="9" height="9" fill="#f25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
      <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
      <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
    </svg>
  );
}

function getInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return 'SA';
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
}

function getAvatarColor(roleCode: string): string {
  const upper = roleCode.toUpperCase();
  if (upper.includes('ADMIN')) return '#1a73e8';
  if (upper.includes('MANAGER') || upper.includes('LEAD')) return '#00897b';
  if (upper.includes('HR')) return '#7b1fa2';
  if (upper.includes('FIN') || upper.includes('ACC')) return '#ea8600';
  return '#475569';
}

function extractLockoutSeconds(message: string): number | null {
  const match = message.match(/sau\s+(\d+)\s+giây/i);
  if (!match) return null;
  const secs = parseInt(match[1], 10);
  return Number.isFinite(secs) && secs > 0 ? secs : null;
}

function SsoProviderIcon({ provider }: { provider: SsoProviderPublic }) {
  if (provider.providerType === 'GOOGLE') return <GoogleBrandIcon />;
  if (provider.providerType === 'MICROSOFT') return <MicrosoftBrandIcon />;
  return <KeyRoundedIcon sx={{ fontSize: 18, color: provider.buttonColor || '#00897b' }} />;
}

function ForgotPasswordModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  if (!open) return null;
  return (
    <div className="modal-overlay" role="dialog" aria-modal="true" aria-labelledby="forgot-pw-title" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 460 }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 14 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <div
              style={{
                width: 36,
                height: 36,
                borderRadius: 10,
                backgroundColor: 'var(--info-bg)',
                color: 'var(--info-text)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <HelpOutlineRoundedIcon sx={{ fontSize: 20 }} />
            </div>
            <div>
              <h3 id="forgot-pw-title" style={{ fontSize: 16, fontWeight: 700, margin: 0 }}>
                Hỗ trợ khôi phục mật khẩu
              </h3>
              <p style={{ fontSize: 12, color: 'var(--text-muted)', margin: 0 }}>
                Quy trình bảo mật tài khoản doanh nghiệp
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng hộp thoại"
            className="btn btn-outline btn-sm"
            style={{ width: 32, padding: 0, borderRadius: '50%' }}
          >
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 10, fontSize: 13, color: 'var(--text-secondary)' }}>
          <div
            style={{
              padding: '12px 14px',
              borderRadius: 12,
              backgroundColor: 'var(--bg-primary)',
              border: '1px solid var(--border-light)',
            }}
          >
            <div style={{ fontWeight: 700, color: 'var(--text-primary)', marginBottom: 4 }}>
              1. Liên hệ Quản trị viên hệ thống
            </div>
            <p style={{ margin: 0, lineHeight: 1.5 }}>
              Tài khoản nội bộ được quản lý tập trung. Vui lòng liên hệ bộ phận IT / Quản trị viên để đặt lại mật khẩu
              tại mục <strong>Quản trị &amp; Quy trình → Người dùng</strong>.
            </p>
          </div>

          <div
            style={{
              padding: '12px 14px',
              borderRadius: 12,
              backgroundColor: 'var(--bg-primary)',
              border: '1px solid var(--border-light)',
            }}
          >
            <div style={{ fontWeight: 700, color: 'var(--text-primary)', marginBottom: 4 }}>
              2. Đăng nhập bằng SSO Doanh nghiệp
            </div>
            <p style={{ margin: 0, lineHeight: 1.5 }}>
              Nếu công ty đã kích hoạt Google Workspace, Microsoft Entra ID hoặc OIDC riêng, bạn có thể đăng nhập trực
              tiếp bằng nút SSO mà không cần mật khẩu cục bộ.
            </p>
          </div>

          <div
            style={{
              padding: '10px 14px',
              borderRadius: 12,
              backgroundColor: 'var(--info-bg)',
              border: '1px solid var(--info-border)',
              color: 'var(--info-text)',
              fontSize: 12.5,
            }}
          >
            <strong>Môi trường Demo / Kiểm thử:</strong> Mật khẩu mặc định cho các tài khoản mẫu là{' '}
            <code style={{ fontWeight: 700 }}>123456</code>, hoặc sử dụng bảng <strong>Đăng nhập nhanh (RBAC)</strong>{' '}
            bên cạnh.
          </div>
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 18 }}>
          <button type="button" className="btn btn-primary" onClick={onClose}>
            Đã hiểu
          </button>
        </div>
      </div>
    </div>
  );
}

function DesktopServerModal({
  open,
  onClose,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [webUrl, setWebUrl] = useState('http://localhost:4100');
  const [apiUrl, setApiUrl] = useState('http://localhost:5200');
  const [savingCfg, setSavingCfg] = useState(false);

  useEffect(() => {
    if (!open) return;
    getDesktopServerConfig().then((cfg) => {
      setWebUrl(cfg.webUrl);
      setApiUrl(cfg.apiUrl);
    });
  }, [open]);

  if (!open) return null;

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setSavingCfg(true);
    try {
      await setDesktopServerConfig(webUrl.trim(), apiUrl.trim());
      onSaved();
      onClose();
    } finally {
      setSavingCfg(false);
    }
  };

  return (
    <div className="modal-overlay" role="dialog" aria-modal="true" aria-labelledby="desktop-cfg-title" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 440 }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 14 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <DesktopWindowsRoundedIcon sx={{ fontSize: 22, color: 'var(--accent-primary)' }} />
            <h3 id="desktop-cfg-title" style={{ fontSize: 16, fontWeight: 700, margin: 0 }}>
              Cấu hình máy chủ Desktop
            </h3>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng"
            className="btn btn-outline btn-sm"
            style={{ width: 32, padding: 0, borderRadius: '50%' }}
          >
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>
        <form onSubmit={handleSave} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div className="form-group" style={{ marginBottom: 0 }}>
            <label className="form-label" htmlFor="desktop-web-url">
              Web Portal URL
            </label>
            <input
              id="desktop-web-url"
              className="form-input"
              value={webUrl}
              onChange={(e) => setWebUrl(e.target.value)}
              required
            />
          </div>
          <div className="form-group" style={{ marginBottom: 0 }}>
            <label className="form-label" htmlFor="desktop-api-url">
              Backend API URL (.NET 9)
            </label>
            <input
              id="desktop-api-url"
              className="form-input"
              value={apiUrl}
              onChange={(e) => setApiUrl(e.target.value)}
              required
            />
          </div>
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 8 }}>
            <button type="button" className="btn btn-outline" onClick={onClose}>
              Hủy
            </button>
            <button type="submit" className="btn btn-primary" disabled={savingCfg}>
              {savingCfg ? 'Đang lưu...' : 'Lưu cấu hình'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

function LoginPageInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const ssoError = searchParams?.get('sso_error') || '';
  const returnUrl = getSafeReturnUrl(searchParams?.get('returnUrl'));

  const { user, allDemoUsers, login, switchDemoUser, loadingAuth, authServerOnline, refreshAuth } = useAuth();

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [rememberUsername, setRememberUsername] = useState(true);
  const [showPassword, setShowPassword] = useState(false);
  const [capsLockOn, setCapsLockOn] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [switchingUser, setSwitchingUser] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState<{ username?: string; password?: string }>({});
  const [lockoutSeconds, setLockoutSeconds] = useState<number>(0);

  const [ssoProviders, setSsoProviders] = useState<SsoProviderPublic[]>([]);
  const [loadingSso, setLoadingSso] = useState(true);

  const [demoQuery, setDemoQuery] = useState('');
  const [demoRoleFilter, setDemoRoleFilter] = useState('ALL');
  const [retryingConn, setRetryingConn] = useState(false);

  const [showForgotModal, setShowForgotModal] = useState(false);
  const [inDesktop, setInDesktop] = useState(false);
  const [showDesktopCfg, setShowDesktopCfg] = useState(false);
  const [isDark, setIsDark] = useState(false);

  const usernameInputRef = useRef<HTMLInputElement>(null);
  const passwordInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    setInDesktop(isTauriDesktop());
    const savedUsername = localStorage.getItem(REMEMBER_USER_KEY);
    if (savedUsername !== null) {
      setUsername(savedUsername);
      setRememberUsername(Boolean(savedUsername));
    } else {
      setUsername('admin');
      setRememberUsername(true);
    }

    const savedTheme = localStorage.getItem(LOGIN_THEME_KEY);
    if (savedTheme === 'dark') {
      document.body.classList.add('theme-dark');
      setIsDark(true);
    } else {
      setIsDark(document.body.classList.contains('theme-dark'));
    }
  }, []);

  useEffect(() => {
    if (ssoError) setError(ssoError);
  }, [ssoError]);

  useEffect(() => {
    if (!loadingAuth && user) {
      router.replace(returnUrl);
    }
  }, [loadingAuth, user, router, returnUrl]);

  const fetchSsoProviders = useCallback(async () => {
    setLoadingSso(true);
    try {
      const res = await fetch('/api/auth/sso/providers').then((r) => r.json());
      if (res.success && Array.isArray(res.data)) {
        setSsoProviders(res.data);
      } else {
        setSsoProviders([]);
      }
    } catch {
      setSsoProviders([]);
    } finally {
      setLoadingSso(false);
    }
  }, []);

  useEffect(() => {
    fetchSsoProviders();
  }, [fetchSsoProviders]);

  useEffect(() => {
    if (lockoutSeconds <= 0) return;
    const timer = window.setInterval(() => {
      setLockoutSeconds((s) => (s <= 1 ? 0 : s - 1));
    }, 1000);
    return () => window.clearInterval(timer);
  }, [lockoutSeconds]);

  const toggleTheme = () => {
    const next = !isDark;
    setIsDark(next);
    document.body.classList.toggle('theme-dark', next);
    localStorage.setItem(LOGIN_THEME_KEY, next ? 'dark' : 'light');
  };

  const handleRetryConnection = async () => {
    setRetryingConn(true);
    await Promise.all([refreshAuth(), fetchSsoProviders()]);
    setRetryingConn(false);
  };

  const handlePasswordKeyEvent = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (typeof e.getModifierState === 'function') {
      setCapsLockOn(e.getModifierState('CapsLock'));
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const cleanUser = username.trim();
    const nextFieldErrors: { username?: string; password?: string } = {};
    if (!cleanUser) nextFieldErrors.username = 'Vui lòng nhập tên đăng nhập.';
    if (!password) nextFieldErrors.password = 'Vui lòng nhập mật khẩu.';
    if (Object.keys(nextFieldErrors).length > 0) {
      setFieldErrors(nextFieldErrors);
      if (nextFieldErrors.username) usernameInputRef.current?.focus();
      else passwordInputRef.current?.focus();
      return;
    }

    setFieldErrors({});
    setError('');
    setSubmitting(true);

    if (rememberUsername) {
      localStorage.setItem(REMEMBER_USER_KEY, cleanUser);
    } else {
      localStorage.setItem(REMEMBER_USER_KEY, '');
    }

    const res = await login(cleanUser, password, returnUrl);
    setSubmitting(false);

    if (!res.success) {
      const msg = res.error || 'Sai tên đăng nhập hoặc mật khẩu.';
      setError(msg);
      const secs = extractLockoutSeconds(msg);
      if (secs) setLockoutSeconds(secs);
    } else {
      setLockoutSeconds(0);
    }
  };

  const handleDemoClick = async (demoUsername: string) => {
    setError('');
    setFieldErrors({});
    setSwitchingUser(demoUsername);
    const res = await switchDemoUser(demoUsername, returnUrl);
    setSwitchingUser(null);
    if (!res.success && res.error) {
      setError(res.error);
    }
  };

  const handlePrefillDemo = (u: DemoAccount) => {
    setUsername(u.username);
    setPassword('123456');
    setFieldErrors({});
    setError('');
    setLockoutSeconds(0);
    passwordInputRef.current?.focus();
  };

  const handleSsoClick = (provider: SsoProviderPublic) => {
    setError('');
    if (!provider.isEnabled || !provider.isConfigured) {
      setError(
        `Phương thức "${provider.displayName}" chưa được kích hoạt hoặc thiếu Client ID. Quản trị viên cần cấu hình tại Cài đặt → Đăng nhập SSO.`
      );
      return;
    }
    const origin = window.location.origin;
    window.location.href = `/api/auth/sso/start/${encodeURIComponent(provider.code)}?redirectOrigin=${encodeURIComponent(origin)}&returnUrl=${encodeURIComponent(returnUrl)}`;
  };

  const readySsoProviders = useMemo(
    () => ssoProviders.filter((p) => p.isEnabled && p.isConfigured),
    [ssoProviders]
  );

  const availableRoles = useMemo(() => {
    const set = new Set<string>();
    allDemoUsers.forEach((u) => {
      if (u.roleCodes?.length) u.roleCodes.forEach((r) => set.add(r));
      else if (u.roleCode) set.add(u.roleCode);
    });
    return ['ALL', ...Array.from(set)];
  }, [allDemoUsers]);

  const filteredDemoUsers = useMemo(() => {
    const q = demoQuery.trim().toLowerCase();
    return allDemoUsers.filter((u) => {
      const roles = u.roleCodes?.length ? u.roleCodes : [u.roleCode];
      if (demoRoleFilter !== 'ALL' && !roles.includes(demoRoleFilter)) return false;
      if (!q) return true;
      return (
        u.fullName.toLowerCase().includes(q) ||
        u.username.toLowerCase().includes(q) ||
        (u.department || '').toLowerCase().includes(q) ||
        roles.some((r) => r.toLowerCase().includes(q)) ||
        u.allowedApps.some((a) => a.toLowerCase().includes(q))
      );
    });
  }, [allDemoUsers, demoQuery, demoRoleFilter]);

  if (loadingAuth || user) {
    return (
      <div className="login-viewport">
        <div
          className="card"
          style={{
            width: '100%',
            maxWidth: 400,
            textAlign: 'center',
            padding: '36px 28px',
            borderRadius: 20,
          }}
        >
          <div
            style={{
              width: 48,
              height: 48,
              borderRadius: 14,
              backgroundColor: 'var(--accent-primary)',
              color: '#ffffff',
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontWeight: 800,
              fontSize: 18,
              marginBottom: 14,
              boxShadow: '0 4px 12px rgba(26, 115, 232, 0.28)',
            }}
          >
            SA
          </div>
          <div style={{ fontSize: 16, fontWeight: 700, color: 'var(--text-primary)' }}>
            {user ? 'Đang chuyển hướng vào hệ thống...' : 'Đang kiểm tra phiên đăng nhập...'}
          </div>
          <div style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 6 }}>
            SmartApps Enterprise Multi-App Core
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="login-viewport">
      {/* Subtle Top Utility Bar */}
      <div
        style={{
          width: '100%',
          maxWidth: 1040,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 10,
          marginBottom: 12,
          padding: '0 4px',
          flexWrap: 'wrap',
        }}
      >
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
          <span
            className={`badge ${authServerOnline ? 'badge-success' : 'badge-danger'}`}
            style={{ fontSize: 11 }}
          >
            <span
              style={{
                width: 7,
                height: 7,
                borderRadius: '50%',
                backgroundColor: authServerOnline ? '#1e8e3e' : '#d93025',
              }}
            />
            {authServerOnline ? 'Máy chủ xác thực sẵn sàng' : 'Mất kết nối API xác thực'}
          </span>
          {!authServerOnline && (
            <button
              type="button"
              className="btn btn-outline btn-xs"
              onClick={handleRetryConnection}
              disabled={retryingConn}
            >
              <RefreshRoundedIcon sx={{ fontSize: 13 }} />
              {retryingConn ? 'Đang thử...' : 'Thử lại'}
            </button>
          )}
        </div>

        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
          {inDesktop && (
            <button
              type="button"
              className="btn btn-outline btn-xs"
              onClick={() => setShowDesktopCfg(true)}
            >
              <DesktopWindowsRoundedIcon sx={{ fontSize: 14 }} />
              Cấu hình máy chủ
            </button>
          )}
          <button
            type="button"
            className="btn btn-outline btn-xs"
            onClick={toggleTheme}
            aria-label={isDark ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'}
            title={isDark ? 'Giao diện sáng' : 'Giao diện tối'}
          >
            {isDark ? <LightModeRoundedIcon sx={{ fontSize: 14 }} /> : <DarkModeRoundedIcon sx={{ fontSize: 14 }} />}
            {isDark ? 'Sáng' : 'Tối'}
          </button>
        </div>
      </div>

      <div className="login-shell">
        {/* Left Column: Brand + Standard Form + Enterprise SSO */}
        <div className="login-panel-form">
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 13, marginBottom: 24 }}>
              <div
                style={{
                  width: 46,
                  height: 46,
                  borderRadius: 14,
                  background: 'linear-gradient(135deg, #1a73e8 0%, #0d47a1 100%)',
                  color: '#ffffff',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontWeight: 800,
                  fontSize: 18,
                  flexShrink: 0,
                  boxShadow: '0 4px 12px rgba(26, 115, 232, 0.3)',
                }}
              >
                SA
              </div>
              <div>
                <h1 style={{ fontSize: 21, fontWeight: 700, margin: 0, color: 'var(--text-primary)', lineHeight: 1.25 }}>
                  SmartApps Enterprise
                </h1>
                <p style={{ fontSize: 12.5, color: 'var(--text-muted)', margin: '2px 0 0' }}>
                  Cổng xác thực tập trung Hệ thống Đa ứng dụng Doanh nghiệp
                </p>
              </div>
            </div>

            {returnUrl !== '/' && (
              <div
                style={{
                  marginBottom: 16,
                  padding: '9px 12px',
                  borderRadius: 10,
                  backgroundColor: 'var(--info-bg)',
                  border: '1px solid var(--info-border)',
                  color: 'var(--info-text)',
                  fontSize: 12,
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                }}
              >
                <VerifiedUserRoundedIcon sx={{ fontSize: 16, flexShrink: 0 }} />
                <span>
                  Vui lòng đăng nhập để tiếp tục truy cập <code>{returnUrl}</code>
                </span>
              </div>
            )}

            <form onSubmit={handleSubmit} noValidate style={{ display: 'flex', flexDirection: 'column', gap: 15 }}>
              <div>
                <label className="form-label" htmlFor="login-username">
                  Tên đăng nhập
                </label>
                <div className="login-input-wrap">
                  <span className="login-input-icon">
                    <PersonRoundedIcon sx={{ fontSize: 18 }} />
                  </span>
                  <input
                    ref={usernameInputRef}
                    id="login-username"
                    name="username"
                    type="text"
                    autoComplete="username"
                    className="form-input has-leading has-trailing"
                    style={fieldErrors.username ? { borderColor: 'var(--danger)' } : undefined}
                    value={username}
                    onChange={(e) => {
                      setUsername(e.target.value);
                      if (fieldErrors.username) setFieldErrors((p) => ({ ...p, username: undefined }));
                      if (error) setError('');
                    }}
                    placeholder="admin / hr_manager / staff"
                    aria-invalid={Boolean(fieldErrors.username)}
                    aria-describedby={fieldErrors.username ? 'login-username-err' : undefined}
                    disabled={submitting || Boolean(switchingUser)}
                  />
                  {username && (
                    <button
                      type="button"
                      className="login-input-action"
                      aria-label="Xóa tên đăng nhập"
                      title="Xóa"
                      onClick={() => {
                        setUsername('');
                        usernameInputRef.current?.focus();
                      }}
                    >
                      <CloseRoundedIcon sx={{ fontSize: 16 }} />
                    </button>
                  )}
                </div>
                {fieldErrors.username && (
                  <div id="login-username-err" style={{ fontSize: 11.5, color: 'var(--danger-text)', marginTop: 4 }}>
                    {fieldErrors.username}
                  </div>
                )}
              </div>

              <div>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 5 }}>
                  <label className="form-label" htmlFor="login-password" style={{ marginBottom: 0 }}>
                    Mật khẩu
                  </label>
                  <button
                    type="button"
                    onClick={() => setShowForgotModal(true)}
                    style={{
                      background: 'none',
                      border: 'none',
                      padding: 0,
                      color: 'var(--accent-primary)',
                      fontSize: 12,
                      fontWeight: 600,
                      cursor: 'pointer',
                      fontFamily: 'inherit',
                    }}
                  >
                    Quên mật khẩu?
                  </button>
                </div>
                <div className="login-input-wrap">
                  <span className="login-input-icon">
                    <LockRoundedIcon sx={{ fontSize: 18 }} />
                  </span>
                  <input
                    ref={passwordInputRef}
                    id="login-password"
                    name="password"
                    type={showPassword ? 'text' : 'password'}
                    autoComplete="current-password"
                    className="form-input has-leading has-trailing"
                    style={fieldErrors.password ? { borderColor: 'var(--danger)' } : undefined}
                    value={password}
                    onChange={(e) => {
                      setPassword(e.target.value);
                      if (fieldErrors.password) setFieldErrors((p) => ({ ...p, password: undefined }));
                      if (error) setError('');
                    }}
                    onKeyDown={handlePasswordKeyEvent}
                    onKeyUp={handlePasswordKeyEvent}
                    onBlur={() => setCapsLockOn(false)}
                    placeholder="Nhập mật khẩu (demo: 123456)"
                    aria-invalid={Boolean(fieldErrors.password)}
                    aria-describedby={fieldErrors.password ? 'login-password-err' : undefined}
                    disabled={submitting || Boolean(switchingUser)}
                  />
                  <button
                    type="button"
                    className="login-input-action"
                    aria-label={showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}
                    title={showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}
                    onClick={() => setShowPassword((v) => !v)}
                  >
                    {showPassword ? (
                      <VisibilityOffRoundedIcon sx={{ fontSize: 18 }} />
                    ) : (
                      <VisibilityRoundedIcon sx={{ fontSize: 18 }} />
                    )}
                  </button>
                </div>
                {fieldErrors.password && (
                  <div id="login-password-err" style={{ fontSize: 11.5, color: 'var(--danger-text)', marginTop: 4 }}>
                    {fieldErrors.password}
                  </div>
                )}
                {capsLockOn && (
                  <div
                    style={{
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: 5,
                      fontSize: 11.5,
                      color: 'var(--warning-text)',
                      marginTop: 5,
                      fontWeight: 500,
                    }}
                  >
                    <WarningAmberRoundedIcon sx={{ fontSize: 15 }} />
                    Caps Lock đang bật
                  </div>
                )}
              </div>

              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
                <label
                  htmlFor="remember-username"
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: 8,
                    fontSize: 12.5,
                    color: 'var(--text-secondary)',
                    cursor: 'pointer',
                    userSelect: 'none',
                  }}
                >
                  <input
                    id="remember-username"
                    type="checkbox"
                    checked={rememberUsername}
                    onChange={(e) => setRememberUsername(e.target.checked)}
                    style={{ width: 15, height: 15, accentColor: 'var(--accent-primary)', cursor: 'pointer' }}
                  />
                  Ghi nhớ tên đăng nhập
                </label>
              </div>

              {error && (
                <div
                  role="alert"
                  style={{
                    padding: '10px 12px',
                    borderRadius: 10,
                    backgroundColor: 'var(--danger-bg)',
                    border: '1px solid var(--danger-border)',
                    color: 'var(--danger-text)',
                    fontSize: 12.5,
                    fontWeight: 600,
                    display: 'flex',
                    alignItems: 'flex-start',
                    justifyContent: 'space-between',
                    gap: 8,
                  }}
                >
                  <span style={{ display: 'inline-flex', alignItems: 'flex-start', gap: 7, lineHeight: 1.45 }}>
                    <ErrorOutlineRoundedIcon sx={{ fontSize: 18, flexShrink: 0, marginTop: '1px' }} />
                    <span>
                      {error}
                      {lockoutSeconds > 0 && (
                        <span style={{ display: 'block', fontSize: 11.5, fontWeight: 500, marginTop: 2 }}>
                          Có thể thử lại sau <strong>{lockoutSeconds} giây</strong>.
                        </span>
                      )}
                    </span>
                  </span>
                  <button
                    type="button"
                    aria-label="Đóng thông báo lỗi"
                    onClick={() => setError('')}
                    style={{
                      background: 'none',
                      border: 'none',
                      color: 'inherit',
                      cursor: 'pointer',
                      padding: 2,
                      display: 'inline-flex',
                    }}
                  >
                    <CloseRoundedIcon sx={{ fontSize: 15 }} />
                  </button>
                </div>
              )}

              <button
                type="submit"
                disabled={submitting || Boolean(switchingUser) || lockoutSeconds > 0}
                className="btn btn-primary"
                style={{
                  height: 44,
                  width: '100%',
                  justifyContent: 'center',
                  fontSize: 14,
                  fontWeight: 600,
                  borderRadius: 12,
                }}
              >
                <LoginRoundedIcon sx={{ fontSize: 18 }} />
                {lockoutSeconds > 0
                  ? `Tạm khóa (${lockoutSeconds}s)`
                  : submitting
                    ? 'Đang xác thực...'
                    : 'Đăng nhập hệ thống'}
              </button>
            </form>

            {/* Enterprise SSO Section — only visible when at least one SSO provider is enabled & configured */}
            {!loadingSso && readySsoProviders.length > 0 && (
              <div style={{ marginTop: 22 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
                  <div style={{ flex: 1, height: 1, backgroundColor: 'var(--border-light)' }} />
                  <span style={{ fontSize: 11.5, fontWeight: 600, color: 'var(--text-muted)' }}>
                    Hoặc đăng nhập bằng SSO Doanh nghiệp
                  </span>
                  <div style={{ flex: 1, height: 1, backgroundColor: 'var(--border-light)' }} />
                </div>

                <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                  {readySsoProviders.map((p) => (
                    <button
                      key={p.id}
                      type="button"
                      disabled={submitting || Boolean(switchingUser)}
                      onClick={() => handleSsoClick(p)}
                      className="login-sso-btn"
                    >
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10 }}>
                        <SsoProviderIcon provider={p} />
                        <span>{p.displayName}</span>
                      </span>
                      <span className="badge badge-success" style={{ fontSize: 10.5 }}>
                        Sẵn sàng
                      </span>
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>

          <div
            style={{
              paddingTop: 14,
              borderTop: '1px solid var(--border-light)',
              fontSize: 11.5,
              color: 'var(--text-muted)',
              lineHeight: 1.5,
            }}
          >
            Phiên đăng nhập sử dụng <strong>HttpOnly Cookie</strong> bảo mật cho Next.js Portal, C# .NET 9 API &amp; Rust
            Desktop.
          </div>
        </div>

        {/* Right Column: Quick Demo RBAC Switcher */}
        <div className="login-panel-side">
          <div>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <AdminPanelSettingsRoundedIcon sx={{ fontSize: 20, color: 'var(--accent-primary)' }} />
                <h2 style={{ fontSize: 15.5, fontWeight: 700, margin: 0, color: 'var(--text-primary)' }}>
                  Đăng nhập nhanh theo Vai trò
                </h2>
              </div>
              {allDemoUsers.length > 0 && (
                <span className="badge badge-info" style={{ fontSize: 11 }}>
                  {allDemoUsers.length} tài khoản
                </span>
              )}
            </div>
            <p style={{ fontSize: 12, color: 'var(--text-secondary)', margin: '6px 0 0', lineHeight: 1.45 }}>
              Nhấn vào thẻ để vào thẳng phiên làm việc, hoặc nhấn biểu tượng{' '}
              <EditNoteRoundedIcon sx={{ fontSize: 15, verticalAlign: 'text-bottom' }} /> để điền sẵn vào form (mật khẩu{' '}
              <code>123456</code>).
            </p>
          </div>

          {allDemoUsers.length > 0 && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              <div className="login-input-wrap">
                <span className="login-input-icon">
                  <SearchRoundedIcon sx={{ fontSize: 17 }} />
                </span>
                <input
                  type="search"
                  className="form-input has-leading"
                  style={{ height: 36, fontSize: 12.5, borderRadius: 10 }}
                  placeholder="Tìm tên, username, phòng ban, app..."
                  value={demoQuery}
                  onChange={(e) => setDemoQuery(e.target.value)}
                  aria-label="Tìm kiếm tài khoản mẫu"
                />
              </div>

              {availableRoles.length > 2 && (
                <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                  {availableRoles.map((role) => {
                    const active = demoRoleFilter === role;
                    return (
                      <button
                        key={role}
                        type="button"
                        onClick={() => setDemoRoleFilter(role)}
                        style={{
                          height: 25,
                          padding: '0 10px',
                          borderRadius: 999,
                          border: `1px solid ${active ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
                          backgroundColor: active ? 'var(--accent-primary)' : 'var(--bg-surface)',
                          color: active ? '#ffffff' : 'var(--text-secondary)',
                          fontSize: 11,
                          fontWeight: 600,
                          cursor: 'pointer',
                          fontFamily: 'inherit',
                        }}
                      >
                        {role === 'ALL' ? 'Tất cả' : role}
                      </button>
                    );
                  })}
                </div>
              )}
            </div>
          )}

          {loadingAuth ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 9 }}>
              <div className="login-skeleton" />
              <div className="login-skeleton" />
              <div className="login-skeleton" />
            </div>
          ) : allDemoUsers.length === 0 ? (
            <div
              style={{
                padding: '24px 16px',
                borderRadius: 14,
                border: '1px dashed var(--border-subtle)',
                backgroundColor: 'var(--bg-surface)',
                textAlign: 'center',
                color: 'var(--text-muted)',
                fontSize: 12.5,
              }}
            >
              <WifiOffRoundedIcon sx={{ fontSize: 28, color: 'var(--text-muted)', marginBottom: '6px' }} />
              <div style={{ fontWeight: 600, color: 'var(--text-primary)', marginBottom: 4 }}>
                {authServerOnline ? 'Chưa có tài khoản mẫu khả dụng' : 'Không tải được danh sách tài khoản'}
              </div>
              <div style={{ marginBottom: 12 }}>
                {authServerOnline
                  ? 'Vui lòng đăng nhập bằng tên tài khoản và mật khẩu ở khung bên trái.'
                  : 'Kiểm tra kết nối tới máy chủ API (.NET 9) rồi thử tải lại.'}
              </div>
              <button
                type="button"
                className="btn btn-outline btn-sm"
                onClick={handleRetryConnection}
                disabled={retryingConn}
              >
                <RefreshRoundedIcon sx={{ fontSize: 15 }} />
                {retryingConn ? 'Đang tải...' : 'Tải lại danh sách'}
              </button>
            </div>
          ) : filteredDemoUsers.length === 0 ? (
            <div
              style={{
                padding: '20px 14px',
                borderRadius: 14,
                border: '1px dashed var(--border-subtle)',
                backgroundColor: 'var(--bg-surface)',
                textAlign: 'center',
                fontSize: 12.5,
                color: 'var(--text-muted)',
              }}
            >
              Không tìm thấy tài khoản khớp với &ldquo;{demoQuery}&rdquo;.
            </div>
          ) : (
            <div className="login-demo-list">
              {filteredDemoUsers.map((u) => {
                const isBusy = switchingUser === u.username;
                const roles = u.roleCodes?.length ? u.roleCodes : [u.roleCode];
                const appsPreview = u.allowedApps.slice(0, 3);
                const extraApps = Math.max(0, u.allowedApps.length - 3);
                return (
                  <div key={u.username} className="login-demo-card">
                    <button
                      type="button"
                      className="login-demo-main"
                      disabled={submitting || Boolean(switchingUser)}
                      onClick={() => handleDemoClick(u.username)}
                      title={`Đăng nhập nhanh với ${u.fullName} (@${u.username})`}
                    >
                      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                        <div
                          style={{
                            width: 34,
                            height: 34,
                            borderRadius: 10,
                            backgroundColor: getAvatarColor(u.roleCode),
                            color: '#ffffff',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            fontWeight: 700,
                            fontSize: 12,
                            flexShrink: 0,
                          }}
                        >
                          {getInitials(u.fullName)}
                        </div>
                        <div style={{ minWidth: 0, flex: 1 }}>
                          <div
                            style={{
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'space-between',
                              gap: 6,
                              flexWrap: 'wrap',
                            }}
                          >
                            <span style={{ fontWeight: 700, fontSize: 13, color: 'var(--text-primary)' }}>
                              {u.fullName}
                            </span>
                            <span className="badge badge-info" style={{ fontSize: 10.5, padding: '2px 8px' }}>
                              {isBusy ? 'Đang vào...' : roles.join(', ')}
                            </span>
                          </div>
                          <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 2 }}>
                            <code>@{u.username}</code> • {u.department}
                          </div>
                        </div>
                      </div>

                      <div style={{ display: 'flex', alignItems: 'center', gap: 4, flexWrap: 'wrap', marginTop: 8, paddingLeft: 44 }}>
                        {u.allowedApps.includes('*') ? (
                          <span
                            className="badge"
                            style={{
                              fontSize: 10.5,
                              padding: '1px 7px',
                              backgroundColor: 'var(--success-bg)',
                              color: 'var(--success-text)',
                            }}
                          >
                            Toàn bộ App (*)
                          </span>
                        ) : u.allowedApps.length === 0 ? (
                          <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>Chưa cấp App</span>
                        ) : (
                          <>
                            {appsPreview.map((app) => (
                              <span
                                key={app}
                                className="badge"
                                style={{
                                  fontSize: 10.5,
                                  padding: '1px 7px',
                                  backgroundColor: 'var(--bg-secondary)',
                                  color: 'var(--text-secondary)',
                                }}
                              >
                                {app}
                              </span>
                            ))}
                            {extraApps > 0 && (
                              <span style={{ fontSize: 10.5, color: 'var(--text-muted)', fontWeight: 600 }}>
                                +{extraApps}
                              </span>
                            )}
                          </>
                        )}
                      </div>
                    </button>

                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      disabled={submitting || Boolean(switchingUser)}
                      onClick={() => handlePrefillDemo(u)}
                      title={`Điền @${u.username} vào form đăng nhập`}
                      aria-label={`Điền tài khoản ${u.username} vào form`}
                      style={{ flexShrink: 0, borderRadius: 8 }}
                    >
                      <EditNoteRoundedIcon sx={{ fontSize: 15 }} />
                    </button>
                  </div>
                );
              })}
            </div>
          )}

          <div
            style={{
              marginTop: 'auto',
              padding: '10px 12px',
              borderRadius: 12,
              backgroundColor: 'var(--bg-surface)',
              border: '1px solid var(--border-light)',
              fontSize: 11.5,
              color: 'var(--text-secondary)',
              lineHeight: 1.45,
            }}
          >
            <strong>Kiểm thử RBAC trực tiếp:</strong> Phân quyền App, giới hạn dòng{' '}
            <code>OWNER_ONLY</code> và ẩn cột nhạy cảm theo từng vai trò.
          </div>
        </div>
      </div>

      <ForgotPasswordModal open={showForgotModal} onClose={() => setShowForgotModal(false)} />
      <DesktopServerModal
        open={showDesktopCfg}
        onClose={() => setShowDesktopCfg(false)}
        onSaved={handleRetryConnection}
      />
    </div>
  );
}

export default function LoginPage() {
  return (
    <Suspense
      fallback={
        <div className="login-viewport">
          <div className="card" style={{ padding: '28px 36px', borderRadius: 18, textAlign: 'center' }}>
            Đang tải trang đăng nhập...
          </div>
        </div>
      }
    >
      <LoginPageInner />
    </Suspense>
  );
}
