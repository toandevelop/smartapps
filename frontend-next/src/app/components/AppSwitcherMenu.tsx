'use client';

import React, { useState, useRef, useEffect } from 'react';
import Link from 'next/link';
import AppsRoundedIcon from '@mui/icons-material/AppsRounded';
import DesignServicesRoundedIcon from '@mui/icons-material/DesignServicesRounded';
import { usePlatform } from '@/lib/app-context';
import { useAuth } from '@/lib/auth-context';
import ClusterIcon from './ui/ClusterIcon';

export default function AppSwitcherMenu() {
  const { apps } = usePlatform();
  const { canAccessApp, hasPermission } = useAuth();
  const visibleApps = apps.filter((a) => canAccessApp(a.code));
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onOutside = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', onOutside);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onOutside);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  return (
    <div ref={ref} style={{ position: 'relative' }}>
      <button
        type="button"
        onClick={() => setOpen((p) => !p)}
        title="Chuyển nhanh giữa các App công ty"
        aria-label="Chuyển đổi ứng dụng"
        aria-expanded={open}
        aria-haspopup="true"
        style={{
          width: 38,
          height: 38,
          borderRadius: '50%',
          border: 'none',
          backgroundColor: open ? '#e8f0fe' : 'transparent',
          color: open ? '#1a73e8' : '#5f6368',
          cursor: 'pointer',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <AppsRoundedIcon sx={{ fontSize: 22 }} />
      </button>

      {open && (
        <div
          style={{
            position: 'absolute',
            right: 0,
            top: 'calc(100% + 8px)',
            width: 300,
            maxWidth: 'calc(100vw - 24px)',
            backgroundColor: 'var(--bg-surface)',
            border: '1px solid var(--border-subtle)',
            borderRadius: 16,
            boxShadow: 'var(--shadow-lg)',
            padding: 16,
            zIndex: 1500,
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
            <span style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-primary)' }}>
              Ứng dụng Doanh nghiệp ({visibleApps.length})
            </span>
            {(hasPermission('studio.manage') || hasPermission('user.manage') || hasPermission('role.manage')) && (
              <Link
                href="/studio"
                onClick={() => setOpen(false)}
                style={{ fontSize: 12, fontWeight: 600, color: '#1a73e8', textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: 4 }}
              >
                <DesignServicesRoundedIcon sx={{ fontSize: 14 }} />
                App Studio
              </Link>
            )}
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 8 }}>
            {visibleApps.map((app) => (
              <Link
                key={app.id}
                href={`/apps/${app.code}`}
                title={app.name}
                onClick={() => setOpen(false)}
                style={{
                  minWidth: 0,
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  textAlign: 'center',
                  padding: '12px 8px',
                  borderRadius: 12,
                  border: '1px solid var(--border-light)',
                  backgroundColor: 'var(--bg-primary)',
                  textDecoration: 'none',
                  color: 'var(--text-primary)',
                  transition: 'background-color 0.15s ease, border-color 0.15s ease',
                }}
              >
                <div
                  style={{
                    width: 42,
                    height: 42,
                    borderRadius: 12,
                    backgroundColor: app.bgColor || '#e8f0fe',
                    color: app.color || '#1a73e8',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    marginBottom: 8,
                  }}
                >
                  <ClusterIcon icon={app.icon} fontSize={22} />
                </div>
                <span
                  style={{
                    fontSize: 12.5,
                    fontWeight: 600,
                    lineHeight: 1.25,
                    maxWidth: '100%',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {app.code}
                </span>
              </Link>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
