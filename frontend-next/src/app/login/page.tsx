'use client';

import React, { useState, useEffect, useMemo, useRef, Suspense, useCallback } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import PersonRoundedIcon from '@mui/icons-material/PersonRounded';
import LoginRoundedIcon from '@mui/icons-material/LoginRounded';
import KeyRoundedIcon from '@mui/icons-material/KeyRounded';
import VisibilityRoundedIcon from '@mui/icons-material/VisibilityRounded';
import VisibilityOffRoundedIcon from '@mui/icons-material/VisibilityOffRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import ErrorOutlineRoundedIcon from '@mui/icons-material/ErrorOutlineRounded';
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded';
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded';
import HelpOutlineRoundedIcon from '@mui/icons-material/HelpOutlineRounded';
import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded';
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded';
import DesktopWindowsRoundedIcon from '@mui/icons-material/DesktopWindowsRounded';
import VerifiedUserRoundedIcon from '@mui/icons-material/VerifiedUserRounded';
import { useAuth, getSafeReturnUrl } from '@/lib/auth-context';
import { SsoProviderPublic } from '@/lib/types';
import { getDesktopServerConfig, setDesktopServerConfig } from '@/lib/tauri-bridge';
import { useIsTauriDesktop } from '@/lib/ui-hooks';

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

function useNativeModal(open: boolean, onClose: () => void) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const pressedOutsideRef = useRef(false);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  const handleClose = () => {
    const dialog = dialogRef.current;
    if (dialog) dialog.returnValue = '';
    onClose();
  };

  const isOutside = (e: React.PointerEvent<HTMLDialogElement>) => {
    const dialog = dialogRef.current;
    if (!dialog) return false;
    const r = dialog.getBoundingClientRect();
    return e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom;
  };

  const handlePointerDown = (e: React.PointerEvent<HTMLDialogElement>) => {
    pressedOutsideRef.current = isOutside(e);
  };

  const handlePointerUp = (e: React.PointerEvent<HTMLDialogElement>) => {
    if (pressedOutsideRef.current && isOutside(e)) {
      dialogRef.current?.close();
    }
    pressedOutsideRef.current = false;
  };

  return { dialogRef, handleClose, handlePointerDown, handlePointerUp };
}

function ForgotPasswordModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { dialogRef, handleClose, handlePointerDown, handlePointerUp } = useNativeModal(open, onClose);

  return (
    <dialog
      ref={dialogRef}
      className="native-modal"
      aria-labelledby="forgot-pw-title"
      onClose={handleClose}
      onPointerDown={handlePointerDown}
      onPointerUp={handlePointerUp}
      {...({ closedby: 'any' } as Record<string, string>)}
    >
      <div className="native-modal-card">
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 16 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <div
              style={{
                width: 38,
                height: 38,
                borderRadius: 11,
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
              <h2 id="forgot-pw-title" style={{ fontSize: 16, fontWeight: 700, margin: 0 }}>
                Hỗ trợ khôi phục mật khẩu
              </h2>
              <p style={{ fontSize: 12, color: 'var(--text-muted)', margin: 0 }}>
                Quy trình bảo mật tài khoản doanh nghiệp
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={() => dialogRef.current?.close()}
            aria-label="Đóng hộp thoại"
            className="btn btn-outline btn-sm"
            style={{ width: 34, height: 34, padding: 0, borderRadius: '50%' }}
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
            <p style={{ margin: 0, lineHeight: 1.55 }}>
              Tài khoản nội bộ được quản lý tập trung. Vui lòng liên hệ bộ phận IT / Quản trị viên để được cấp lại mật
              khẩu mới.
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
            <p style={{ margin: 0, lineHeight: 1.55 }}>
              Nếu tổ chức đã kích hoạt Google Workspace, Microsoft Entra ID hoặc OIDC, bạn có thể đăng nhập trực tiếp
              bằng nút SSO mà không cần mật khẩu cục bộ.
            </p>
          </div>
        </div>

        <form method="dialog" style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 18 }}>
          <button type="submit" className="btn btn-primary" autoFocus>
            Đã hiểu
          </button>
        </form>
      </div>
    </dialog>
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
  const { dialogRef, handleClose, handlePointerDown, handlePointerUp } = useNativeModal(open, onClose);

  useEffect(() => {
    if (!open) return;
    getDesktopServerConfig().then((cfg) => {
      setWebUrl(cfg.webUrl);
      setApiUrl(cfg.apiUrl);
    });
  }, [open]);

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setSavingCfg(true);
    try {
      await setDesktopServerConfig(webUrl.trim(), apiUrl.trim());
      onSaved();
      dialogRef.current?.close();
    } finally {
      setSavingCfg(false);
    }
  };

  return (
    <dialog
      ref={dialogRef}
      className="native-modal"
      aria-labelledby="desktop-cfg-title"
      onClose={handleClose}
      onPointerDown={handlePointerDown}
      onPointerUp={handlePointerUp}
      {...({ closedby: 'any' } as Record<string, string>)}
    >
      <div className="native-modal-card">
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 16 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <DesktopWindowsRoundedIcon sx={{ fontSize: 22, color: 'var(--accent-primary)' }} />
            <h2 id="desktop-cfg-title" style={{ fontSize: 16, fontWeight: 700, margin: 0 }}>
              Cấu hình máy chủ Desktop
            </h2>
          </div>
          <button
            type="button"
            onClick={() => dialogRef.current?.close()}
            aria-label="Đóng hộp thoại"
            className="btn btn-outline btn-sm"
            style={{ width: 34, height: 34, padding: 0, borderRadius: '50%' }}
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
              name="webUrl"
              type="url"
              className="form-input"
              value={webUrl}
              onChange={(e) => setWebUrl(e.target.value)}
              required
            />
          </div>
          <div className="form-group" style={{ marginBottom: 0 }}>
            <label className="form-label" htmlFor="desktop-api-url">
              Backend API URL
            </label>
            <input
              id="desktop-api-url"
              name="apiUrl"
              type="url"
              className="form-input"
              value={apiUrl}
              onChange={(e) => setApiUrl(e.target.value)}
              required
            />
          </div>
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 8 }}>
            <button type="button" className="btn btn-outline" onClick={() => dialogRef.current?.close()}>
              Hủy
            </button>
            <button type="submit" className="btn btn-primary" disabled={savingCfg}>
              {savingCfg ? 'Đang lưu...' : 'Lưu cấu hình'}
            </button>
          </div>
        </form>
      </div>
    </dialog>
  );
}

function LoginPageInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const ssoError = searchParams?.get('sso_error') || '';
  const returnUrl = getSafeReturnUrl(searchParams?.get('returnUrl'));

  const { user, login, loadingAuth, authServerOnline, refreshAuth } = useAuth();

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [rememberUsername, setRememberUsername] = useState(true);
  const [showPassword, setShowPassword] = useState(false);
  const [capsLockOn, setCapsLockOn] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState(() => ssoError);
  const [dismissedSsoErr, setDismissedSsoErr] = useState('');
  const [fieldErrors, setFieldErrors] = useState<{ username?: string; password?: string }>({});
  const [touched, setTouched] = useState<{ username?: boolean; password?: boolean }>({});
  const [lockoutSeconds, setLockoutSeconds] = useState<number>(0);

  const [ssoProviders, setSsoProviders] = useState<SsoProviderPublic[]>([]);
  const [loadingSso, setLoadingSso] = useState(true);
  const [retryingConn, setRetryingConn] = useState(false);

  const [showForgotModal, setShowForgotModal] = useState(false);
  const inDesktop = useIsTauriDesktop();
  const [showDesktopCfg, setShowDesktopCfg] = useState(false);
  const [isDark, setIsDark] = useState(false);

  const usernameInputRef = useRef<HTMLInputElement>(null);
  const passwordInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let cancelled = false;
    Promise.resolve().then(() => {
      if (cancelled) return;
      const savedUsername = localStorage.getItem(REMEMBER_USER_KEY);
      if (savedUsername) {
        setUsername(savedUsername);
        setRememberUsername(true);
      }

      const savedTheme = localStorage.getItem(LOGIN_THEME_KEY);
      if (savedTheme === 'dark') {
        document.body.classList.add('theme-dark');
        setIsDark(true);
      } else {
        setIsDark(document.body.classList.contains('theme-dark'));
      }
    });
    return () => {
      cancelled = true;
    };
  }, []);

  const displayError = error || (dismissedSsoErr === ssoError ? '' : ssoError);

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
    let cancelled = false;
    Promise.resolve().then(() => {
      if (!cancelled) void fetchSsoProviders();
    });
    return () => {
      cancelled = true;
    };
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

  const handleUsernameBlur = () => {
    if (!touched.username) return;
    if (!username.trim()) {
      setFieldErrors((p) => ({ ...p, username: 'Vui lòng nhập tên đăng nhập.' }));
    }
  };

  const handlePasswordBlur = () => {
    if (!touched.password) return;
    setCapsLockOn(false);
    if (!password) {
      setFieldErrors((p) => ({ ...p, password: 'Vui lòng nhập mật khẩu.' }));
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
      localStorage.removeItem(REMEMBER_USER_KEY);
    }

    const res = await login(cleanUser, password, returnUrl);
    setSubmitting(false);

    if (!res.success) {
      const msg = res.error || 'Tên đăng nhập hoặc mật khẩu không đúng.';
      setError(msg);
      const secs = extractLockoutSeconds(msg);
      if (secs) setLockoutSeconds(secs);
    } else {
      setLockoutSeconds(0);
    }
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
    window.location.assign(`/api/auth/sso/start/${encodeURIComponent(provider.code)}?redirectOrigin=${encodeURIComponent(origin)}&returnUrl=${encodeURIComponent(returnUrl)}`);
  };

  const readySsoProviders = useMemo(
    () => ssoProviders.filter((p) => p.isEnabled && p.isConfigured),
    [ssoProviders]
  );

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
              background: 'linear-gradient(135deg, #1a73e8 0%, #0d47a1 100%)',
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
            SmartApps Enterprise
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="login-viewport">
      <div className="login-topbar">
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
          <span
            className={`badge ${authServerOnline ? 'badge-success' : 'badge-danger'}`}
            style={{ fontSize: 11.5 }}
          >
            <span
              style={{
                width: 7,
                height: 7,
                borderRadius: '50%',
                backgroundColor: authServerOnline ? '#1e8e3e' : '#d93025',
              }}
            />
            {authServerOnline ? 'Máy chủ sẵn sàng' : 'Mất kết nối máy chủ'}
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
              Máy chủ
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

      <main className="login-shell">
        <div className="login-panel-form">
          <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
            <div
              style={{
                width: 48,
                height: 48,
                borderRadius: 14,
                background: 'linear-gradient(135deg, #1a73e8 0%, #0d47a1 100%)',
                color: '#ffffff',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontWeight: 800,
                fontSize: 19,
                flexShrink: 0,
                boxShadow: '0 4px 14px rgba(26, 115, 232, 0.28)',
              }}
            >
              SA
            </div>
            <div>
              <h1
                id="login-form-title"
                style={{ fontSize: 22, fontWeight: 700, margin: 0, color: 'var(--text-primary)', lineHeight: 1.25 }}
              >
                SmartApps Enterprise
              </h1>
              <p style={{ fontSize: 13, color: 'var(--text-muted)', margin: '3px 0 0' }}>
                Đăng nhập để truy cập không gian làm việc
              </p>
            </div>
          </div>

          {returnUrl !== '/' && (
            <div
              style={{
                padding: '10px 12px',
                borderRadius: 12,
                backgroundColor: 'var(--info-bg)',
                border: '1px solid var(--info-border)',
                color: 'var(--info-text)',
                fontSize: 12.5,
                display: 'flex',
                alignItems: 'center',
                gap: 8,
              }}
            >
              <VerifiedUserRoundedIcon sx={{ fontSize: 17, flexShrink: 0 }} />
              <span>
                Vui lòng đăng nhập để tiếp tục truy cập <strong>{returnUrl}</strong>
              </span>
            </div>
          )}

          <form
            aria-labelledby="login-form-title"
            onSubmit={handleSubmit}
            noValidate
            style={{ display: 'flex', flexDirection: 'column', gap: 16 }}
          >
            <div>
              <label className="form-label" htmlFor="login-username" style={{ marginBottom: 6 }}>
                Tên đăng nhập
              </label>
              <div className="login-input-wrap">
                <span className="login-input-icon" aria-hidden="true">
                  <PersonRoundedIcon sx={{ fontSize: 19 }} />
                </span>
                <input
                  ref={usernameInputRef}
                  id="login-username"
                  name="username"
                  type="text"
                  autoComplete="username"
                  spellCheck={false}
                  autoCorrect="off"
                  autoCapitalize="none"
                  enterKeyHint="next"
                  required
                  className="form-input has-leading has-trailing"
                  style={fieldErrors.username ? { borderColor: 'var(--danger)' } : undefined}
                  value={username}
                  onChange={(e) => {
                    setUsername(e.target.value);
                    setTouched((p) => ({ ...p, username: true }));
                    if (fieldErrors.username) setFieldErrors((p) => ({ ...p, username: undefined }));
                    if (error) setError('');
                  }}
                  onBlur={handleUsernameBlur}
                  placeholder="Nhập tên đăng nhập"
                  aria-invalid={Boolean(fieldErrors.username)}
                  aria-describedby={fieldErrors.username ? 'login-username-err' : undefined}
                  disabled={submitting}
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
                <div
                  id="login-username-err"
                  role="status"
                  style={{ fontSize: 12, color: 'var(--danger-text)', marginTop: 5 }}
                >
                  {fieldErrors.username}
                </div>
              )}
            </div>

            <div>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
                <label className="form-label" htmlFor="login-password" style={{ marginBottom: 0 }}>
                  Mật khẩu
                </label>
                <button
                  type="button"
                  onClick={() => setShowForgotModal(true)}
                  style={{
                    background: 'none',
                    border: 'none',
                    padding: '2px 0',
                    color: 'var(--accent-primary)',
                    fontSize: 12.5,
                    fontWeight: 600,
                    cursor: 'pointer',
                    fontFamily: 'inherit',
                  }}
                >
                  Quên mật khẩu?
                </button>
              </div>
              <div className="login-input-wrap">
                <span className="login-input-icon" aria-hidden="true">
                  <LockRoundedIcon sx={{ fontSize: 19 }} />
                </span>
                <input
                  ref={passwordInputRef}
                  id="login-password"
                  name="password"
                  type={showPassword ? 'text' : 'password'}
                  autoComplete="current-password"
                  enterKeyHint="done"
                  required
                  className="form-input has-leading has-trailing"
                  style={fieldErrors.password ? { borderColor: 'var(--danger)' } : undefined}
                  value={password}
                  onChange={(e) => {
                    setPassword(e.target.value);
                    setTouched((p) => ({ ...p, password: true }));
                    if (fieldErrors.password) setFieldErrors((p) => ({ ...p, password: undefined }));
                    if (error) setError('');
                  }}
                  onKeyDown={handlePasswordKeyEvent}
                  onKeyUp={handlePasswordKeyEvent}
                  onBlur={handlePasswordBlur}
                  placeholder="Nhập mật khẩu"
                  aria-invalid={Boolean(fieldErrors.password)}
                  aria-describedby={fieldErrors.password ? 'login-password-err' : undefined}
                  disabled={submitting}
                />
                <button
                  type="button"
                  className="login-input-action"
                  aria-pressed={showPassword}
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
                <div
                  id="login-password-err"
                  role="status"
                  style={{ fontSize: 12, color: 'var(--danger-text)', marginTop: 5 }}
                >
                  {fieldErrors.password}
                </div>
              )}
              {capsLockOn && (
                <div
                  role="status"
                  aria-live="polite"
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: 5,
                    fontSize: 12,
                    color: 'var(--warning-text)',
                    marginTop: 6,
                    fontWeight: 500,
                  }}
                >
                  <WarningAmberRoundedIcon sx={{ fontSize: 15 }} />
                  Caps Lock đang bật
                </div>
              )}
            </div>

            <div style={{ display: 'flex', alignItems: 'center' }}>
              <label
                htmlFor="remember-username"
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 8,
                  fontSize: 13,
                  color: 'var(--text-secondary)',
                  cursor: 'pointer',
                  userSelect: 'none',
                  padding: '4px 0',
                }}
              >
                <input
                  id="remember-username"
                  name="rememberUsername"
                  type="checkbox"
                  checked={rememberUsername}
                  onChange={(e) => setRememberUsername(e.target.checked)}
                  style={{ width: 16, height: 16, accentColor: 'var(--accent-primary)', cursor: 'pointer' }}
                />
                Ghi nhớ tên đăng nhập
              </label>
            </div>

            {displayError && (
              <div
                role="alert"
                style={{
                  padding: '11px 13px',
                  borderRadius: 12,
                  backgroundColor: 'var(--danger-bg)',
                  border: '1px solid var(--danger-border)',
                  color: 'var(--danger-text)',
                  fontSize: 13,
                  fontWeight: 600,
                  display: 'flex',
                  alignItems: 'flex-start',
                  justifyContent: 'space-between',
                  gap: 8,
                }}
              >
                <span style={{ display: 'inline-flex', alignItems: 'flex-start', gap: 8, lineHeight: 1.45 }}>
                  <ErrorOutlineRoundedIcon sx={{ fontSize: 18, flexShrink: 0, marginTop: '1px' }} />
                  <span>
                    {displayError}
                    {lockoutSeconds > 0 && (
                      <span style={{ display: 'block', fontSize: 12, fontWeight: 500, marginTop: 2 }}>
                        Có thể thử lại sau <strong>{lockoutSeconds} giây</strong>.
                      </span>
                    )}
                  </span>
                </span>
                <button
                  type="button"
                  aria-label="Đóng thông báo lỗi"
                  onClick={() => {
                    setError('');
                    setDismissedSsoErr(ssoError);
                  }}
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
              disabled={submitting || lockoutSeconds > 0}
              className="btn btn-primary"
              style={{
                minHeight: 48,
                width: '100%',
                justifyContent: 'center',
                fontSize: 15,
                fontWeight: 600,
                borderRadius: 12,
                marginTop: 2,
              }}
            >
              <LoginRoundedIcon sx={{ fontSize: 19 }} />
              {lockoutSeconds > 0
                ? `Tạm khóa (${lockoutSeconds}s)`
                : submitting
                  ? 'Đang xác thực...'
                  : 'Đăng nhập'}
            </button>
          </form>

          {!loadingSso && readySsoProviders.length > 0 && (
            <div style={{ marginTop: 4 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
                <div style={{ flex: 1, height: 1, backgroundColor: 'var(--border-light)' }} />
                <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-muted)' }}>
                  Hoặc đăng nhập bằng SSO
                </span>
                <div style={{ flex: 1, height: 1, backgroundColor: 'var(--border-light)' }} />
              </div>

              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                {readySsoProviders.map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    disabled={submitting}
                    onClick={() => handleSsoClick(p)}
                    className="login-sso-btn"
                  >
                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10 }}>
                      <SsoProviderIcon provider={p} />
                      <span>{p.displayName}</span>
                    </span>
                    <span className="badge badge-success" style={{ fontSize: 11 }}>
                      SSO
                    </span>
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
      </main>

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
