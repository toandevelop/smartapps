'use client';

import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import AppsRoundedIcon from '@mui/icons-material/AppsRounded';
import DescriptionRoundedIcon from '@mui/icons-material/DescriptionRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import ChatBubbleOutlineRoundedIcon from '@mui/icons-material/ChatBubbleOutlineRounded';
import AttachFileRoundedIcon from '@mui/icons-material/AttachFileRounded';
import { usePlatform } from '@/lib/app-context';
import { useAuth } from '@/lib/auth-context';
import { useSettings } from '@/lib/settings-context';
import { useEscapeKey } from '@/lib/ui-hooks';
import type { GlobalSearchResult } from '@/lib/types';

interface PaletteItem {
  id: string;
  group: string;
  title: string;
  subtitle?: string;
  href?: string;
  externalHref?: string;
  onAction?: () => void;
  kind: 'app' | 'nav' | 'record' | 'chat' | 'attachment' | 'action';
}

export default function CommandPaletteModal({
  open,
  onClose,
}: {
  open: boolean;
  onClose: () => void;
}) {
  useEscapeKey(open, onClose);
  if (!open) return null;
  return <CommandPaletteBody key="open" onClose={onClose} />;
}

function CommandPaletteBody({ onClose }: { onClose: () => void }) {
  const router = useRouter();
  const { apps } = usePlatform();
  const { canAccessApp, hasPermission } = useAuth();
  const { userSettings, densityMode, setDensityMode, saveUserSettings, t } = useSettings();
  const [query, setQuery] = useState('');
  const [activeIdx, setActiveIdx] = useState(0);
  const [recordHits, setRecordHits] = useState<PaletteItem[]>([]);
  const [searching, setSearching] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const timer = setTimeout(() => inputRef.current?.focus(), 30);
    return () => clearTimeout(timer);
  }, []);

  useEffect(() => {
    const q = query.trim();
    if (q.length < 2) return;
    let cancelled = false;
    const timer = setTimeout(async () => {
      setSearching(true);
      try {
        const res = await fetch(`/api/records/global-search?q=${encodeURIComponent(q)}&limit=6`, { credentials: 'include' }).then((r) => r.json());
        if (cancelled) return;
        if (res.success && res.data) {
          const d = res.data as GlobalSearchResult;
          const mapped: PaletteItem[] = [
            ...(d.records || []).map((r) => ({
              id: `rec_${r.id}`,
              group: 'Hồ sơ khớp từ khóa',
              title: r.title,
              subtitle: r.subtitle,
              href: r.url,
              kind: 'record' as const,
            })),
            ...(d.chats || []).map((c) => ({
              id: `chat_${c.id}`,
              group: 'Tin nhắn Chat',
              title: c.title,
              subtitle: c.subtitle,
              href: c.url,
              kind: 'chat' as const,
            })),
            ...(d.attachments || []).map((a) => ({
              id: `att_${a.id}`,
              group: 'Tệp đính kèm',
              title: a.title,
              subtitle: a.subtitle,
              externalHref: a.url,
              kind: 'attachment' as const,
            })),
          ];
          if (!cancelled) setRecordHits(mapped);
        }
      } catch {
        if (!cancelled) setRecordHits([]);
      } finally {
        if (!cancelled) setSearching(false);
      }
    }, 220);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [query]);

  const items = useMemo<PaletteItem[]>(() => {
    const q = query.trim().toLowerCase();
    const activeRecordHits = q.length < 2 ? [] : recordHits;
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
      { id: 'nav_store', group: 'Điều hướng nhanh', title: 'Kho Apps Online (Marketplace)', subtitle: '/store', href: '/store', kind: 'nav' },
      { id: 'nav_tasks', group: 'Điều hướng nhanh', title: 'Quản lý Công việc', subtitle: '/tasks', href: '/tasks', kind: 'nav' },
      { id: 'nav_chat', group: 'Điều hướng nhanh', title: 'Trò chuyện nội bộ', subtitle: '/chat', href: '/chat', kind: 'nav' },
      { id: 'nav_notif', group: 'Điều hướng nhanh', title: 'Trung tâm Thông báo', subtitle: '/notifications', href: '/notifications', kind: 'nav' },
      { id: 'nav_settings', group: 'Điều hướng nhanh', title: 'Cài đặt cá nhân', subtitle: '/settings', href: '/settings', kind: 'nav' },
      ...(hasPermission('studio.manage')
        ? [{ id: 'nav_studio', group: 'Điều hướng nhanh', title: 'App Builder Studio', subtitle: '/studio', href: '/studio', kind: 'nav' as const }]
        : []),
      ...(hasPermission('user.manage') || hasPermission('role.manage') || hasPermission('workflow.manage') || hasPermission('audit.view')
        ? [
            { id: 'nav_admin', group: 'Điều hướng nhanh', title: 'Quản trị & Quy trình', subtitle: '/admin', href: '/admin', kind: 'nav' as const },
            { id: 'nav_audit', group: 'Điều hướng nhanh', title: 'Nhật ký Kiểm toán Toàn hệ thống', subtitle: '/admin?tab=audit', href: '/admin?tab=audit', kind: 'nav' as const },
          ]
        : []),
    ];

    const nextDensity = densityMode === 'compact' ? 'comfortable' : densityMode === 'comfortable' ? 'spacious' : 'compact';
    const nextDensityLabel =
      nextDensity === 'compact' ? 'Gọn (Compact)' : nextDensity === 'spacious' ? 'Thoải mái (Spacious)' : 'Tiêu chuẩn (Comfortable)';

    const actionItems: PaletteItem[] = [
      {
        id: 'act_ai_copilot',
        group: 'Hành động nhanh',
        title: 'Mở Trợ lý AI Doanh nghiệp (SmartApps Copilot • Alt+A)',
        subtitle: 'ai copilot assistant trợ lý hỏi đáp tìm kiếm đề xuất',
        kind: 'action',
        onAction: () => {
          window.dispatchEvent(new CustomEvent('smartapps_open_copilot'));
        },
      },
      {
        id: 'act_theme',
        group: 'Hành động nhanh',
        title: userSettings.themeMode === 'dark' ? 'Chuyển sang Giao diện Sáng (Light Mode)' : 'Chuyển sang Giao diện Tối (Dark Mode)',
        subtitle: 'theme dark light sáng tối',
        kind: 'action',
        onAction: () => {
          const nextMode = userSettings.themeMode === 'dark' ? 'light' : 'dark';
          void saveUserSettings({ ...userSettings, themeMode: nextMode });
        },
      },
      {
        id: 'act_density',
        group: 'Hành động nhanh',
        title: `Đổi mật độ hiển thị sang ${nextDensityLabel}`,
        subtitle: 'compact comfortable spacious density gọn thoải mái',
        kind: 'action',
        onAction: () => {
          void setDensityMode(nextDensity);
        },
      },
    ];

    const staticFiltered = [...appItems, ...actionItems, ...navItems].filter(
      (it) => !q || it.title.toLowerCase().includes(q) || (it.subtitle || '').toLowerCase().includes(q),
    );
    return [...activeRecordHits, ...staticFiltered].slice(0, 20);
  }, [apps, canAccessApp, hasPermission, query, recordHits, userSettings, densityMode, setDensityMode, saveUserSettings]);

  const safeIdx = items.length > 0 ? Math.min(activeIdx, items.length - 1) : 0;

  const executeItem = (item: PaletteItem) => {
    onClose();
    if (item.onAction) {
      item.onAction();
      return;
    }
    if (item.externalHref) {
      window.open(item.externalHref, '_blank', 'noopener,noreferrer');
      return;
    }
    if (item.href) {
      router.push(item.href);
    }
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
              } else if (e.key === 'Enter' && items[safeIdx]) {
                e.preventDefault();
                executeItem(items[safeIdx]);
              }
            }}
            placeholder={t('cmd.placeholder')}
            style={{
              flex: 1,
              border: 'none',
              outline: 'none',
              fontSize: 14.5,
              backgroundColor: 'transparent',
              color: 'var(--text-primary)',
            }}
          />
          {searching && <span style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>{t('cmd.searching')}</span>}
          <kbd style={{ fontSize: 11, padding: '2px 6px', borderRadius: 4, border: '1px solid var(--border-subtle)', color: 'var(--text-muted)' }}>ESC</kbd>
        </div>

        <div style={{ maxHeight: 400, overflowY: 'auto', padding: 8 }}>
          {items.length === 0 ? (
            <div style={{ padding: 24, textAlign: 'center', fontSize: 13, color: 'var(--text-muted)' }}>
              {t('cmd.empty')}
            </div>
          ) : (
            items.map((item, idx) => {
              const selected = idx === safeIdx;
              return (
                <button
                  key={item.id}
                  type="button"
                  onMouseEnter={() => setActiveIdx(idx)}
                  onClick={() => executeItem(item)}
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
                  ) : item.kind === 'chat' ? (
                    <ChatBubbleOutlineRoundedIcon sx={{ fontSize: 18, color: '#1a73e8' }} />
                  ) : item.kind === 'attachment' ? (
                    <AttachFileRoundedIcon sx={{ fontSize: 18, color: '#b06000' }} />
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
