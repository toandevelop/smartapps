'use client';

import React, { useState, useEffect } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import DashboardRoundedIcon from '@mui/icons-material/DashboardRounded';
import DesignServicesRoundedIcon from '@mui/icons-material/DesignServicesRounded';
import AdminPanelSettingsRoundedIcon from '@mui/icons-material/AdminPanelSettingsRounded';
import ForumRoundedIcon from '@mui/icons-material/ForumRounded';
import NotificationsRoundedIcon from '@mui/icons-material/NotificationsRounded';
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded';
import ClusterIcon from './ui/ClusterIcon';
import { usePlatform } from '@/lib/app-context';
import { useAuth } from '@/lib/auth-context';
import { useSettings } from '@/lib/settings-context';

export default function Sidebar() {
  const pathname = usePathname() || '/';
  const { apps } = usePlatform();
  const { canAccessApp, hasPermission } = useAuth();
  const { unreadSummary, systemSettings } = useSettings();
  const [collapsed, setCollapsed] = useState(false);

  useEffect(() => {
    const saved = localStorage.getItem('smartapps_sidebar_collapsed') === 'true';
    setCollapsed(saved);
    const handler = (e: Event) => {
      const detail = (e as CustomEvent<{ collapsed: boolean }>).detail;
      if (detail) setCollapsed(detail.collapsed);
    };
    window.addEventListener('smartapps_sidebar_toggle', handler);
    return () => window.removeEventListener('smartapps_sidebar_toggle', handler);
  }, []);

  if (pathname === '/login' || pathname.startsWith('/forms/')) return null;

  const visibleApps = apps.filter((a) => canAccessApp(a.code));

  const renderBadge = (count: number, color = '#d93025') =>
    count > 0 && !collapsed ? (
      <span
        style={{
          marginLeft: 'auto',
          minWidth: 20,
          height: 20,
          padding: '0 6px',
          borderRadius: 999,
          backgroundColor: color,
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

  return (
    <aside className={`app-sidebar ${collapsed ? 'collapsed' : ''}`}>
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
        <div className="nav-section-label">Nền tảng</div>
        <Link href="/" className={`nav-link ${pathname === '/' ? 'active' : ''}`}>
          <DashboardRoundedIcon sx={{ fontSize: 20 }} />
          <span>Cổng Ứng dụng</span>
        </Link>
        {(hasPermission('studio.manage') || hasPermission('user.manage') || hasPermission('role.manage')) && (
          <Link href="/studio" className={`nav-link ${pathname.startsWith('/studio') ? 'active' : ''}`}>
            <DesignServicesRoundedIcon sx={{ fontSize: 20 }} />
            <span>App Builder Studio</span>
          </Link>
        )}
        {(hasPermission('user.manage') || hasPermission('role.manage') || hasPermission('workflow.manage') || hasPermission('automation.manage') || hasPermission('studio.manage')) && (
          <Link href="/admin" className={`nav-link ${pathname.startsWith('/admin') ? 'active' : ''}`}>
            <AdminPanelSettingsRoundedIcon sx={{ fontSize: 20 }} />
            <span>Quản trị &amp; Quy trình</span>
          </Link>
        )}

        <div className="nav-section-label">Giao tiếp &amp; Hệ thống</div>
        <Link href="/chat" className={`nav-link ${pathname.startsWith('/chat') ? 'active' : ''}`}>
          <ForumRoundedIcon sx={{ fontSize: 20 }} />
          <span>Chat Nội bộ</span>
          {renderBadge(unreadSummary.unreadChatCount, '#1a73e8')}
        </Link>
        <Link href="/notifications" className={`nav-link ${pathname.startsWith('/notifications') ? 'active' : ''}`}>
          <NotificationsRoundedIcon sx={{ fontSize: 20 }} />
          <span>Thông báo</span>
          {renderBadge(unreadSummary.unreadNotifCount, '#d93025')}
        </Link>
        <Link href="/settings" className={`nav-link ${pathname.startsWith('/settings') ? 'active' : ''}`}>
          <SettingsRoundedIcon sx={{ fontSize: 20 }} />
          <span>Cài đặt</span>
        </Link>

        <div className="nav-section-label">Ứng dụng Công ty ({visibleApps.length})</div>
        {visibleApps.map((app) => {
          const href = `/apps/${app.code}`;
          const active = pathname.toUpperCase().startsWith(`/APPS/${app.code.toUpperCase()}`);
          const isTasksApp = app.code.toUpperCase() === 'TASKS';
          return (
            <Link key={app.id} href={href} className={`nav-link ${active ? 'active' : ''}`}>
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
              {isTasksApp && renderBadge(unreadSummary.pendingTaskCount, '#ea8600')}
            </Link>
          );
        })}
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
          {!collapsed && <span title="Máy chủ dữ liệu đang hoạt động">Hệ thống Sẵn sàng</span>}
        </div>
      </div>
    </aside>
  );
}
