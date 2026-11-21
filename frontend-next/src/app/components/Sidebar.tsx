'use client';

import React, { useState, useEffect, useMemo } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import DashboardRoundedIcon from '@mui/icons-material/DashboardRounded';
import StorefrontRoundedIcon from '@mui/icons-material/StorefrontRounded';
import DesignServicesRoundedIcon from '@mui/icons-material/DesignServicesRounded';
import AdminPanelSettingsRoundedIcon from '@mui/icons-material/AdminPanelSettingsRounded';
import ForumRoundedIcon from '@mui/icons-material/ForumRounded';
import NotificationsRoundedIcon from '@mui/icons-material/NotificationsRounded';
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded';
import PushPinRoundedIcon from '@mui/icons-material/PushPinRounded';
import PushPinOutlinedIcon from '@mui/icons-material/PushPinOutlined';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import ChevronRightRoundedIcon from '@mui/icons-material/ChevronRightRounded';
import ClusterIcon from './ui/ClusterIcon';
import { usePlatform } from '@/lib/app-context';
import { useAuth } from '@/lib/auth-context';
import { useSettings } from '@/lib/settings-context';
import { useSidebarCollapsed } from '@/lib/ui-hooks';
import { SysApp } from '@/lib/types';

export default function Sidebar() {
  const pathname = usePathname() || '/';
  const { apps } = usePlatform();
  const { canAccessApp, hasPermission } = useAuth();
  const { unreadSummary, systemSettings, pinnedApps, togglePinnedApp, t } = useSettings();
  const collapsed = useSidebarCollapsed();
  const [openMobilePath, setOpenMobilePath] = useState<string | null>(null);
  const [appFilter, setAppFilter] = useState('');
  const [collapsedGroups, setCollapsedGroups] = useState<Record<string, boolean>>(() => {
    if (typeof window === 'undefined') return {};
    try {
      const raw = localStorage.getItem('smartapps_sidebar_collapsed_groups');
      return raw ? (JSON.parse(raw) as Record<string, boolean>) : {};
    } catch {
      return {};
    }
  });
  const mobileOpen = openMobilePath === pathname;

  useEffect(() => {
    const mobileHandler = () => setOpenMobilePath((prev) => (prev === pathname ? null : pathname));
    window.addEventListener('smartapps_mobile_sidebar_toggle', mobileHandler);
    return () => {
      window.removeEventListener('smartapps_mobile_sidebar_toggle', mobileHandler);
    };
  }, [pathname]);

  const visibleApps = useMemo(
    () => apps.filter((a) => canAccessApp(a.code)),
    [apps, canAccessApp],
  );

  const pinnedSet = useMemo(() => new Set(pinnedApps.map((c) => c.toUpperCase())), [pinnedApps]);

  const filteredApps = useMemo(() => {
    const q = appFilter.trim().toLowerCase();
    if (!q) return visibleApps;
    return visibleApps.filter(
      (a) =>
        a.name.toLowerCase().includes(q) ||
        a.code.toLowerCase().includes(q) ||
        (a.department || '').toLowerCase().includes(q),
    );
  }, [visibleApps, appFilter]);

  const pinnedAppObjects = useMemo(() => {
    const map = new Map(filteredApps.map((a) => [a.code.toUpperCase(), a]));
    return pinnedApps.map((code) => map.get(code.toUpperCase())).filter((a): a is SysApp => Boolean(a));
  }, [filteredApps, pinnedApps]);

  const unpinnedApps = useMemo(
    () => filteredApps.filter((a) => !pinnedSet.has(a.code.toUpperCase())),
    [filteredApps, pinnedSet],
  );

  const departmentGroups = useMemo(() => {
    const groups = new Map<string, SysApp[]>();
    for (const app of unpinnedApps) {
      const dept = (app.department || 'Toàn công ty').trim() || 'Toàn công ty';
      const list = groups.get(dept) ?? [];
      list.push(app);
      groups.set(dept, list);
    }
    return Array.from(groups.entries()).sort((a, b) => a[0].localeCompare(b[0], 'vi'));
  }, [unpinnedApps]);

  const useGroupedDisplay = !collapsed && !appFilter.trim() && departmentGroups.length > 1 && visibleApps.length >= 6;

  if (pathname === '/login' || pathname.startsWith('/forms/')) return null;

  const toggleGroup = (dept: string) => {
    setCollapsedGroups((prev) => {
      const next = { ...prev, [dept]: !prev[dept] };
      try {
        localStorage.setItem('smartapps_sidebar_collapsed_groups', JSON.stringify(next));
      } catch {
        // ignore storage quota errors
      }
      return next;
    });
  };

  const renderBadge = (count: number, bgToken = 'var(--danger)') =>
    count > 0 && !collapsed ? (
      <span
        style={{
          marginLeft: 'auto',
          minWidth: 20,
          height: 20,
          padding: '0 6px',
          borderRadius: 999,
          backgroundColor: bgToken,
          color: '#ffffff',
          fontSize: 10.5,
          fontWeight: 700,
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        {count > 99 ? '99+' : count}
      </span>
    ) : null;

  const renderAppRow = (app: SysApp) => {
    const href = `/apps/${app.code}`;
    const active = pathname.toUpperCase().startsWith(`/APPS/${app.code.toUpperCase()}`);
    const isTasksApp = app.code.toUpperCase() === 'TASKS';
    const isPinned = pinnedSet.has(app.code.toUpperCase());

    return (
      <div key={app.id} className="sidebar-app-row">
        <Link href={href} className={`nav-link ${active ? 'active' : ''}`} title={app.name}>
          <span
            style={{
              width: 26,
              height: 26,
              borderRadius: 8,
              backgroundColor: active ? app.color : app.bgColor,
              color: active ? '#ffffff' : app.color,
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0,
            }}
          >
            <ClusterIcon icon={app.icon} fontSize={16} />
          </span>
          <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {app.name}
          </span>
          {isTasksApp && renderBadge(unreadSummary.pendingTaskCount, 'var(--warning)')}
        </Link>
        {!collapsed && (
          <button
            type="button"
            className={`sidebar-pin-btn ${isPinned ? 'pinned' : ''}`}
            onClick={(e) => {
              e.preventDefault();
              e.stopPropagation();
              void togglePinnedApp(app.code);
            }}
            aria-label={isPinned ? `Bỏ ghim ${app.name}` : `Ghim ${app.name}`}
            title={isPinned ? 'Bỏ ghim khỏi mục yêu thích' : 'Ghim lên đầu danh sách'}
          >
            {isPinned ? <PushPinRoundedIcon sx={{ fontSize: 14 }} /> : <PushPinOutlinedIcon sx={{ fontSize: 14 }} />}
          </button>
        )}
      </div>
    );
  };

  return (
    <>
      {mobileOpen && (
        <div
          className="sidebar-mobile-backdrop"
          onClick={() => setOpenMobilePath(null)}
          aria-hidden="true"
        />
      )}
      <aside className={`app-sidebar ${collapsed ? 'collapsed' : ''} ${mobileOpen ? 'mobile-open' : ''}`}>
        <div className="sidebar-header">
          <div className="brand-logo" style={{ overflow: 'hidden' }}>
            {systemSettings.logoUrl ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img
                src={systemSettings.logoUrl}
                alt={systemSettings.companyName || 'Logo'}
                style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: 'inherit' }}
              />
            ) : systemSettings.brandIcon ? (
              <ClusterIcon icon={systemSettings.brandIcon} fontSize={20} />
            ) : (
              'SA'
            )}
          </div>
          <div className="brand-info">
            <h2>{systemSettings.companyName || 'SmartApps Hub'}</h2>
            <p>Multi-App Dynamic Core</p>
          </div>
        </div>

        <nav className="sidebar-nav">
          <div className="nav-section-label">{t('nav.section.platform')}</div>
          <Link href="/" className={`nav-link ${pathname === '/' ? 'active' : ''}`}>
            <DashboardRoundedIcon sx={{ fontSize: 20 }} />
            <span>{t('nav.portal')}</span>
          </Link>
          <Link href="/store" className={`nav-link ${pathname.startsWith('/store') ? 'active' : ''}`}>
            <StorefrontRoundedIcon sx={{ fontSize: 20 }} />
            <span>{t('nav.store')}</span>
          </Link>
          {hasPermission('studio.manage') && (
            <Link href="/studio" className={`nav-link ${pathname.startsWith('/studio') ? 'active' : ''}`}>
              <DesignServicesRoundedIcon sx={{ fontSize: 20 }} />
              <span>{t('nav.studio')}</span>
            </Link>
          )}
          {(hasPermission('user.manage') || hasPermission('role.manage') || hasPermission('workflow.manage') || hasPermission('automation.manage') || hasPermission('audit.view') || hasPermission('studio.manage')) && (
            <Link href="/admin" className={`nav-link ${pathname.startsWith('/admin') ? 'active' : ''}`}>
              <AdminPanelSettingsRoundedIcon sx={{ fontSize: 20 }} />
              <span>{t('nav.admin')}</span>
            </Link>
          )}

          <div className="nav-section-label">{t('nav.chat')} &amp; {t('nav.notifications')}</div>
          <Link href="/chat" className={`nav-link ${pathname.startsWith('/chat') ? 'active' : ''}`}>
            <ForumRoundedIcon sx={{ fontSize: 20 }} />
            <span>{t('nav.chat')}</span>
            {renderBadge(unreadSummary.unreadChatCount, 'var(--accent-primary)')}
          </Link>
          <Link href="/notifications" className={`nav-link ${pathname.startsWith('/notifications') ? 'active' : ''}`}>
            <NotificationsRoundedIcon sx={{ fontSize: 20 }} />
            <span>{t('nav.notifications')}</span>
            {renderBadge(unreadSummary.unreadNotifCount, 'var(--danger)')}
          </Link>
          <Link href="/settings" className={`nav-link ${pathname.startsWith('/settings') ? 'active' : ''}`}>
            <SettingsRoundedIcon sx={{ fontSize: 20 }} />
            <span>{t('nav.settings')}</span>
          </Link>

          <div className="nav-section-label">{t('nav.section.apps')} ({visibleApps.length})</div>

          {!collapsed && visibleApps.length >= 6 && (
            <div className="sidebar-search-wrap">
              <SearchRoundedIcon
                sx={{
                  fontSize: 15,
                  position: 'absolute',
                  left: 9,
                  top: '50%',
                  transform: 'translateY(-50%)',
                  color: 'var(--text-muted)',
                  pointerEvents: 'none',
                }}
              />
              <input
                type="text"
                className="sidebar-search-input"
                aria-label="Lọc nhanh ứng dụng trên thanh bên"
                placeholder="Lọc nhanh App..."
                value={appFilter}
                onChange={(e) => setAppFilter(e.target.value)}
              />
              {appFilter && (
                <button
                  type="button"
                  aria-label="Xóa lọc ứng dụng"
                  onClick={() => setAppFilter('')}
                  style={{
                    position: 'absolute',
                    right: 6,
                    top: '50%',
                    transform: 'translateY(-50%)',
                    border: 'none',
                    background: 'transparent',
                    color: 'var(--text-muted)',
                    cursor: 'pointer',
                    display: 'inline-flex',
                    padding: 2,
                  }}
                >
                  <CloseRoundedIcon sx={{ fontSize: 14 }} />
                </button>
              )}
            </div>
          )}

          {pinnedAppObjects.length > 0 && (
            <div style={{ marginBottom: 4 }}>
              {!collapsed && (
                <div className="sidebar-group-header" style={{ cursor: 'default' }}>
                  <span>Đã ghim ({pinnedAppObjects.length})</span>
                </div>
              )}
              {pinnedAppObjects.map(renderAppRow)}
            </div>
          )}

          {useGroupedDisplay ? (
            departmentGroups.map(([dept, deptApps]) => {
              const isCollapsedGroup = Boolean(collapsedGroups[dept]);
              return (
                <div key={dept} style={{ marginBottom: 2 }}>
                  <button
                    type="button"
                    className="sidebar-group-header"
                    onClick={() => toggleGroup(dept)}
                    aria-expanded={!isCollapsedGroup}
                  >
                    <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {dept} ({deptApps.length})
                    </span>
                    {isCollapsedGroup ? (
                      <ChevronRightRoundedIcon sx={{ fontSize: 15 }} />
                    ) : (
                      <ExpandMoreRoundedIcon sx={{ fontSize: 15 }} />
                    )}
                  </button>
                  {!isCollapsedGroup && deptApps.map(renderAppRow)}
                </div>
              );
            })
          ) : (
            unpinnedApps.map(renderAppRow)
          )}

          {!collapsed && appFilter.trim() && filteredApps.length === 0 && (
            <div style={{ padding: '8px 14px', fontSize: 12, color: 'var(--text-muted)' }}>
              Không tìm thấy App khớp &ldquo;{appFilter}&rdquo;.
            </div>
          )}
        </nav>

        <div className="sidebar-footer">
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              fontSize: 11.5,
              color: 'var(--text-muted)',
              padding: '6px 10px',
              borderRadius: 999,
              backgroundColor: 'var(--bg-secondary)',
            }}
          >
            <span
              style={{
                width: 8,
                height: 8,
                borderRadius: '50%',
                backgroundColor: 'var(--success)',
                animation: 'pulse 2s infinite',
                flexShrink: 0,
              }}
            />
            {!collapsed && <span title={t('nav.systemReadyTitle')}>{t('nav.systemReady')}</span>}
          </div>
        </div>
      </aside>
    </>
  );
}
