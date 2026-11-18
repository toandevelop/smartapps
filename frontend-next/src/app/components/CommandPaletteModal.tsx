'use client';

import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import AppsRoundedIcon from '@mui/icons-material/AppsRounded';
import DescriptionRoundedIcon from '@mui/icons-material/DescriptionRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import { usePlatform } from '@/lib/app-context';
import { useAuth } from '@/lib/auth-context';
import { useEscapeKey } from '@/lib/ui-hooks';

interface PaletteItem {
  id: string;
  group: string;
  title: string;
  subtitle?: string;
  href: string;
  kind: 'app' | 'nav' | 'record';
}

export default function CommandPaletteModal({
  open,
  onClose,
}: {
  open: boolean;
  onClose: () => void;
}) {
  const router = useRouter();
  const { apps } = usePlatform();
  const { canAccessApp, hasPermission } = useAuth();
  const [query, setQuery] = useState('');
  const [activeIdx, setActiveIdx] = useState(0);
  const [recordHits, setRecordHits] = useState<PaletteItem[]>([]);
  const [searching, setSearching] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  useEscapeKey(open, onClose);

  useEffect(() => {
    if (open) {
      setQuery('');
      setActiveIdx(0);
      setRecordHits([]);
      setTimeout(() => inputRef.current?.focus(), 30);
    }
  }, [open]);

  useEffect(() => {
    const q = query.trim();
    if (!open || q.length < 2) {
      setRecordHits([]);
      return;
    }
    const timer = setTimeout(async () => {
      setSearching(true);
      try {
        const res = await fetch(`/api/records?search=${encodeURIComponent(q)}&limit=8`, { credentials: 'include' }).then((r) => r.json());
        if (res.success && Array.isArray(res.data?.items)) {
          setRecordHits(
            res.data.items.map((r: any) => ({
              id: `rec_${r.id}`,
              group: 'Hồ sơ khớp từ khóa',
              title: r.recordKey || r.id,
              subtitle: Object.values(r.data || {}).filter((v) => v !== null && v !== undefined && typeof v !== 'object').slice(0, 3).join(' • '),
              href: `/apps/${encodeURIComponent(r.appId)}/records/${encodeURIComponent(r.id)}`,
              kind: 'record' as const,
            })),
          );
        }
      } catch {
        setRecordHits([]);
      } finally {
        setSearching(false);
      }
    }, 220);
    return () => clearTimeout(timer);
  }, [open, query]);

  const items = useMemo<PaletteItem[]>(() => {
    const q = query.trim().toLowerCase();
    const appItems: PaletteItem[] = apps
      .filter((a) => canAccessApp(a.code))
      .map((a) => ({
        id: `app_${a.id}`,
        group: 'Ứng dụng',
        title: `${a.name} (${a.code})`,
        subtitle: a.department || `${a.tableCount} bảng • ${a.recordCount} hồ sơ`,
        href: `/apps/${encodeURIComponent(a.code)}`,
        kind: 'app',
      }));

    const navItems: PaletteItem[] = [
      { id: 'nav_home', group: 'Điều hướng nhanh', title: 'Cổng Ứng dụng & Điều hành', subtitle: '/', href: '/', kind: 'nav' },
      { id: 'nav_tasks', group: 'Điều hướng nhanh', title: 'Quản lý Công việc', subtitle: '/tasks', href: '/tasks', kind: 'nav' },
      { id: 'nav_chat', group: 'Điều hướng nhanh', title: 'Trò chuyện nội bộ', subtitle: '/chat', href: '/chat', kind: 'nav' },
      { id: 'nav_notif', group: 'Điều hướng nhanh', title: 'Trung tâm Thông báo', subtitle: '/notifications', href: '/notifications', kind: 'nav' },
      { id: 'nav_settings', group: 'Điều hướng nhanh', title: 'Cài đặt cá nhân', subtitle: '/settings', href: '/settings', kind: 'nav' },
      ...(hasPermission('studio.manage')
        ? [{ id: 'nav_studio', group: 'Điều hướng nhanh', title: 'App Builder Studio', subtitle: '/studio', href: '/studio', kind: 'nav' as const }]
        : []),
    ];

    const staticFiltered = [...appItems, ...navItems].filter(
      (it) => !q || it.title.toLowerCase().includes(q) || (it.subtitle || '').toLowerCase().includes(q),
    );
    return [...recordHits, ...staticFiltered].slice(0, 16);
  }, [apps, canAccessApp, hasPermission, query, recordHits]);

  useEffect(() => {
    setActiveIdx(0);
  }, [items.length]);

  if (!open) return null;

  const navigateTo = (href: string) => {
    onClose();
    router.push(href);
  };

  return (
    <div className="modal-overlay" style={{ alignItems: 'flex-start', paddingTop: '10vh' }} onClick={onClose}>
      <div
        className="modal-dialog"
        style={{ maxWidth: 620, padding: 0, overflow: 'hidden' }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '12px 16px', borderBottom: '1px solid var(--border-light)' }}>
          <SearchRoundedIcon sx={{ color: 'var(--text-muted)', fontSize: 20 }} />
          <input
            ref={inputRef}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'ArrowDown') {
                e.preventDefault();
                setActiveIdx((i) => (items.length ? (i + 1) % items.length : 0));
              } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                setActiveIdx((i) => (items.length ? (i - 1 + items.length) % items.length : 0));
              } else if (e.key === 'Enter' && items[activeIdx]) {
                e.preventDefault();
                navigateTo(items[activeIdx].href);
              }
            }}
            placeholder="Tìm nhanh Ứng dụng, Hồ sơ (mã/từ khóa), hoặc Trang..."
            style={{
              flex: 1,
              border: 'none',
              outline: 'none',
              fontSize: 14.5,
              backgroundColor: 'transparent',
              color: 'var(--text-primary)',
            }}
          />
          {searching && <span style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>Đang tìm...</span>}
          <kbd style={{ fontSize: 11, padding: '2px 6px', borderRadius: 4, border: '1px solid var(--border-subtle)', color: 'var(--text-muted)' }}>ESC</kbd>
        </div>

        <div style={{ maxHeight: 400, overflowY: 'auto', padding: 8 }}>
          {items.length === 0 ? (
            <div style={{ padding: 24, textAlign: 'center', fontSize: 13, color: 'var(--text-muted)' }}>
              Không tìm thấy kết quả phù hợp.
            </div>
          ) : (
            items.map((item, idx) => {
              const selected = idx === activeIdx;
              return (
                <button
                  key={item.id}
                  type="button"
                  onMouseEnter={() => setActiveIdx(idx)}
                  onClick={() => navigateTo(item.href)}
                  style={{
                    width: '100%',
                    display: 'flex',
                    alignItems: 'center',
                    gap: 10,
                    padding: '9px 12px',
                    borderRadius: 8,
                    border: 'none',
                    textAlign: 'left',
                    cursor: 'pointer',
                    backgroundColor: selected ? 'var(--accent-primary-container)' : 'transparent',
                    color: 'var(--text-primary)',
                  }}
                >
                  {item.kind === 'app' ? (
                    <AppsRoundedIcon sx={{ fontSize: 18, color: 'var(--accent-primary)' }} />
                  ) : item.kind === 'record' ? (
                    <DescriptionRoundedIcon sx={{ fontSize: 18, color: '#137333' }} />
                  ) : (
                    <BoltRoundedIcon sx={{ fontSize: 18, color: 'var(--text-muted)' }} />
                  )}
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ fontSize: 13.5, fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                      {item.title}
                    </div>
                    {item.subtitle && (
                      <div style={{ fontSize: 11.5, color: 'var(--text-muted)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        {item.subtitle}
                      </div>
                    )}
                  </div>
                  <span style={{ fontSize: 10.5, color: 'var(--text-muted)', flexShrink: 0 }}>{item.group}</span>
                </button>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
}
