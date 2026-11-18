'use client';

import React, { useState, useEffect, useCallback, Suspense } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded';
import PersonRoundedIcon from '@mui/icons-material/PersonRounded';
import PaletteRoundedIcon from '@mui/icons-material/PaletteRounded';
import NotificationsActiveRoundedIcon from '@mui/icons-material/NotificationsActiveRounded';
import SecurityRoundedIcon from '@mui/icons-material/SecurityRounded';
import AdminPanelSettingsRoundedIcon from '@mui/icons-material/AdminPanelSettingsRounded';
import VolumeUpRoundedIcon from '@mui/icons-material/VolumeUpRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import SendRoundedIcon from '@mui/icons-material/SendRounded';
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded';
import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded';
import SettingsBrightnessRoundedIcon from '@mui/icons-material/SettingsBrightnessRounded';
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded';
import KeyRoundedIcon from '@mui/icons-material/KeyRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import { PageHeader, NotificationSnackbar, BooleanSwitch, ClusterIcon, APP_ICON_OPTIONS } from '@/app/components/ui';
import NotificationChannelsPanel from './NotificationChannelsPanel';
import SsoConfigPanel from './SsoConfigPanel';
import AiConfigPanel from './AiConfigPanel';
import { useAuth } from '@/lib/auth-context';
import { usePlatform } from '@/lib/app-context';
import { useSettings } from '@/lib/settings-context';
import { SysRole, SystemSettings, UserSettings } from '@/lib/types';
import CloudSyncRoundedIcon from '@mui/icons-material/CloudSyncRounded';
import ExtensionRoundedIcon from '@mui/icons-material/ExtensionRounded';
import { BackupManagerPanel, PluginsMarketplacePanel } from '@/app/studio/StudioPlatformPanels';

const ACCENT_SWATCHES = [
  { hex: '#1a73e8', name: 'Google Blue' },
  { hex: '#00897b', name: 'Teal' },
  { hex: '#7b1fa2', name: 'Purple' },
  { hex: '#1e8e3e', name: 'Emerald' },
  { hex: '#ea8600', name: 'Amber' },
  { hex: '#d93025', name: 'Crimson' },
];

type SettingsTab = 'profile' | 'appearance' | 'notifications' | 'channels' | 'sso' | 'ai' | 'plugins' | 'security' | 'system' | 'backup';

function SettingsPageInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const initialTab = searchParams.get('tab') as SettingsTab | null;

  const { user, hasPermission, refreshAuth } = useAuth();
  const { apps } = usePlatform();
  const {
    userSettings,
    systemSettings,
    saveUserSettings,
    saveSystemSettings,
    playNotificationSound,
  } = useSettings();

  const isAdmin =
    hasPermission('studio.manage') ||
    Boolean(user?.isAdmin) ||
    user?.roleCode === 'ADMIN' ||
    Boolean(user?.roleCodes?.includes('ADMIN'));
  const canManageSso = isAdmin || hasPermission('user.manage') || hasPermission('role.manage');
  const [tabState, setTabState] = useState<{ base: SettingsTab | null | undefined; val: SettingsTab } | null>(null);
  const tab: SettingsTab = (tabState && tabState.base === initialTab ? tabState.val : initialTab) || 'profile';
  const [roles, setRoles] = useState<SysRole[]>([]);

  useEffect(() => {
    if (!canManageSso) return;
    fetch('/api/roles', { credentials: 'include' })
      .then((r) => r.json())
      .then((res) => { if (res.success && Array.isArray(res.data)) setRoles(res.data); })
      .catch(() => {});
  }, [canManageSso]);

  // Profile form (keyed to current user snapshot so context refresh updates naturally)
  const [profileDraft, setProfileDraft] = useState<{
    baseKey: string;
    fullName?: string;
    email?: string;
    department?: string;
  } | null>(null);
  const currentUserKey = `${user?.id || ''}:${user?.fullName || ''}:${user?.email || ''}:${user?.department || ''}`;
  const activeProfileDraft = profileDraft?.baseKey === currentUserKey ? profileDraft : null;
  const fullName = activeProfileDraft?.fullName ?? user?.fullName ?? '';
  const email = activeProfileDraft?.email ?? user?.email ?? '';
  const department = activeProfileDraft?.department ?? user?.department ?? 'Toàn công ty';
  const setFullName = (val: string) => setProfileDraft({ ...activeProfileDraft, baseKey: currentUserKey, fullName: val });
  const setEmail = (val: string) => setProfileDraft({ ...activeProfileDraft, baseKey: currentUserKey, email: val });
  const setDepartment = (val: string) => setProfileDraft({ ...activeProfileDraft, baseKey: currentUserKey, department: val });

  // Preferences draft (reset automatically whenever context settings object refreshes)
  const [prefsDraft, setPrefsDraft] = useState<{ base: UserSettings; val: UserSettings } | null>(null);
  const prefs = prefsDraft && prefsDraft.base === userSettings ? prefsDraft.val : userSettings;
  const setPrefs: React.Dispatch<React.SetStateAction<UserSettings>> = useCallback(
    (updater) => {
      setPrefsDraft((prev) => {
        const cur = prev && prev.base === userSettings ? prev.val : userSettings;
        const next = typeof updater === 'function' ? updater(cur) : updater;
        return { base: userSettings, val: next };
      });
    },
    [userSettings]
  );

  const [sysCfgDraft, setSysCfgDraft] = useState<{ base: SystemSettings; val: SystemSettings } | null>(null);
  const sysCfg = sysCfgDraft && sysCfgDraft.base === systemSettings ? sysCfgDraft.val : systemSettings;
  const setSysCfg: React.Dispatch<React.SetStateAction<SystemSettings>> = useCallback(
    (updater) => {
      setSysCfgDraft((prev) => {
        const cur = prev && prev.base === systemSettings ? prev.val : systemSettings;
        const next = typeof updater === 'function' ? updater(cur) : updater;
        return { base: systemSettings, val: next };
      });
    },
    [systemSettings]
  );

  // Password form
  const [curPw, setCurPw] = useState('');
  const [newPw, setNewPw] = useState('');
  const [confirmPw, setConfirmPw] = useState('');

  const [saving, setSaving] = useState(false);
  const [snack, setSnack] = useState<{ open: boolean; message: string; severity: 'success' | 'error' | 'info' }>({
    open: false,
    message: '',
    severity: 'success',
  });

  const switchTab = (next: SettingsTab) => {
    setTabState({ base: initialTab, val: next });
    router.replace(`/settings?tab=${next}`, { scroll: false });
  };

  const handleSaveProfile = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      const res = await fetch('/api/settings/profile', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ fullName, email, department }),
      }).then((r) => r.json());
      if (res.success) {
        await refreshAuth();
        setSnack({ open: true, message: 'Đã cập nhật hồ sơ cá nhân!', severity: 'success' });
      } else {
        setSnack({ open: true, message: res.message || 'Lỗi cập nhật hồ sơ', severity: 'error' });
      }
    } finally {
      setSaving(false);
    }
  };

  const handleSaveUserPrefs = async (next: UserSettings = prefs) => {
    setSaving(true);
    try {
      const ok = await saveUserSettings(next);
      setSnack({
        open: true,
        message: ok ? 'Đã lưu cài đặt cá nhân!' : 'Không thể lưu cài đặt',
        severity: ok ? 'success' : 'error',
      });
    } finally {
      setSaving(false);
    }
  };

  const handleSaveSystem = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      const ok = await saveSystemSettings(sysCfg);
      setSnack({
        open: true,
        message: ok ? 'Đã lưu cấu hình toàn hệ thống!' : 'Chỉ Admin mới có quyền lưu cấu hình hệ thống',
        severity: ok ? 'success' : 'error',
      });
    } finally {
      setSaving(false);
    }
  };

  const handleChangePassword = async (e: React.FormEvent) => {
    e.preventDefault();
    if (newPw !== confirmPw) {
      setSnack({ open: true, message: 'Mật khẩu xác nhận không khớp!', severity: 'error' });
      return;
    }
    setSaving(true);
    try {
      const res = await fetch('/api/auth/change-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ currentPassword: curPw, newPassword: newPw }),
      }).then((r) => r.json());
      if (res.success) {
        setCurPw('');
        setNewPw('');
        setConfirmPw('');
        setSnack({ open: true, message: 'Đã đổi mật khẩu thành công!', severity: 'success' });
      } else {
        setSnack({ open: true, message: res.message || 'Đổi mật khẩu thất bại', severity: 'error' });
      }
    } finally {
      setSaving(false);
    }
  };

  const requestBrowserNotify = async () => {
    if (typeof window === 'undefined' || !('Notification' in window)) {
      setSnack({ open: true, message: 'Trình duyệt không hỗ trợ thông báo Desktop.', severity: 'error' });
      return;
    }
    const perm = await Notification.requestPermission();
    if (perm === 'granted') {
      const next = { ...prefs, notifyDesktop: true };
      setPrefs(next);
      await saveUserSettings(next);
      new Notification('SmartApps Enterprise', { body: 'Đã bật thông báo Desktop thành công!' });
      setSnack({ open: true, message: 'Đã cấp quyền thông báo Desktop!', severity: 'success' });
    } else {
      setSnack({ open: true, message: 'Bạn đã từ chối quyền thông báo trên trình duyệt.', severity: 'error' });
    }
  };

  const userInitials = (user?.fullName || user?.username || 'U')
    .split(' ')
    .filter(Boolean)
    .slice(-2)
    .map((s) => s[0]?.toUpperCase())
    .join('');

  const roleList = user?.roleCodes?.length ? user.roleCodes : user?.roleCode ? [user.roleCode] : [];

  return (
    <div>
      <PageHeader
        title="Cài đặt Tài khoản & Hệ thống"
        subtitle="Tùy chỉnh hồ sơ, giao diện Sáng/Tối, thông báo Gmail/Zalo/Telegram, Đăng nhập SSO, API AI & cấu hình hệ thống"
        icon={<SettingsRoundedIcon sx={{ fontSize: 24 }} />}
        breadcrumbs={[{ label: 'Cổng Ứng dụng', href: '/' }, { label: 'Cài đặt' }]}
      />

      <div className="page-content">
        <div className="settings-split-grid">
          {/* Grouped Vertical Settings Nav */}
          <div className="card settings-nav-card" style={{ padding: 12, display: 'flex', flexDirection: 'column', gap: 4 }}>
            <div className="nav-section-label" style={{ paddingTop: 4 }}>Tài khoản cá nhân</div>
            <button
              type="button"
              className={`nav-link ${tab === 'profile' ? 'active' : ''}`}
              style={{ border: 'none', background: tab === 'profile' ? undefined : 'transparent', textAlign: 'left', width: '100%' }}
              onClick={() => switchTab('profile')}
            >
              <PersonRoundedIcon sx={{ fontSize: 19 }} />
              <span>Hồ sơ cá nhân</span>
            </button>
            <button
              type="button"
              className={`nav-link ${tab === 'appearance' ? 'active' : ''}`}
              style={{ border: 'none', background: tab === 'appearance' ? undefined : 'transparent', textAlign: 'left', width: '100%' }}
              onClick={() => switchTab('appearance')}
            >
              <PaletteRoundedIcon sx={{ fontSize: 19 }} />
              <span>Giao diện &amp; Chat</span>
            </button>
            <button
              type="button"
              className={`nav-link ${tab === 'security' ? 'active' : ''}`}
              style={{ border: 'none', background: tab === 'security' ? undefined : 'transparent', textAlign: 'left', width: '100%' }}
              onClick={() => switchTab('security')}
            >
              <SecurityRoundedIcon sx={{ fontSize: 19 }} />
              <span>Bảo mật &amp; Mật khẩu</span>
            </button>

            <div className="nav-section-label">Thông báo &amp; Kết nối</div>
            <button
              type="button"
              className={`nav-link ${tab === 'notifications' ? 'active' : ''}`}
              style={{ border: 'none', background: tab === 'notifications' ? undefined : 'transparent', textAlign: 'left', width: '100%' }}
              onClick={() => switchTab('notifications')}
            >
              <NotificationsActiveRoundedIcon sx={{ fontSize: 19 }} />
              <span>Thông báo &amp; Âm thanh</span>
            </button>
            <button
              type="button"
              className={`nav-link ${tab === 'channels' ? 'active' : ''}`}
              style={{ border: 'none', background: tab === 'channels' ? undefined : 'transparent', textAlign: 'left', width: '100%' }}
              onClick={() => switchTab('channels')}
            >
              <SendRoundedIcon sx={{ fontSize: 19 }} />
              <span>Gmail, Zalo &amp; Telegram</span>
            </button>

            {(isAdmin || canManageSso) && (
              <>
                <div className="nav-section-label">Quản trị</div>
                {canManageSso && (
                  <button
                    type="button"
                    className={`nav-link ${tab === 'sso' ? 'active' : ''}`}
                    style={{ border: 'none', background: tab === 'sso' ? undefined : 'transparent', textAlign: 'left', width: '100%' }}
                    onClick={() => switchTab('sso')}
                  >
                    <KeyRoundedIcon sx={{ fontSize: 19 }} />
                    <span>Đăng nhập SSO</span>
                  </button>
                )}
                {isAdmin && (
                  <>
                    <button
                      type="button"
                      className={`nav-link ${tab === 'ai' ? 'active' : ''}`}
                      style={{ border: 'none', background: tab === 'ai' ? undefined : 'transparent', textAlign: 'left', width: '100%' }}
                      onClick={() => switchTab('ai')}
                    >
                      <AutoAwesomeRoundedIcon sx={{ fontSize: 19 }} />
                      <span>Cấu hình API AI</span>
                    </button>
                    <button
                      type="button"
                      className={`nav-link ${tab === 'system' ? 'active' : ''}`}
                      style={{ border: 'none', background: tab === 'system' ? undefined : 'transparent', textAlign: 'left', width: '100%' }}
                      onClick={() => switchTab('system')}
                    >
                      <AdminPanelSettingsRoundedIcon sx={{ fontSize: 19 }} />
                      <span>Cấu hình Hệ thống</span>
                    </button>
                    <button
                      type="button"
                      className={`nav-link ${tab === 'plugins' ? 'active' : ''}`}
                      style={{ border: 'none', background: tab === 'plugins' ? undefined : 'transparent', textAlign: 'left', width: '100%' }}
                      onClick={() => switchTab('plugins')}
                    >
                      <ExtensionRoundedIcon sx={{ fontSize: 19 }} />
                      <span>Tích hợp &amp; Plugin</span>
                    </button>
                    <button
                      type="button"
                      className={`nav-link ${tab === 'backup' ? 'active' : ''}`}
                      style={{ border: 'none', background: tab === 'backup' ? undefined : 'transparent', textAlign: 'left', width: '100%' }}
                      onClick={() => switchTab('backup')}
                    >
                      <CloudSyncRoundedIcon sx={{ fontSize: 19 }} />
                      <span>Sao lưu &amp; Đám mây</span>
                    </button>
                  </>
                )}
              </>
            )}
          </div>

          {/* Standardized Content Card */}
          <div className="card" style={{ padding: 24, minWidth: 0 }}>
            {/* TAB 1: PROFILE */}
            {tab === 'profile' && (
              <form onSubmit={handleSaveProfile} style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
                <div style={{ borderBottom: '1px solid var(--border-light)', paddingBottom: 14 }}>
                  <h2 style={{ fontSize: 17, fontWeight: 700, margin: 0 }}>Hồ sơ Cá nhân &amp; Phân quyền</h2>
                  <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 4 }}>
                    Tài khoản dùng chung trên toàn hệ sinh thái ứng dụng doanh nghiệp.
                  </p>
                </div>

                {/* User Identity Summary Banner */}
                <div
                  style={{
                    padding: 16,
                    borderRadius: 14,
                    backgroundColor: 'var(--bg-primary)',
                    border: '1px solid var(--border-light)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    flexWrap: 'wrap',
                    gap: 14,
                  }}
                >
                  <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
                    <div
                      style={{
                        width: 52,
                        height: 52,
                        borderRadius: '50%',
                        backgroundColor: 'var(--accent-primary)',
                        color: '#ffffff',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        fontWeight: 800,
                        fontSize: 18,
                        flexShrink: 0,
                      }}
                    >
                      {userInitials}
                    </div>
                    <div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                        <strong style={{ fontSize: 15.5 }}>{user?.fullName || user?.username}</strong>
                        <code style={{ fontSize: 12 }}>@{user?.username}</code>
                        {user?.employeeCode && (
                          <span className="badge" style={{ backgroundColor: '#e0f2f1', color: '#00695c' }}>
                            Mã NV: {user.employeeCode}
                          </span>
                        )}
                        {user?.ssoProvider && (
                          <span className="badge badge-info">
                            <KeyRoundedIcon sx={{ fontSize: 12 }} /> SSO: {user.ssoProvider}
                          </span>
                        )}
                      </div>
                      <div style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 3 }}>
                        {user?.department || 'Toàn công ty'} • {user?.email || 'Chưa có email'}
                      </div>
                    </div>
                  </div>

                  <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                    {roleList.map((rc) => (
                      <span key={rc} className="badge badge-info" style={{ fontWeight: 700 }}>
                        {rc}
                      </span>
                    ))}
                  </div>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(230px, 1fr))', gap: 14 }}>
                  <div>
                    <label htmlFor="profile-fullname" className="form-label">Họ và tên hiển thị *</label>
                    <input
                      id="profile-fullname"
                      name="fullName"
                      className="form-input"
                      value={fullName}
                      onChange={(e) => setFullName(e.target.value)}
                      required
                    />
                  </div>
                  <div>
                    <label htmlFor="profile-email" className="form-label">Email công việc (nhận thông báo Gmail)</label>
                    <input
                      id="profile-email"
                      name="email"
                      type="email"
                      className="form-input"
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                    />
                  </div>
                  <div>
                    <label htmlFor="profile-dept" className="form-label">Phòng ban / Đơn vị</label>
                    <input
                      id="profile-dept"
                      name="department"
                      className="form-input"
                      value={department}
                      onChange={(e) => setDepartment(e.target.value)}
                    />
                  </div>
                </div>

                {/* Readable Access Summary */}
                <div style={{ padding: 14, borderRadius: 12, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-light)', display: 'flex', flexDirection: 'column', gap: 10 }}>
                  <div>
                    <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 6 }}>
                      Ứng dụng được cấp quyền truy cập:
                    </div>
                    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                      {(user?.allowedApps || []).map((a) => (
                        <span key={a} className="badge badge-info">
                          {a === '*' ? 'Tất cả Ứng dụng (*)' : a}
                        </span>
                      ))}
                      {(!user?.allowedApps || user.allowedApps.length === 0) && (
                        <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>Chưa cấp App</span>
                      )}
                    </div>
                  </div>
                  <div>
                    <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 6 }}>
                      Quyền thao tác ({user?.permissions?.length || 0}):
                    </div>
                    <div style={{ display: 'flex', gap: 5, flexWrap: 'wrap' }}>
                      {(user?.permissions || []).map((p) => (
                        <code key={p} style={{ fontSize: 11, backgroundColor: 'var(--bg-secondary)', padding: '2px 7px', borderRadius: 6 }}>
                          {p}
                        </code>
                      ))}
                    </div>
                  </div>
                </div>

                <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                  <button type="submit" disabled={saving} className="btn btn-primary btn-sm">
                    <SaveRoundedIcon sx={{ fontSize: 16 }} />
                    {saving ? 'Đang lưu...' : 'Lưu thông tin hồ sơ'}
                  </button>
                </div>
              </form>
            )}

            {/* TAB 2: APPEARANCE */}
            {tab === 'appearance' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
                <div style={{ borderBottom: '1px solid var(--border-light)', paddingBottom: 14 }}>
                  <h2 style={{ fontSize: 17, fontWeight: 700, margin: 0 }}>Giao diện &amp; Trải nghiệm Chat</h2>
                  <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 4 }}>
                    Cá nhân hóa chế độ Sáng/Tối, màu nhấn chủ đạo, mật độ hiển thị và thao tác Chat nội bộ.
                  </p>
                </div>

                <div>
                  <div className="form-label">Ngôn ngữ giao diện (Language)</div>
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 10, marginBottom: 16 }}>
                    {(
                      [
                        ['vi', 'Tiếng Việt (VI)'],
                        ['en', 'English (EN)'],
                      ] as const
                    ).map(([langCode, label]) => (
                      <button
                        key={langCode}
                        type="button"
                        onClick={() => setPrefs((p) => ({ ...p, language: langCode }))}
                        style={{
                          padding: '12px 14px',
                          borderRadius: 12,
                          border: `2px solid ${(prefs.language || 'vi') === langCode ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
                          backgroundColor: (prefs.language || 'vi') === langCode ? 'var(--accent-primary-container)' : 'var(--bg-primary)',
                          color: 'var(--text-primary)',
                          fontWeight: 700,
                          fontSize: 13,
                          cursor: 'pointer',
                        }}
                      >
                        {label}
                      </button>
                    ))}
                  </div>
                </div>

                <div>
                  <div className="form-label">Chế độ hiển thị (Theme Mode)</div>
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 10 }}>
                    {(
                      [
                        ['light', 'Sáng (Light)', <LightModeRoundedIcon key="l" sx={{ fontSize: 20, color: '#ea8600' }} />],
                        ['dark', 'Tối (Dark)', <DarkModeRoundedIcon key="d" sx={{ fontSize: 20, color: '#6366f1' }} />],
                        ['auto', 'Theo hệ thống', <SettingsBrightnessRoundedIcon key="a" sx={{ fontSize: 20, color: '#00897b' }} />],
                      ] as const
                    ).map(([mode, label, iconEl]) => (
                      <button
                        key={mode}
                        type="button"
                        onClick={() => setPrefs((p) => ({ ...p, themeMode: mode }))}
                        style={{
                          padding: '14px 16px',
                          borderRadius: 12,
                          border: `2px solid ${prefs.themeMode === mode ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
                          backgroundColor: prefs.themeMode === mode ? 'var(--accent-primary-container)' : 'var(--bg-primary)',
                          color: 'var(--text-primary)',
                          fontWeight: 700,
                          fontSize: 13,
                          cursor: 'pointer',
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          gap: 8,
                        }}
                      >
                        {iconEl}
                        <span>{label}</span>
                      </button>
                    ))}
                  </div>
                </div>

                <div>
                  <div className="form-label">Màu chủ đạo (Accent Color)</div>
                  <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
                    {ACCENT_SWATCHES.map((s) => (
                      <button
                        key={s.hex}
                        type="button"
                        onClick={() => setPrefs((p) => ({ ...p, accentColor: s.hex }))}
                        title={s.name}
                        style={{
                          display: 'inline-flex',
                          alignItems: 'center',
                          gap: 8,
                          padding: '6px 12px 6px 6px',
                          borderRadius: 999,
                          backgroundColor: prefs.accentColor === s.hex ? 'var(--accent-primary-container)' : 'var(--bg-primary)',
                          border: `1.5px solid ${prefs.accentColor === s.hex ? s.hex : 'var(--border-subtle)'}`,
                          cursor: 'pointer',
                          color: 'var(--text-primary)',
                          fontSize: 12,
                          fontWeight: 600,
                        }}
                      >
                        <span
                          style={{
                            width: 22,
                            height: 22,
                            borderRadius: '50%',
                            backgroundColor: s.hex,
                            display: 'inline-block',
                          }}
                        />
                        {s.name}
                      </button>
                    ))}
                  </div>
                </div>

                <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '12px 14px', borderRadius: 12, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-light)', gap: 12 }}>
                    <div>
                      <div style={{ fontSize: 13.5, fontWeight: 700 }}>Chế độ hiển thị gọn (Compact Density)</div>
                      <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Thu gọn khoảng cách bảng dữ liệu &amp; thẻ để xem nhiều thông tin hơn</div>
                    </div>
                    <BooleanSwitch
                      checked={prefs.compactMode}
                      onChange={(v) => setPrefs((p) => ({ ...p, compactMode: v }))}
                      size="sm"
                      trueLabel="Bật"
                      falseLabel="Tắt"
                    />
                  </div>

                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '12px 14px', borderRadius: 12, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-light)', gap: 12 }}>
                    <div>
                      <div style={{ fontSize: 13.5, fontWeight: 700 }}>Nhấn Enter để gửi tin nhắn Chat</div>
                      <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Nếu tắt, dùng Ctrl+Enter để gửi và Enter để xuống dòng</div>
                    </div>
                    <BooleanSwitch
                      checked={prefs.chatEnterToSend}
                      onChange={(v) => setPrefs((p) => ({ ...p, chatEnterToSend: v }))}
                      size="sm"
                      trueLabel="Bật"
                      falseLabel="Tắt"
                    />
                  </div>
                </div>

                <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                  <button type="button" disabled={saving} className="btn btn-primary btn-sm" onClick={() => handleSaveUserPrefs()}>
                    <SaveRoundedIcon sx={{ fontSize: 16 }} />
                    Áp dụng &amp; Lưu giao diện
                  </button>
                </div>
              </div>
            )}

            {/* TAB 3: IN-APP NOTIFICATIONS & SOUND */}
            {tab === 'notifications' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                <div style={{ borderBottom: '1px solid var(--border-light)', paddingBottom: 14, display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
                  <div>
                    <h2 style={{ fontSize: 17, fontWeight: 700, margin: 0 }}>Thông báo Trong Ứng dụng &amp; Âm thanh</h2>
                    <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 4 }}>
                      Kiểm soát loại sự kiện nhận cảnh báo thời gian thực và thông báo nổi trên trình duyệt.
                    </p>
                  </div>
                  <button
                    type="button"
                    className="btn btn-outline btn-sm"
                    onClick={() => {
                      playNotificationSound();
                      setSnack({ open: true, message: 'Đã phát thử âm thanh thông báo!', severity: 'info' });
                    }}
                  >
                    <VolumeUpRoundedIcon sx={{ fontSize: 16 }} />
                    Nghe thử âm thanh
                  </button>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(290px, 1fr))', gap: 10 }}>
                  {[
                    { key: 'notifyInApp', title: 'Hiển thị thông báo trong ứng dụng', desc: 'Nhận thông báo thời gian thực trên thanh công cụ' },
                    { key: 'notifySound', title: 'Âm thanh khi có tin mới', desc: 'Phát âm báo nhẹ nhàng khi có sự kiện gửi tới bạn' },
                    { key: 'notifyChat', title: 'Thông báo tin nhắn Chat mới', desc: 'Cập nhật & âm báo khi đồng nghiệp nhắn tin' },
                    { key: 'notifyTaskAssigned', title: 'Thông báo khi được giao việc mới', desc: 'Nhắc ngay khi có người giao việc trong Chat / TASKS' },
                    { key: 'notifyApproval', title: 'Thông báo phê duyệt hồ sơ', desc: 'Nhận tin khi có đơn mới cần duyệt hoặc đã xử lý' },
                    { key: 'notifyMentions', title: 'Thông báo khi được nhắc tên (@)', desc: 'Cảnh báo khi ai đó tag @username của bạn' },
                  ].map((item) => (
                    <div
                      key={item.key}
                      style={{
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        padding: '12px 14px',
                        borderRadius: 12,
                        backgroundColor: 'var(--bg-primary)',
                        border: '1px solid var(--border-light)',
                        gap: 12,
                      }}
                    >
                      <div>
                        <div style={{ fontSize: 13.5, fontWeight: 700 }}>{item.title}</div>
                        <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>{item.desc}</div>
                      </div>
                      <BooleanSwitch
                        checked={Boolean(prefs[item.key as keyof UserSettings])}
                        onChange={(v) => setPrefs((p) => ({ ...p, [item.key]: v }))}
                        size="sm"
                        trueLabel="Bật"
                        falseLabel="Tắt"
                      />
                    </div>
                  ))}
                </div>

                <div
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    flexWrap: 'wrap',
                    padding: '12px 14px',
                    borderRadius: 12,
                    backgroundColor: 'var(--bg-primary)',
                    border: '1px solid var(--border-light)',
                    gap: 12,
                  }}
                >
                  <div>
                    <div style={{ fontSize: 13.5, fontWeight: 700 }}>Thông báo nổi Desktop (Browser Notification)</div>
                    <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Hiện thông báo góc màn hình ngay cả khi đang mở tab khác</div>
                  </div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <button type="button" className="btn btn-outline btn-xs" onClick={requestBrowserNotify}>
                      Cấp quyền trình duyệt
                    </button>
                    <BooleanSwitch
                      checked={prefs.notifyDesktop}
                      onChange={(v) => setPrefs((p) => ({ ...p, notifyDesktop: v }))}
                      size="sm"
                      trueLabel="Bật"
                      falseLabel="Tắt"
                    />
                  </div>
                </div>

                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', flexWrap: 'wrap', gap: 12 }}>
                  <div>
                    <label htmlFor="pref-digest" className="form-label">Tần suất tổng hợp thông báo (Digest)</label>
                    <select
                      id="pref-digest"
                      name="emailDigest"
                      className="form-select"
                      style={{ minWidth: 260 }}
                      value={prefs.emailDigest}
                      onChange={(e) => setPrefs((p) => ({ ...p, emailDigest: e.target.value }))}
                    >
                      <option value="INSTANT">Tức thì theo thời gian thực</option>
                      <option value="DAILY">Tổng hợp cuối ngày</option>
                      <option value="OFF">Tắt tổng hợp</option>
                    </select>
                  </div>

                  <button type="button" disabled={saving} className="btn btn-primary btn-sm" onClick={() => handleSaveUserPrefs()}>
                    <SaveRoundedIcon sx={{ fontSize: 16 }} />
                    Lưu cài đặt thông báo
                  </button>
                </div>

                {/* Friendly Shortcut Card to External Channels */}
                <div
                  style={{
                    padding: '12px 16px',
                    borderRadius: 12,
                    backgroundColor: 'var(--accent-primary-container)',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    flexWrap: 'wrap',
                    gap: 10,
                  }}
                >
                  <div style={{ fontSize: 13, color: 'var(--text-primary)' }}>
                    <strong>Muốn nhận thông báo qua Gmail, Zalo hoặc Telegram?</strong>
                    <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                      Thiết lập nhận tin cá nhân hoặc kết nối nhóm Zalo, Telegram, Gmail SMTP ở tab bên cạnh.
                    </div>
                  </div>
                  <button type="button" className="btn btn-primary btn-sm" onClick={() => switchTab('channels')}>
                    Cấu hình Gmail, Zalo &amp; Telegram
                    <ArrowForwardRoundedIcon sx={{ fontSize: 15 }} />
                  </button>
                </div>
              </div>
            )}

            {/* TAB 4: EXTERNAL CHANNELS */}
            {tab === 'channels' && (
              <NotificationChannelsPanel
                user={user}
                isAdmin={isAdmin}
                prefs={prefs}
                setPrefs={setPrefs}
                onSavePrefs={handleSaveUserPrefs}
                savingPrefs={saving}
                onToast={(message, severity = 'success') => setSnack({ open: true, message, severity })}
              />
            )}

            {/* TAB 5: SECURITY */}
            {tab === 'security' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
                <div style={{ borderBottom: '1px solid var(--border-light)', paddingBottom: 14 }}>
                  <h2 style={{ fontSize: 17, fontWeight: 700, margin: 0 }}>Đổi Mật khẩu &amp; Bảo mật Tài khoản</h2>
                  <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 4 }}>
                    Cập nhật mật khẩu định kỳ hoặc kiểm tra phương thức đăng nhập SSO được liên kết.
                  </p>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(290px, 1fr))', gap: 20, alignItems: 'start' }}>
                  <form onSubmit={handleChangePassword} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
                    <div>
                      <label htmlFor="sec-cur-pw" className="form-label">Mật khẩu hiện tại</label>
                      <input
                        id="sec-cur-pw"
                        name="currentPassword"
                        type="password"
                        autoComplete="current-password"
                        className="form-input"
                        value={curPw}
                        onChange={(e) => setCurPw(e.target.value)}
                        required
                      />
                    </div>
                    <div>
                      <label htmlFor="sec-new-pw" className="form-label">Mật khẩu mới (tối thiểu 6 ký tự)</label>
                      <input
                        id="sec-new-pw"
                        name="newPassword"
                        type="password"
                        autoComplete="new-password"
                        className="form-input"
                        value={newPw}
                        onChange={(e) => setNewPw(e.target.value)}
                        minLength={6}
                        required
                      />
                    </div>
                    <div>
                      <label htmlFor="sec-confirm-pw" className="form-label">Nhập lại mật khẩu mới</label>
                      <input
                        id="sec-confirm-pw"
                        name="confirmPassword"
                        type="password"
                        autoComplete="new-password"
                        className="form-input"
                        value={confirmPw}
                        onChange={(e) => setConfirmPw(e.target.value)}
                        minLength={6}
                        required
                      />
                    </div>
                    <div>
                      <button type="submit" disabled={saving} className="btn btn-primary btn-sm">
                        <SaveRoundedIcon sx={{ fontSize: 16 }} />
                        Cập nhật mật khẩu
                      </button>
                    </div>
                  </form>

                  <div style={{ padding: 16, borderRadius: 14, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-light)', display: 'flex', flexDirection: 'column', gap: 10 }}>
                    <strong style={{ fontSize: 14 }}>Thông tin Xác thực &amp; SSO</strong>
                    <div style={{ fontSize: 12.5, color: 'var(--text-secondary)', lineHeight: 1.55 }}>
                      <div>• Cookie phiên <code>smartapps_token</code> sử dụng HttpOnly bảo mật cho cả Web &amp; API.</div>
                      <div>• Khóa tạm thời 60 giây nếu nhập sai mật khẩu quá 5 lần liên tiếp.</div>
                      <div style={{ marginTop: 6 }}>
                        <strong>Liên kết SSO:</strong>{' '}
                        {user?.ssoProvider ? (
                          <span className="badge badge-success">Đã liên kết {user.ssoProvider}</span>
                        ) : (
                          <span className="badge" style={{ backgroundColor: 'var(--bg-secondary)', color: 'var(--text-secondary)' }}>
                            Đăng nhập nội bộ (Email khớp SSO vẫn tự liên kết)
                          </span>
                        )}
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            )}

            {/* TAB 6: SSO PROVIDERS (ADMIN / USER MANAGER) */}
            {tab === 'sso' && canManageSso && (
              <SsoConfigPanel
                roles={roles}
                onToast={(message, severity = 'success') => setSnack({ open: true, message, severity })}
              />
            )}

            {/* TAB 7: AI API CONFIGURATION (ADMIN) */}
            {tab === 'ai' && isAdmin && (
              <AiConfigPanel
                onToast={(message, severity = 'success') => setSnack({ open: true, message, severity })}
              />
            )}

            {/* TAB 8: BACKUP & CLOUD SYNC (ADMIN) */}
            {tab === 'backup' && isAdmin && (
              <BackupManagerPanel
                toast={(message, severity = 'success') => setSnack({ open: true, message, severity })}
              />
            )}

            {/* TAB 9: INTEGRATIONS & PLUGINS (ADMIN) */}
            {tab === 'plugins' && isAdmin && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                <div style={{ borderBottom: '1px solid var(--border-light)', paddingBottom: 14 }}>
                  <h2 style={{ fontSize: 17, fontWeight: 700, margin: 0 }}>Tích hợp Hệ thống &amp; Danh mục Plugin</h2>
                  <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 4 }}>
                    Quản lý kết nối, khóa bí mật (API Key / Secret / Chứng thư số) dùng chung toàn doanh nghiệp. Sau khi kết nối tại đây, từng App có thể bật/tắt và map trường dữ liệu trong App Builder Studio.
                  </p>
                </div>
                <PluginsMarketplacePanel
                  apps={apps}
                  toast={(message, severity = 'success') => setSnack({ open: true, message, severity })}
                />
              </div>
            )}

            {/* TAB 7: SYSTEM SETTINGS (ADMIN) */}
            {tab === 'system' && isAdmin && (
              <form onSubmit={handleSaveSystem} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                <div style={{ borderBottom: '1px solid var(--border-light)', paddingBottom: 14 }}>
                  <h2 style={{ fontSize: 17, fontWeight: 700, margin: 0 }}>Cấu hình Chung Toàn Hệ thống (Admin)</h2>
                  <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 4 }}>
                    Áp dụng đồng bộ cho toàn bộ Doanh nghiệp: thương hiệu, banner thông báo, tính năng Chat &amp; phiên đăng nhập.
                  </p>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(230px, 1fr))', gap: 12 }}>
                  <div>
                    <label htmlFor="sys-company" className="form-label">Tên Doanh nghiệp / Thương hiệu</label>
                    <input
                      id="sys-company"
                      name="companyName"
                      className="form-input"
                      value={sysCfg.companyName}
                      onChange={(e) => setSysCfg((s) => ({ ...s, companyName: e.target.value }))}
                    />
                  </div>
                  <div>
                    <label htmlFor="sys-task-app" className="form-label">App mặc định khi liên kết công việc</label>
                    <select
                      id="sys-task-app"
                      name="defaultTaskAppCode"
                      className="form-select"
                      value={sysCfg.defaultTaskAppCode}
                      onChange={(e) => setSysCfg((s) => ({ ...s, defaultTaskAppCode: e.target.value }))}
                    >
                      {apps.map((a) => (
                        <option key={a.id} value={a.code}>{a.code} — {a.name}</option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label htmlFor="sys-session-days" className="form-label">Thời hạn phiên đăng nhập (ngày)</label>
                    <input
                      id="sys-session-days"
                      name="sessionTimeoutDays"
                      type="number"
                      min={1}
                      max={30}
                      className="form-input"
                      value={sysCfg.sessionTimeoutDays}
                      onChange={(e) => setSysCfg((s) => ({ ...s, sessionTimeoutDays: Number(e.target.value) || 7 }))}
                    />
                    <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 6 }}>
                      Áp dụng cho lần đăng nhập mới. Các phiên hiện tại giữ thời hạn đã cấp.
                    </div>
                  </div>
                </div>

                <div>
                  <label htmlFor="sys-ws-title" className="form-label">Tiêu đề thanh Không gian làm việc (Navbar)</label>
                  <input
                    id="sys-ws-title"
                    name="workspaceTitle"
                    className="form-input"
                    value={sysCfg.workspaceTitle}
                    onChange={(e) => setSysCfg((s) => ({ ...s, workspaceTitle: e.target.value }))}
                  />
                </div>

                <div style={{ padding: '14px 16px', borderRadius: 12, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-light)', display: 'flex', flexDirection: 'column', gap: 12 }}>
                  <div style={{ fontSize: 13.5, fontWeight: 700 }}>Logo &amp; Biểu tượng thương hiệu (Icon)</div>
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 12 }}>
                    <div>
                      <label htmlFor="sys-logo-url" className="form-label">Logo hệ thống (URL hoặc tải ảnh lên)</label>
                      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                        <div className="brand-logo" style={{ width: 38, height: 38, flexShrink: 0, overflow: 'hidden' }}>
                          {sysCfg.logoUrl ? (
                            // eslint-disable-next-line @next/next/no-img-element
                            <img src={sysCfg.logoUrl} alt="Logo preview" style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: 'inherit' }} />
                          ) : sysCfg.brandIcon ? (
                            <ClusterIcon icon={sysCfg.brandIcon} fontSize={20} />
                          ) : (
                            'SA'
                          )}
                        </div>
                        <input
                          id="sys-logo-url"
                          name="logoUrl"
                          className="form-input"
                          placeholder="https://... hoặc /api/attachments/..."
                          value={sysCfg.logoUrl || ''}
                          onChange={(e) => setSysCfg((s) => ({ ...s, logoUrl: e.target.value }))}
                        />
                        <label className="btn btn-outline btn-xs" style={{ cursor: 'pointer', whiteSpace: 'nowrap' }}>
                          Tải ảnh
                          <input
                            type="file"
                            accept="image/*"
                            style={{ display: 'none' }}
                            onChange={async (e) => {
                              const file = e.target.files?.[0];
                              e.target.value = '';
                              if (!file) return;
                              const fd = new FormData();
                              fd.append('file', file);
                              const res = await fetch('/api/attachments', { method: 'POST', credentials: 'include', body: fd }).then((r) => r.json()).catch(() => null);
                              if (res?.success && res.data?.url) {
                                setSysCfg((s) => ({ ...s, logoUrl: res.data.url }));
                                setSnack({ open: true, message: 'Đã tải ảnh logo lên! Nhấn Lưu để áp dụng.', severity: 'success' });
                              } else {
                                setSnack({ open: true, message: res?.message || 'Không thể tải ảnh logo', severity: 'error' });
                              }
                            }}
                          />
                        </label>
                        {sysCfg.logoUrl && (
                          <button type="button" className="btn btn-outline btn-xs" onClick={() => setSysCfg((s) => ({ ...s, logoUrl: '' }))}>
                            Xóa
                          </button>
                        )}
                      </div>
                    </div>

                    <div>
                      <label htmlFor="sys-brand-icon" className="form-label">Icon biểu tượng (khi không dùng ảnh Logo)</label>
                      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                        <select
                          id="sys-brand-icon"
                          name="brandIcon"
                          className="form-select"
                          value={sysCfg.brandIcon || ''}
                          onChange={(e) => setSysCfg((s) => ({ ...s, brandIcon: e.target.value }))}
                        >
                          <option value="">Mặc định (Chữ SA)</option>
                          {APP_ICON_OPTIONS.map((opt) => (
                            <option key={opt.value} value={opt.value}>{opt.label}</option>
                          ))}
                        </select>
                      </div>
                    </div>

                    <div>
                      <label htmlFor="sys-favicon-url" className="form-label">Favicon tab trình duyệt (URL hoặc tải ảnh)</label>
                      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                        <input
                          id="sys-favicon-url"
                          name="faviconUrl"
                          className="form-input"
                          placeholder="https://... hoặc /api/attachments/..."
                          value={sysCfg.faviconUrl || ''}
                          onChange={(e) => setSysCfg((s) => ({ ...s, faviconUrl: e.target.value }))}
                        />
                        <label className="btn btn-outline btn-xs" style={{ cursor: 'pointer', whiteSpace: 'nowrap' }}>
                          Tải icon
                          <input
                            type="file"
                            accept="image/*"
                            style={{ display: 'none' }}
                            onChange={async (e) => {
                              const file = e.target.files?.[0];
                              e.target.value = '';
                              if (!file) return;
                              const fd = new FormData();
                              fd.append('file', file);
                              const res = await fetch('/api/attachments', { method: 'POST', credentials: 'include', body: fd }).then((r) => r.json()).catch(() => null);
                              if (res?.success && res.data?.url) {
                                setSysCfg((s) => ({ ...s, faviconUrl: res.data.url }));
                                setSnack({ open: true, message: 'Đã tải favicon lên! Nhấn Lưu để áp dụng.', severity: 'success' });
                              } else {
                                setSnack({ open: true, message: res?.message || 'Không thể tải favicon', severity: 'error' });
                              }
                            }}
                          />
                        </label>
                        {sysCfg.faviconUrl && (
                          <button type="button" className="btn btn-outline btn-xs" onClick={() => setSysCfg((s) => ({ ...s, faviconUrl: '' }))}>
                            Xóa
                          </button>
                        )}
                      </div>
                    </div>
                  </div>
                </div>

                <div>
                  <label htmlFor="sys-banner" className="form-label">Thông báo nổi toàn công ty trên Navbar (để trống nếu tắt)</label>
                  <input
                    id="sys-banner"
                    name="announcementBanner"
                    className="form-input"
                    placeholder="Ví dụ: Họp giao ban toàn công ty 14:00 thứ 6"
                    value={sysCfg.announcementBanner}
                    onChange={(e) => setSysCfg((s) => ({ ...s, announcementBanner: e.target.value }))}
                  />
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 10 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '12px 14px', borderRadius: 12, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-light)', gap: 12 }}>
                    <div>
                      <div style={{ fontSize: 13.5, fontWeight: 700 }}>Cho phép đính kèm tệp trong Chat</div>
                      <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Nhân viên có thể gửi tài liệu, hình ảnh trong kênh chat</div>
                    </div>
                    <BooleanSwitch
                      checked={sysCfg.allowChatFileUpload}
                      onChange={(v) => setSysCfg((s) => ({ ...s, allowChatFileUpload: v }))}
                      size="sm"
                      trueLabel="Bật"
                      falseLabel="Tắt"
                    />
                  </div>

                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '12px 14px', borderRadius: 12, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-light)', gap: 12 }}>
                    <div>
                      <div style={{ fontSize: 13.5, fontWeight: 700 }}>Giao việc trực tiếp trong Chat</div>
                      <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Bật nút &ldquo;Giao việc&rdquo;, chuyển tin nhắn &amp; lệnh /task</div>
                    </div>
                    <BooleanSwitch
                      checked={sysCfg.allowTaskFromChat}
                      onChange={(v) => setSysCfg((s) => ({ ...s, allowTaskFromChat: v }))}
                      size="sm"
                      trueLabel="Bật"
                      falseLabel="Tắt"
                    />
                  </div>
                </div>

                <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                  <button type="submit" disabled={saving} className="btn btn-primary btn-sm">
                    <SaveRoundedIcon sx={{ fontSize: 16 }} />
                    Lưu cấu hình Hệ thống
                  </button>
                </div>
              </form>
            )}
          </div>
        </div>
      </div>

      <NotificationSnackbar
        open={snack.open}
        message={snack.message}
        severity={snack.severity}
        onClose={() => setSnack((s) => ({ ...s, open: false }))}
      />
    </div>
  );
}

export default function SettingsPage() {
  return (
    <Suspense fallback={<div style={{ padding: 32, color: 'var(--text-muted)' }}>Đang tải cài đặt...</div>}>
      <SettingsPageInner />
    </Suspense>
  );
}
