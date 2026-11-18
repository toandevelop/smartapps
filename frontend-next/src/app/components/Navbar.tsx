'use client';

import React, { useState, useEffect, useRef, useCallback } from 'react';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import MenuRoundedIcon from '@mui/icons-material/MenuRounded';
import DesignServicesRoundedIcon from '@mui/icons-material/DesignServicesRounded';
import DesktopWindowsRoundedIcon from '@mui/icons-material/DesktopWindowsRounded';
import PersonRoundedIcon from '@mui/icons-material/PersonRounded';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import NotificationsNoneRoundedIcon from '@mui/icons-material/NotificationsNoneRounded';
import DoneAllRoundedIcon from '@mui/icons-material/DoneAllRounded';
import LogoutRoundedIcon from '@mui/icons-material/LogoutRounded';
import VpnKeyRoundedIcon from '@mui/icons-material/VpnKeyRounded';
import SyncRoundedIcon from '@mui/icons-material/SyncRounded';
import ForumRoundedIcon from '@mui/icons-material/ForumRounded';
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded';
import AssignmentTurnedInRoundedIcon from '@mui/icons-material/AssignmentTurnedInRounded';
import CampaignRoundedIcon from '@mui/icons-material/CampaignRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded';
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded';
import ViewCompactRoundedIcon from '@mui/icons-material/ViewCompactRounded';
import TableRowsRoundedIcon from '@mui/icons-material/TableRowsRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import AppSwitcherMenu from './AppSwitcherMenu';
import NotificationSnackbar from './ui/NotificationSnackbar';
import CommandPaletteModal from './CommandPaletteModal';
import { useAuth } from '@/lib/auth-context';
import { useSettings } from '@/lib/settings-context';
import { useEscapeKey, useIsTauriDesktop, useSidebarCollapsed } from '@/lib/ui-hooks';
import {
  listOfflineQueue,
  discardOfflineMutation,
  syncAllOfflineMutations,
  syncSingleOfflineMutation,
  getOfflineConflictMap,
  getDesktopServerConfig,
  setDesktopServerConfig,
  type OfflineMutationItem,
  type OfflineSyncErrorMeta,
} from '@/lib/tauri-bridge';
import { SysNotification } from '@/lib/types';

const NOTIF_CATEGORY_LABEL: Record<string, { label: string; color: string }> = {
  TASK: { label: 'Giao việc', color: '#ea8600' },
  CHAT: { label: 'Chat', color: '#1a73e8' },
  MENTION: { label: 'Nhắc tên', color: '#7b1fa2' },
  APPROVAL: { label: 'Phê duyệt', color: '#1e8e3e' },
  AUTOMATION: { label: 'Tự động hóa', color: '#0284c7' },
  SYSTEM: { label: 'Hệ thống', color: '#5f6368' },
};

export default function Navbar() {
  const pathname = usePathname() || '/';
  const router = useRouter();
  const { user, allDemoUsers, canAccessApp, hasPermission, switchDemoUser, logout } = useAuth();
  const {
    userSettings,
    systemSettings,
    unreadSummary,
    language,
    setLanguage,
    t,
    refreshUnreadSummary,
    saveUserSettings,
    playNotificationSound,
    showDesktopNotification,
  } = useSettings();

  const collapsed = useSidebarCollapsed();
  const inDesktop = useIsTauriDesktop();
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const [notifOpen, setNotifOpen] = useState(false);
  const [notifFilter, setNotifFilter] = useState<'ALL' | 'UNREAD' | 'TASK' | 'CHAT' | 'APPROVAL'>('ALL');
  const [notifications, setNotifications] = useState<SysNotification[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [offlineCount, setOfflineCount] = useState(0);
  const [offlineItems, setOfflineItems] = useState<OfflineMutationItem[]>([]);
  const [showPwModal, setShowPwModal] = useState(false);
  const [curPw, setCurPw] = useState('');
  const [newPw, setNewPw] = useState('');
  const [pwMsg, setPwMsg] = useState('');
  const [showDesktopCfg, setShowDesktopCfg] = useState(false);
  const [cmdOpen, setCmdOpen] = useState(false);
  const [onlineUsers, setOnlineUsers] = useState<Array<{ username: string; fullName: string; roleCode: string; department: string }>>([]);
  const [onlineOpen, setOnlineOpen] = useState(false);
  const onlineRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setCmdOpen((prev) => !prev);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);
  const [srvWebUrl, setSrvWebUrl] = useState('http://localhost:4100');
  const [srvApiUrl, setSrvApiUrl] = useState('http://localhost:5200');
  const [navSnack, setNavSnack] = useState<{ open: boolean; message: string; severity: 'success' | 'error' | 'info' }>({
    open: false,
    message: '',
    severity: 'info',
  });
  const menuRef = useRef<HTMLDivElement>(null);
  const notifRef = useRef<HTMLDivElement>(null);

  const loadNotifications = useCallback(async () => {
    if (!user) return;
    try {
      const res = await fetch('/api/notifications?limit=30', { credentials: 'include' });
      if (!res.ok) return;
      const json = await res.json();
      if (json.success && json.data) {
        setNotifications(json.data.items || []);
        setUnreadCount(json.data.unreadCount ?? 0);
      }
    } catch {
      // Ignore network poll error
    }
  }, [user]);

  useEffect(() => {
    document.body.classList.toggle('sidebar-collapsed', collapsed);
  }, [collapsed]);

  useEffect(() => {
    let cancelled = false;
    listOfflineQueue().then((q) => {
      if (cancelled) return;
      setOfflineCount(q.length);
      setOfflineItems(q);
    });
    getDesktopServerConfig().then((cfg) => {
      if (cancelled) return;
      setSrvWebUrl(cfg.webUrl);
      setSrvApiUrl(cfg.apiUrl);
    });
    const onOnline = async () => {
      const queued = await listOfflineQueue();
      if (queued.length === 0) return;
      const res = await syncAllOfflineMutations();
      const left = await listOfflineQueue();
      setOfflineItems(left);
      setOfflineCount(left.length);
      if (res.synced > 0 || res.conflicts > 0) {
        setNavSnack({
          open: true,
          message: `Tự đồng bộ khi có mạng: ${res.synced} thành công${res.conflicts > 0 ? `, ${res.conflicts} xung đột 409 cần xử lý` : ''}.`,
          severity: res.failed > 0 ? 'error' : 'success',
        });
      }
    };
    window.addEventListener('online', onOnline);
    return () => {
      cancelled = true;
      window.removeEventListener('online', onOnline);
    };
  }, []);

  const loadOnlineUsers = useCallback(async () => {
    if (!user) return;
    try {
      const res = await fetch('/api/notifications/online-users', { credentials: 'include' }).then((r) => r.json());
      if (res.success && Array.isArray(res.data)) {
        setOnlineUsers(res.data);
      }
    } catch {
      // ignore
    }
  }, [user]);

  useEffect(() => {
    let cancelled = false;
    if (!user) {
      Promise.resolve().then(() => {
        if (!cancelled) setOnlineUsers([]);
      });
      return () => {
        cancelled = true;
      };
    }
    Promise.resolve().then(() => {
      if (!cancelled) void loadOnlineUsers();
    });
    const presenceTimer = setInterval(loadOnlineUsers, 45000);
    return () => {
      cancelled = true;
      clearInterval(presenceTimer);
    };
  }, [user, loadOnlineUsers]);

  useEffect(() => {
    let cancelled = false;
    if (!user) {
      Promise.resolve().then(() => {
        if (!cancelled) {
          setNotifications([]);
          setUnreadCount(0);
        }
      });
      return () => {
        cancelled = true;
      };
    }
    Promise.resolve().then(() => {
      if (!cancelled) void loadNotifications();
    });

    let es: EventSource | null = null;
    try {
      es = new EventSource('/api/notifications/stream', { withCredentials: true });
      es.addEventListener('platform', (evt) => {
        try {
          const detail = JSON.parse((evt as MessageEvent).data);
          if (detail.type === 'PRESENCE_CHANGED') {
            loadOnlineUsers();
          } else if (detail.type === 'notification') {
            loadNotifications();
            refreshUnreadSummary();
            const payload = detail.data || {};
            const isFromOther = !payload.senderUsername || payload.senderUsername !== user.username;
            if (isFromOther && userSettings.notifyInApp) {
              playNotificationSound();
              showDesktopNotification(payload.title || 'Thông báo mới', payload.message || '', payload.linkUrl);
            }
          } else if (detail.type === 'chat_message') {
            refreshUnreadSummary();
            const payload = detail.data || {};
            if (payload.sender && payload.sender !== user.username && userSettings.notifyChat) {
              playNotificationSound();
            }
          } else if (detail.type === 'task_updated') {
            refreshUnreadSummary();
          }
          window.dispatchEvent(new CustomEvent('smartapps_realtime', { detail }));
        } catch {
          // ignore parse errors
        }
      });
    } catch {
      // ignore if EventSource unavailable
    }

    const timer = setInterval(loadNotifications, 60000);
    return () => {
      clearInterval(timer);
      if (es) es.close();
    };
  }, [user, loadNotifications, loadOnlineUsers, refreshUnreadSummary, playNotificationSound, showDesktopNotification, userSettings.notifyInApp, userSettings.notifyChat]);

  useEffect(() => {
    if (!userMenuOpen && !notifOpen && !onlineOpen) return;
    const onOut = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setUserMenuOpen(false);
      if (notifRef.current && !notifRef.current.contains(e.target as Node)) setNotifOpen(false);
      if (onlineRef.current && !onlineRef.current.contains(e.target as Node)) setOnlineOpen(false);
    };
    document.addEventListener('mousedown', onOut);
    return () => document.removeEventListener('mousedown', onOut);
  }, [userMenuOpen, notifOpen, onlineOpen]);

  useEscapeKey(showPwModal, () => setShowPwModal(false));
  useEscapeKey(showDesktopCfg, () => setShowDesktopCfg(false));
  useEscapeKey(userMenuOpen || notifOpen || onlineOpen, () => {
    setUserMenuOpen(false);
    setNotifOpen(false);
    setOnlineOpen(false);
  });

  if (pathname === '/login' || pathname.startsWith('/forms/')) return null;

  const toggleSidebar = () => {
    if (typeof window !== 'undefined' && window.innerWidth <= 768) {
      window.dispatchEvent(new CustomEvent('smartapps_mobile_sidebar_toggle'));
      return;
    }
    const next = !collapsed;
    localStorage.setItem('smartapps_sidebar_collapsed', String(next));
    if (next) document.body.classList.add('sidebar-collapsed');
    else document.body.classList.remove('sidebar-collapsed');
    window.dispatchEvent(new CustomEvent('smartapps_sidebar_toggle', { detail: { collapsed: next } }));
  };

  const handleMarkRead = async (n: SysNotification) => {
    if (!n.isRead) {
      await fetch(`/api/notifications/${n.id}/read`, { method: 'PATCH', credentials: 'include' }).catch(() => null);
      setNotifications((prev) => prev.map((x) => (x.id === n.id ? { ...x, isRead: true } : x)));
      setUnreadCount((c) => Math.max(0, c - 1));
      refreshUnreadSummary();
    }
    setNotifOpen(false);
    if (n.linkUrl) {
      router.push(n.linkUrl);
    } else if (n.appCode) {
      router.push(`/apps/${n.appCode}`);
    }
  };

  const handleMarkAllRead = async () => {
    await fetch('/api/notifications/read-all', { method: 'POST', credentials: 'include' }).catch(() => null);
    setNotifications((prev) => prev.map((x) => ({ ...x, isRead: true })));
    setUnreadCount(0);
    refreshUnreadSummary();
  };

  const filteredNotifs = notifications.filter((n) => {
    if (notifFilter === 'UNREAD') return !n.isRead;
    if (notifFilter === 'TASK') return n.category === 'TASK';
    if (notifFilter === 'CHAT') return n.category === 'CHAT' || n.category === 'MENTION';
    if (notifFilter === 'APPROVAL') return n.category === 'APPROVAL';
    return true;
  });

  const displayOnlineUsers =
    onlineUsers.length > 0
      ? onlineUsers
      : user
      ? [{ username: user.username, fullName: user.fullName || user.username, roleCode: user.roleCode || 'USER', department: user.department || '' }]
      : [];

  return (
    <div
      className="no-print"
      style={{
        height: 64,
        backgroundColor: 'var(--bg-surface)',
        borderBottom: '1px solid var(--border-subtle)',
        padding: '0 24px',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        flexShrink: 0,
        position: 'sticky',
        top: 0,
        zIndex: 90,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, minWidth: 0, flex: 1 }}>
        <button
          type="button"
          onClick={toggleSidebar}
          title="Thu gọn / Mở rộng menu"
          style={{
            width: 36,
            height: 36,
            borderRadius: '50%',
            border: 'none',
            backgroundColor: 'transparent',
            color: 'var(--text-muted)',
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flexShrink: 0,
          }}
        >
          <MenuRoundedIcon sx={{ fontSize: 22 }} />
        </button>

        {displayOnlineUsers.length > 0 && (
          <div ref={onlineRef} style={{ position: 'relative', display: 'inline-flex', alignItems: 'center', flexShrink: 0 }}>
            <button
              type="button"
              onClick={() => {
                setOnlineOpen((v) => !v);
                setNotifOpen(false);
                setUserMenuOpen(false);
              }}
              aria-label={t('navbar.onlineUsers', { count: displayOnlineUsers.length })}
              title={t('navbar.onlineUsers', { count: displayOnlineUsers.length })}
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                padding: '2px 4px',
                borderRadius: 999,
                border: '1px solid var(--border-light)',
                backgroundColor: 'var(--bg-primary)',
                cursor: 'pointer',
              }}
            >
              {displayOnlineUsers.slice(0, 3).map((ou, idx) => {
                const palette = ['#1a73e8', '#1e8e3e', '#9334e6', '#ea8600', '#0284c7', '#d93025'];
                const color = palette[(ou.username.charCodeAt(0) + idx) % palette.length];
                const parts = (ou.fullName || ou.username).trim().split(/\s+/);
                const initials = parts.length >= 2
                  ? `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase()
                  : (ou.fullName || ou.username).slice(0, 2).toUpperCase();
                return (
                  <span
                    key={ou.username}
                    title={`${ou.fullName || ou.username} (${ou.roleCode})`}
                    style={{
                      width: 26,
                      height: 26,
                      borderRadius: '50%',
                      backgroundColor: color,
                      color: '#ffffff',
                      fontSize: 10.5,
                      fontWeight: 700,
                      display: 'inline-flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      border: '2px solid var(--bg-surface)',
                      marginLeft: idx === 0 ? 0 : -8,
                      position: 'relative',
                    }}
                  >
                    {initials}
                    <span
                      style={{
                        position: 'absolute',
                        bottom: -1,
                        right: -1,
                        width: 8,
                        height: 8,
                        borderRadius: '50%',
                        backgroundColor: '#1e8e3e',
                        border: '1.5px solid var(--bg-surface)',
                      }}
                    />
                  </span>
                );
              })}
              <span
                style={{
                  width: 26,
                  height: 26,
                  borderRadius: '50%',
                  backgroundColor: 'var(--accent-primary-container)',
                  color: 'var(--accent-primary)',
                  fontSize: 11,
                  fontWeight: 700,
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  border: '2px solid var(--bg-surface)',
                  marginLeft: -6,
                }}
              >
                {displayOnlineUsers.length > 3 ? `+${displayOnlineUsers.length - 3}` : displayOnlineUsers.length}
              </span>
            </button>

            {onlineOpen && (
              <div
                style={{
                  position: 'absolute',
                  top: 'calc(100% + 8px)',
                  left: 0,
                  width: 310,
                  backgroundColor: 'var(--bg-surface)',
                  border: '1px solid var(--border-subtle)',
                  borderRadius: 14,
                  boxShadow: 'var(--shadow-lg)',
                  zIndex: 60,
                  overflow: 'hidden',
                }}
              >
                <div
                  style={{
                    padding: '10px 14px',
                    borderBottom: '1px solid var(--border-light)',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                  }}
                >
                  <strong style={{ fontSize: 13 }}>{t('navbar.onlineUsersTitle')}</strong>
                  <span className="badge badge-success" style={{ fontSize: 11 }}>
                    {displayOnlineUsers.length} online
                  </span>
                </div>
                <div style={{ maxHeight: 280, overflowY: 'auto', padding: 6, display: 'flex', flexDirection: 'column', gap: 4 }}>
                  {displayOnlineUsers.map((ou) => {
                    const isMe = ou.username.toLowerCase() === user?.username?.toLowerCase();
                    const parts = (ou.fullName || ou.username).trim().split(/\s+/);
                    const initials = parts.length >= 2
                      ? `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase()
                      : (ou.fullName || ou.username).slice(0, 2).toUpperCase();
                    return (
                      <div
                        key={ou.username}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          gap: 10,
                          padding: '7px 8px',
                          borderRadius: 8,
                          backgroundColor: isMe ? 'var(--bg-primary)' : 'transparent',
                        }}
                      >
                        <span
                          style={{
                            width: 30,
                            height: 30,
                            borderRadius: '50%',
                            backgroundColor: 'var(--accent-primary)',
                            color: '#fff',
                            fontSize: 11,
                            fontWeight: 700,
                            display: 'inline-flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            flexShrink: 0,
                            position: 'relative',
                          }}
                        >
                          {initials}
                          <span
                            style={{
                              position: 'absolute',
                              bottom: 0,
                              right: 0,
                              width: 8,
                              height: 8,
                              borderRadius: '50%',
                              backgroundColor: '#1e8e3e',
                              border: '1.5px solid var(--bg-surface)',
                            }}
                          />
                        </span>
                        <div style={{ flex: 1, minWidth: 0 }}>
                          <div style={{ fontSize: 12.5, fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                            {ou.fullName || ou.username} {isMe && <span style={{ color: 'var(--accent-primary)', fontSize: 11 }}>(Bạn)</span>}
                          </div>
                          <div style={{ fontSize: 11, color: 'var(--text-muted)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                            @{ou.username} • {ou.department || ou.roleCode}
                          </div>
                        </div>
                        {!isMe && (
                          <Link
                            href="/chat"
                            onClick={() => setOnlineOpen(false)}
                            className="btn btn-outline btn-xs"
                            style={{ flexShrink: 0 }}
                          >
                            Chat
                          </Link>
                        )}
                      </div>
                    );
                  })}
                </div>
              </div>
            )}
          </div>
        )}

        {systemSettings.announcementBanner && (
          <span
            className="badge badge-info"
            style={{ maxWidth: 320, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
            title={systemSettings.announcementBanner}
          >
            <CampaignRoundedIcon sx={{ fontSize: 14 }} />
            {systemSettings.announcementBanner}
          </span>
        )}
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexShrink: 0 }}>
        <button
          type="button"
          onClick={() => setCmdOpen(true)}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 8,
            padding: '5px 12px',
            borderRadius: 999,
            backgroundColor: 'var(--bg-primary)',
            border: '1px solid var(--border-subtle)',
            fontSize: 12,
            color: 'var(--text-muted)',
            cursor: 'pointer',
            whiteSpace: 'nowrap',
            flexShrink: 0,
          }}
          title={t('navbar.searchPlaceholder')}
        >
          <SearchRoundedIcon sx={{ fontSize: 15 }} />
          <span>{language === 'en' ? 'Quick search...' : 'Tìm nhanh...'}</span>
          <kbd style={{ fontSize: 10.5, padding: '1px 5px', borderRadius: 4, border: '1px solid var(--border-light)' }}>Ctrl+K</kbd>
        </button>

        {offlineCount > 0 && (
          <button
            type="button"
            className="btn btn-outline btn-sm"
            style={{ color: '#ea8600', borderColor: '#f6ad55' }}
            onClick={async () => {
              const res = await syncAllOfflineMutations();
              const left = await listOfflineQueue();
              setOfflineCount(left.length);
              setNavSnack({
                open: true,
                message: `Đã đồng bộ ${res.synced} bản ghi offline${res.conflicts > 0 ? ` (${res.conflicts} xung đột phiên bản — bản ghi trên máy chủ đã thay đổi)` : res.failed > 0 ? ` (${res.failed} lỗi)` : ''}.`,
                severity: res.failed > 0 ? 'error' : 'success',
              });
            }}
            title="Đồng bộ dữ liệu đã lưu Offline lên máy chủ"
          >
            <SyncRoundedIcon sx={{ fontSize: 15 }} />
            Đồng bộ Offline ({offlineCount})
          </button>
        )}

        <button
          type="button"
          onClick={async () => {
            const q = await listOfflineQueue();
            setOfflineItems(q);
            setOfflineCount(q.length);
            setShowDesktopCfg(true);
          }}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 6,
            padding: '5px 11px',
            borderRadius: 999,
            backgroundColor: 'var(--bg-secondary)',
            border: '1px solid var(--border-light)',
            fontSize: 11.5,
            fontWeight: 600,
            color: 'var(--text-secondary)',
            cursor: 'pointer',
            whiteSpace: 'nowrap',
            flexShrink: 0,
          }}
          title="Cấu hình kết nối Desktop & Offline"
        >
          <DesktopWindowsRoundedIcon sx={{ fontSize: 14, color: inDesktop ? '#1e8e3e' : 'var(--accent-primary)' }} />
          <span>{inDesktop ? 'Desktop App' : 'Desktop'}</span>
        </button>

        {hasPermission('studio.manage') && (
          <Link href="/studio" className="btn btn-outline btn-sm" style={{ whiteSpace: 'nowrap', flexShrink: 0 }}>
            <DesignServicesRoundedIcon sx={{ fontSize: 16 }} />
            {t('navbar.appStudio')}
          </Link>
        )}

        <button
          type="button"
          onClick={async () => {
            const nextLang = language === 'vi' ? 'en' : 'vi';
            await setLanguage(nextLang);
            setNavSnack({
              open: true,
              message: nextLang === 'en' ? 'Language switched to English' : 'Đã chuyển ngôn ngữ sang Tiếng Việt',
              severity: 'info',
            });
          }}
          aria-label={t('navbar.languageLabel')}
          title={t('navbar.languageLabel')}
          style={{
            height: 32,
            padding: '0 10px',
            borderRadius: 999,
            border: '1px solid var(--border-subtle)',
            backgroundColor: 'var(--bg-surface)',
            color: 'var(--text-primary)',
            fontSize: 11.5,
            fontWeight: 700,
            cursor: 'pointer',
            flexShrink: 0,
            letterSpacing: '0.03em',
          }}
        >
          {language.toUpperCase()}
        </button>

        <button
          type="button"
          onClick={async () => {
            const currentlyDark =
              userSettings.themeMode === 'dark' ||
              (userSettings.themeMode === 'auto' &&
                typeof document !== 'undefined' &&
                document.body.classList.contains('theme-dark'));
            const nextMode = currentlyDark ? 'light' : 'dark';
            await saveUserSettings({ ...userSettings, themeMode: nextMode });
          }}
          aria-label="Chuyển chế độ Sáng / Tối"
          title={userSettings.themeMode === 'dark' ? 'Chuyển sang Giao diện Sáng' : 'Chuyển sang Giao diện Tối (Dark Mode)'}
          style={{
            width: 36,
            height: 36,
            borderRadius: '50%',
            border: '1px solid var(--border-subtle)',
            backgroundColor: 'var(--bg-surface)',
            color: 'var(--text-secondary)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            cursor: 'pointer',
            flexShrink: 0,
          }}
        >
          {userSettings.themeMode === 'dark' ? (
            <LightModeRoundedIcon sx={{ fontSize: 18, color: '#f59e0b' }} />
          ) : (
            <DarkModeRoundedIcon sx={{ fontSize: 18 }} />
          )}
        </button>

        <button
          type="button"
          onClick={async () => {
            const nextCompact = !userSettings.compactMode;
            const ok = await saveUserSettings({ ...userSettings, compactMode: nextCompact });
            if (ok) {
              setNavSnack({
                open: true,
                message: nextCompact ? 'Đã bật chế độ hiển thị Gọn (Compact)' : 'Đã chuyển sang chế độ Thoải mái',
                severity: 'info',
              });
            }
          }}
          aria-label="Chuyển mật độ hiển thị Gọn / Thoải mái"
          title={userSettings.compactMode ? 'Đang bật chế độ Gọn (Compact) — Bấm để chuyển Thoải mái' : 'Bật chế độ hiển thị Gọn (Compact)'}
          style={{
            width: 36,
            height: 36,
            borderRadius: '50%',
            border: '1px solid var(--border-subtle)',
            backgroundColor: userSettings.compactMode ? 'var(--accent-primary-container)' : 'var(--bg-surface)',
            color: userSettings.compactMode ? 'var(--accent-primary)' : 'var(--text-secondary)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            cursor: 'pointer',
            flexShrink: 0,
          }}
        >
          {userSettings.compactMode ? (
            <ViewCompactRoundedIcon sx={{ fontSize: 18 }} />
          ) : (
            <TableRowsRoundedIcon sx={{ fontSize: 18 }} />
          )}
        </button>

        <AppSwitcherMenu />

        {/* Quick Internal Chat Button */}
        <Link
          href="/chat"
          title="Chat Nội bộ & Giao việc trực tuyến"
          style={{
            width: 38,
            height: 38,
            borderRadius: '50%',
            border: '1px solid var(--border-subtle)',
            backgroundColor: pathname.startsWith('/chat') ? 'var(--accent-primary-container)' : 'var(--bg-surface)',
            color: pathname.startsWith('/chat') || unreadSummary.unreadChatCount > 0 ? 'var(--accent-primary)' : 'var(--text-muted)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            position: 'relative',
            textDecoration: 'none',
          }}
        >
          <ForumRoundedIcon sx={{ fontSize: 19 }} />
          {unreadSummary.unreadChatCount > 0 && (
            <span
              style={{
                position: 'absolute',
                top: -2,
                right: -2,
                minWidth: 18,
                height: 18,
                padding: '0 4px',
                borderRadius: 999,
                backgroundColor: '#1a73e8',
                color: '#ffffff',
                fontSize: 10,
                fontWeight: 700,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              {unreadSummary.unreadChatCount > 9 ? '9+' : unreadSummary.unreadChatCount}
            </span>
          )}
        </Link>

        {/* Notification Center Bell */}
        <div ref={notifRef} style={{ position: 'relative' }}>
          <button
            type="button"
            onClick={() => {
              const next = !notifOpen;
              setNotifOpen(next);
              if (next) loadNotifications();
            }}
            title="Trung tâm Thông báo"
            style={{
              width: 38,
              height: 38,
              borderRadius: '50%',
              border: '1px solid var(--border-subtle)',
              backgroundColor: 'var(--bg-surface)',
              color: unreadCount > 0 ? 'var(--accent-primary)' : 'var(--text-muted)',
              cursor: 'pointer',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              position: 'relative',
            }}
          >
            <NotificationsNoneRoundedIcon sx={{ fontSize: 20 }} />
            {unreadCount > 0 && (
              <span
                style={{
                  position: 'absolute',
                  top: -2,
                  right: -2,
                  minWidth: 18,
                  height: 18,
                  padding: '0 4px',
                  borderRadius: 999,
                  backgroundColor: '#d93025',
                  color: '#ffffff',
                  fontSize: 10,
                  fontWeight: 700,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                {unreadCount > 9 ? '9+' : unreadCount}
              </span>
            )}
          </button>

          {notifOpen && (
            <div
              style={{
                position: 'absolute',
                right: 0,
                top: 'calc(100% + 8px)',
                width: 380,
                maxHeight: 480,
                display: 'flex',
                flexDirection: 'column',
                backgroundColor: 'var(--bg-surface)',
                border: '1px solid var(--border-subtle)',
                borderRadius: 16,
                boxShadow: 'var(--shadow-lg)',
                zIndex: 1500,
                overflow: 'hidden',
              }}
            >
              <div
                style={{
                  padding: '12px 14px 8px',
                  borderBottom: '1px solid var(--border-light)',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 8,
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 13.5, fontWeight: 700, color: 'var(--text-primary)' }}>
                    Thông báo ({unreadCount} chưa đọc)
                  </span>
                  {unreadCount > 0 && (
                    <button
                      type="button"
                      onClick={handleMarkAllRead}
                      style={{
                        border: 'none',
                        background: 'transparent',
                        color: 'var(--accent-primary)',
                        fontSize: 11.5,
                        fontWeight: 600,
                        cursor: 'pointer',
                        display: 'inline-flex',
                        alignItems: 'center',
                        gap: 4,
                      }}
                    >
                      <DoneAllRoundedIcon sx={{ fontSize: 14 }} />
                      Đọc tất cả
                    </button>
                  )}
                </div>
                <div style={{ display: 'flex', gap: 5, flexWrap: 'wrap' }}>
                  {(
                    [
                      ['ALL', 'Tất cả'],
                      ['UNREAD', 'Chưa đọc'],
                      ['TASK', 'Giao việc'],
                      ['CHAT', 'Chat'],
                      ['APPROVAL', 'Phê duyệt'],
                    ] as const
                  ).map(([key, label]) => (
                    <button
                      key={key}
                      type="button"
                      onClick={() => setNotifFilter(key)}
                      style={{
                        padding: '3px 9px',
                        borderRadius: 999,
                        border: '1px solid var(--border-light)',
                        backgroundColor: notifFilter === key ? 'var(--accent-primary-container)' : 'var(--bg-secondary)',
                        color: notifFilter === key ? 'var(--accent-primary)' : 'var(--text-secondary)',
                        fontSize: 11,
                        fontWeight: 600,
                        cursor: 'pointer',
                      }}
                    >
                      {label}
                    </button>
                  ))}
                </div>
              </div>

              <div style={{ overflowY: 'auto', maxHeight: 340 }}>
                {filteredNotifs.length === 0 ? (
                  <div style={{ padding: 24, textAlign: 'center', fontSize: 12.5, color: 'var(--text-muted)' }}>
                    Không có thông báo phù hợp.
                  </div>
                ) : (
                  filteredNotifs.map((n) => {
                    const cat = NOTIF_CATEGORY_LABEL[n.category || 'SYSTEM'] || NOTIF_CATEGORY_LABEL.SYSTEM;
                    return (
                      <button
                        key={n.id}
                        type="button"
                        onClick={() => handleMarkRead(n)}
                        style={{
                          width: '100%',
                          textAlign: 'left',
                          padding: '10px 14px',
                          border: 'none',
                          borderBottom: '1px solid var(--border-light)',
                          backgroundColor: n.isRead ? 'var(--bg-surface)' : 'var(--accent-primary-container)',
                          cursor: 'pointer',
                          display: 'block',
                        }}
                      >
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
                          <span style={{ fontSize: 12.5, fontWeight: n.isRead ? 600 : 700, color: 'var(--text-primary)' }}>
                            {n.title}
                          </span>
                          <span
                            className="badge"
                            style={{
                              backgroundColor: `${cat.color}18`,
                              color: cat.color,
                              fontSize: 10,
                              padding: '2px 7px',
                              flexShrink: 0,
                            }}
                          >
                            {n.appCode || cat.label}
                          </span>
                        </div>
                        {n.message && (
                          <div style={{ fontSize: 11.5, color: 'var(--text-secondary)', marginTop: 3 }}>
                            {n.message}
                          </div>
                        )}
                        <div style={{ fontSize: 10.5, color: 'var(--text-muted)', marginTop: 4 }}>
                          {new Date(n.createdAt).toLocaleString('vi-VN')}
                        </div>
                      </button>
                    );
                  })
                )}
              </div>

              <div
                style={{
                  padding: '8px 14px',
                  borderTop: '1px solid var(--border-light)',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  backgroundColor: 'var(--bg-secondary)',
                }}
              >
                <Link
                  href="/notifications"
                  onClick={() => setNotifOpen(false)}
                  style={{ fontSize: 12, fontWeight: 700, color: 'var(--accent-primary)', textDecoration: 'none' }}
                >
                  Xem tất cả thông báo
                </Link>
                <Link
                  href="/settings?tab=notifications"
                  onClick={() => setNotifOpen(false)}
                  style={{ fontSize: 11.5, fontWeight: 600, color: 'var(--text-muted)', textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: 4 }}
                >
                  <SettingsRoundedIcon sx={{ fontSize: 14 }} />
                  Cài đặt
                </Link>
              </div>
            </div>
          )}
        </div>

        {/* User / RBAC Role Switcher */}
        <div ref={menuRef} style={{ position: 'relative' }}>
          <button
            type="button"
            onClick={() => setUserMenuOpen((p) => !p)}
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 8,
              padding: '4px 10px 4px 6px',
              borderRadius: 999,
              border: '1px solid var(--border-subtle)',
              backgroundColor: 'var(--bg-surface)',
              cursor: 'pointer',
            }}
            title="Tài khoản / Chuyển vai trò phân quyền (RBAC)"
          >
            <div
              style={{
                width: 28,
                height: 28,
                borderRadius: '50%',
                backgroundColor: 'var(--accent-primary-container)',
                color: 'var(--accent-primary)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <PersonRoundedIcon sx={{ fontSize: 17 }} />
            </div>
            <div style={{ textAlign: 'left', lineHeight: 1.15 }}>
              <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-primary)' }}>
                {user?.username || 'Chưa đăng nhập'}
              </div>
              <div style={{ fontSize: 10, fontWeight: 600, color: 'var(--accent-primary)' }}>
                {user?.roleCodes?.length ? user.roleCodes.join(' · ') : user?.roleCode || 'GUEST'}
              </div>
            </div>
            <ExpandMoreRoundedIcon sx={{ fontSize: 16, color: 'var(--text-muted)' }} />
          </button>

          {userMenuOpen && (
            <div
              style={{
                position: 'absolute',
                right: 0,
                top: 'calc(100% + 8px)',
                width: 300,
                backgroundColor: 'var(--bg-surface)',
                border: '1px solid var(--border-subtle)',
                borderRadius: 14,
                boxShadow: 'var(--shadow-lg)',
                padding: 12,
                zIndex: 1500,
              }}
            >
              <div style={{ fontSize: 11.5, fontWeight: 700, color: 'var(--text-muted)', marginBottom: 8, padding: '0 4px' }}>
                CHUYỂN VAI TRÒ KIỂM THỬ RBAC
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                {allDemoUsers.map((u) => {
                  const active = u.username === user?.username;
                  return (
                    <button
                      key={u.username}
                      type="button"
                      onClick={async () => {
                        await switchDemoUser(u.username);
                        setUserMenuOpen(false);
                      }}
                      style={{
                        textAlign: 'left',
                        padding: '8px 10px',
                        borderRadius: 10,
                        border: `1px solid ${active ? 'var(--accent-primary)' : 'transparent'}`,
                        backgroundColor: active ? 'var(--accent-primary-container)' : 'var(--bg-secondary)',
                        color: 'var(--text-primary)',
                        cursor: 'pointer',
                      }}
                    >
                      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12.5, fontWeight: 700, gap: 6 }}>
                        <span>{u.fullName}</span>
                        <span className="badge badge-info">
                          {u.roleCodes?.length ? u.roleCodes.join(', ') : u.roleCode}
                        </span>
                      </div>
                      <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>
                        Apps: {u.allowedApps.length > 0 ? u.allowedApps.join(', ') : 'Chưa cấp'}
                      </div>
                    </button>
                  );
                })}
              </div>

              <div style={{ borderTop: '1px solid var(--border-light)', marginTop: 10, paddingTop: 10, display: 'flex', flexDirection: 'column', gap: 6 }}>
                {canAccessApp('TASKS') && (
                  <Link
                    href="/apps/TASKS"
                    onClick={() => setUserMenuOpen(false)}
                    className="btn btn-outline btn-sm"
                    style={{ width: '100%', justifyContent: 'center' }}
                  >
                    <AssignmentTurnedInRoundedIcon sx={{ fontSize: 16 }} />
                    Giao việc &amp; Tiến độ ({unreadSummary.pendingTaskCount})
                  </Link>
                )}
                <Link
                  href="/settings"
                  onClick={() => setUserMenuOpen(false)}
                  className="btn btn-outline btn-sm"
                  style={{ width: '100%', justifyContent: 'center' }}
                >
                  <SettingsRoundedIcon sx={{ fontSize: 16 }} />
                  Cài đặt tài khoản &amp; Giao diện
                </Link>
                <button
                  type="button"
                  onClick={() => {
                    setUserMenuOpen(false);
                    setPwMsg('');
                    setCurPw('');
                    setNewPw('');
                    setShowPwModal(true);
                  }}
                  style={{
                    width: '100%',
                    padding: '7px 12px',
                    borderRadius: 10,
                    border: '1px solid var(--border-subtle)',
                    backgroundColor: 'var(--bg-secondary)',
                    color: 'var(--text-primary)',
                    fontSize: 12.5,
                    fontWeight: 600,
                    cursor: 'pointer',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    gap: 6,
                  }}
                >
                  <VpnKeyRoundedIcon sx={{ fontSize: 16 }} />
                  Đổi mật khẩu
                </button>
                <button
                  type="button"
                  onClick={async () => {
                    setUserMenuOpen(false);
                    await logout();
                  }}
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: 10,
                    border: '1px solid #f5c2c7',
                    backgroundColor: '#fce8e6',
                    color: '#c5221f',
                    fontSize: 12.5,
                    fontWeight: 700,
                    cursor: 'pointer',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    gap: 6,
                  }}
                >
                  <LogoutRoundedIcon sx={{ fontSize: 16 }} />
                  Đăng xuất phiên làm việc
                </button>
              </div>
            </div>
          )}
        </div>
      </div>

      {showPwModal && (
        <div className="modal-overlay" onClick={() => setShowPwModal(false)}>
          <div className="modal-dialog" style={{ maxWidth: 420 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16, fontWeight: 700, marginBottom: 12 }}>Đổi mật khẩu tài khoản</h3>
            <form
              onSubmit={async (e) => {
                e.preventDefault();
                setPwMsg('');
                const res = await fetch('/api/auth/change-password', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  credentials: 'include',
                  body: JSON.stringify({ currentPassword: curPw, newPassword: newPw }),
                }).then((r) => r.json());
                setPwMsg(res.message || (res.success ? 'Thành công!' : 'Thất bại'));
                if (res.success) setTimeout(() => setShowPwModal(false), 900);
              }}
              style={{ display: 'flex', flexDirection: 'column', gap: 10 }}
            >
              <input
                type="password"
                className="form-input"
                placeholder="Mật khẩu hiện tại"
                value={curPw}
                onChange={(e) => setCurPw(e.target.value)}
                required
              />
              <input
                type="password"
                className="form-input"
                placeholder="Mật khẩu mới (>= 6 ký tự)"
                value={newPw}
                onChange={(e) => setNewPw(e.target.value)}
                required
              />
              {pwMsg && <div style={{ fontSize: 12.5, fontWeight: 600, color: '#1a73e8' }}>{pwMsg}</div>}
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 6 }}>
                <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowPwModal(false)}>Hủy</button>
                <button type="submit" className="btn btn-primary btn-sm">Cập nhật mật khẩu</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {showDesktopCfg && (
        <div className="modal-overlay" onClick={() => setShowDesktopCfg(false)}>
          <div className="modal-dialog" style={{ maxWidth: 460 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16, fontWeight: 700, marginBottom: 8 }}>Cấu hình Server Desktop &amp; Offline</h3>
            <p style={{ fontSize: 12, color: 'var(--text-muted)', marginBottom: 12 }}>
              Trạng thái: <strong>{inDesktop ? 'Đang chạy trên Ứng dụng Desktop' : 'Đang chạy trên Trình duyệt Web'}</strong> • Hàng đợi offline: <strong>{offlineCount}</strong> mục.
            </p>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              <div>
                <label className="form-label">Địa chỉ Giao diện Web (App URL)</label>
                <input className="form-input" value={srvWebUrl} onChange={(e) => setSrvWebUrl(e.target.value)} />
              </div>
              <div>
                <label className="form-label">Địa chỉ Máy chủ Dữ liệu (API URL)</label>
                <input className="form-input" value={srvApiUrl} onChange={(e) => setSrvApiUrl(e.target.value)} />
              </div>
              <div style={{ marginTop: 4, padding: 10, borderRadius: 10, border: '1px solid var(--border-light)', background: 'var(--bg-secondary)' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
                  <span style={{ fontSize: 12, fontWeight: 700 }}>Hàng đợi đồng bộ Offline ({offlineItems.length})</span>
                  {offlineItems.length > 0 && (
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      onClick={async () => {
                        const res = await syncAllOfflineMutations();
                        const left = await listOfflineQueue();
                        setOfflineItems(left);
                        setOfflineCount(left.length);
                        setNavSnack({
                          open: true,
                          message: `Đã đồng bộ ${res.synced} bản ghi offline${res.conflicts > 0 ? ` (${res.conflicts} xung đột phiên bản)` : res.failed > 0 ? ` (${res.failed} lỗi)` : ''}.`,
                          severity: res.failed > 0 ? 'error' : 'success',
                        });
                      }}
                    >
                      <SyncRoundedIcon sx={{ fontSize: 13 }} /> Đồng bộ ngay
                    </button>
                  )}
                </div>
                {offlineItems.length === 0 ? (
                  <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>Không có thay đổi nào đang chờ đồng bộ.</div>
                ) : (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 6, maxHeight: 160, overflowY: 'auto' }}>
                    {(() => {
                      const conflictMap: Record<string, OfflineSyncErrorMeta> = getOfflineConflictMap();
                      return offlineItems.map((item) => {
                        const errMeta = conflictMap[item.id];
                        return (
                      <div
                        key={item.id}
                        style={{
                          display: 'flex',
                          justifyContent: 'space-between',
                          alignItems: 'center',
                          gap: 8,
                          padding: '6px 8px',
                          borderRadius: 6,
                          background: 'var(--bg-surface)',
                          border: errMeta?.status === 'conflict' ? '1px solid var(--danger)' : '1px solid var(--border-light)',
                          fontSize: 11.5,
                        }}
                      >
                        <div style={{ minWidth: 0, flex: 1 }}>
                          <div style={{ fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {item.method || 'POST'} • {item.appCode}
                            {errMeta && (
                              <span
                                style={{
                                  marginLeft: 6,
                                  fontSize: 10,
                                  padding: '1px 5px',
                                  borderRadius: 4,
                                  backgroundColor: errMeta.status === 'conflict' ? 'var(--danger-bg)' : 'var(--warning-bg)',
                                  color: errMeta.status === 'conflict' ? 'var(--danger-text)' : 'var(--warning-text)',
                                }}
                              >
                                {errMeta.status === 'conflict' ? 'Xung đột 409' : 'Chờ thử lại'}
                              </span>
                            )}
                          </div>
                          <div style={{ fontSize: 10.5, color: errMeta ? 'var(--danger-text)' : 'var(--text-muted)' }}>
                            {errMeta ? errMeta.message : new Date(item.createdAt).toLocaleString('vi-VN')}
                          </div>
                        </div>
                        <div style={{ display: 'inline-flex', gap: 4, flexShrink: 0 }}>
                          <button
                            type="button"
                            className="btn btn-outline btn-xs"
                            title="Thử đồng bộ lại mục này"
                            onClick={async () => {
                              const r = await syncSingleOfflineMutation(item.id);
                              const left = await listOfflineQueue();
                              setOfflineItems(left);
                              setOfflineCount(left.length);
                              setNavSnack({
                                open: true,
                                message: r.status === 'synced' ? 'Đã đồng bộ mục offline.' : (r.message || 'Đồng bộ lại chưa thành công.'),
                                severity: r.status === 'synced' ? 'success' : 'error',
                              });
                            }}
                          >
                            <SyncRoundedIcon sx={{ fontSize: 13 }} />
                          </button>
                          {errMeta?.status === 'conflict' && (
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              style={{ color: 'var(--warning-text)', borderColor: 'var(--warning-border)' }}
                              title="Ép ghi đè thay đổi mới nhất lên máy chủ (bỏ kiểm tra expectedVersion)"
                              onClick={async () => {
                                const r = await syncSingleOfflineMutation(item.id, { forceOverwrite: true });
                                const left = await listOfflineQueue();
                                setOfflineItems(left);
                                setOfflineCount(left.length);
                                setNavSnack({
                                  open: true,
                                  message: r.status === 'synced' ? 'Đã ép ghi đè thay đổi lên máy chủ.' : (r.message || 'Ghi đè thất bại.'),
                                  severity: r.status === 'synced' ? 'success' : 'error',
                                });
                              }}
                            >
                              Ghi đè
                            </button>
                          )}
                          <button
                            type="button"
                            className="btn btn-outline btn-xs"
                            aria-label="Xóa mục khỏi hàng đợi"
                            onClick={async () => {
                              const left = await discardOfflineMutation(item.id);
                              setOfflineItems(left);
                              setOfflineCount(left.length);
                            }}
                          >
                            <DeleteOutlineRoundedIcon sx={{ fontSize: 14, color: 'var(--danger)' }} />
                          </button>
                        </div>
                      </div>
                        );
                      });
                    })()}
                  </div>
                )}
              </div>
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 6 }}>
                <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowDesktopCfg(false)}>Đóng</button>
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  onClick={async () => {
                    if (inDesktop) await setDesktopServerConfig(srvWebUrl, srvApiUrl);
                    setShowDesktopCfg(false);
                    setNavSnack({ open: true, message: 'Đã lưu cấu hình kết nối!', severity: 'success' });
                  }}
                >
                  Lưu cấu hình
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      <NotificationSnackbar
        open={navSnack.open}
        message={navSnack.message}
        severity={navSnack.severity}
        onClose={() => setNavSnack((s) => ({ ...s, open: false }))}
      />

      <CommandPaletteModal open={cmdOpen} onClose={() => setCmdOpen(false)} />
    </div>
  );
}
