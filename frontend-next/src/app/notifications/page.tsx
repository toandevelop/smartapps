'use client';

import React, { useState, useEffect, useCallback } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import NotificationsRoundedIcon from '@mui/icons-material/NotificationsRounded';
import DoneAllRoundedIcon from '@mui/icons-material/DoneAllRounded';
import DeleteSweepRoundedIcon from '@mui/icons-material/DeleteSweepRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import CampaignRoundedIcon from '@mui/icons-material/CampaignRounded';
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded';
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded';
import { PageHeader, StatCard, NotificationSnackbar } from '@/app/components/ui';
import { useAuth } from '@/lib/auth-context';
import { useSettings } from '@/lib/settings-context';
import { useEscapeKey } from '@/lib/ui-hooks';
import { ChatUserSummary, SysNotification } from '@/lib/types';

const CATEGORY_MAP: Record<string, { label: string; color: string }> = {
  TASK: { label: 'Giao việc', color: '#ea8600' },
  CHAT: { label: 'Chat nội bộ', color: '#1a73e8' },
  MENTION: { label: 'Nhắc tên (@)', color: '#7b1fa2' },
  APPROVAL: { label: 'Phê duyệt', color: '#1e8e3e' },
  AUTOMATION: { label: 'Tự động hóa', color: '#0284c7' },
  SYSTEM: { label: 'Hệ thống', color: '#5f6368' },
};

export default function NotificationsPage() {
  const router = useRouter();
  const { user, hasPermission } = useAuth();
  const { refreshUnreadSummary } = useSettings();
  const isAdmin = hasPermission('studio.manage') || Boolean(user?.isAdmin) || user?.roleCode === 'ADMIN' || Boolean(user?.roleCodes?.includes('ADMIN'));

  const [items, setItems] = useState<SysNotification[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [users, setUsers] = useState<ChatUserSummary[]>([]);
  const [loading, setLoading] = useState(true);

  const [category, setCategory] = useState('ALL');
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [search, setSearch] = useState('');

  // Admin broadcast modal
  const [showBroadcast, setShowBroadcast] = useState(false);
  useEscapeKey(showBroadcast, () => setShowBroadcast(false));
  const [bRecipient, setBRecipient] = useState('*');
  const [bTitle, setBTitle] = useState('');
  const [bMessage, setBMessage] = useState('');
  const [bPriority, setBPriority] = useState('NORMAL');
  const [bLink, setBLink] = useState('');
  const [sendingBroadcast, setSendingBroadcast] = useState(false);

  const [snack, setSnack] = useState<{ open: boolean; message: string; severity: 'success' | 'error' }>({
    open: false,
    message: '',
    severity: 'success',
  });

  const loadNotifications = useCallback(async () => {
    setLoading(true);
    try {
      const qs = new URLSearchParams({ limit: '100' });
      if (category !== 'ALL') qs.set('category', category);
      if (unreadOnly) qs.set('unreadOnly', 'true');
      const res = await fetch(`/api/notifications?${qs.toString()}`, { credentials: 'include' });
      if (res.ok) {
        const j = await res.json();
        if (j.success && j.data) {
          setItems(j.data.items || []);
          setUnreadCount(j.data.unreadCount ?? 0);
        }
      }
    } finally {
      setLoading(false);
    }
  }, [category, unreadOnly]);

  useEffect(() => {
    if (!user) return;
    let cancelled = false;
    Promise.resolve().then(() => {
      if (!cancelled) void loadNotifications();
    });
    fetch('/api/chat/users', { credentials: 'include' })
      .then((r) => r.json())
      .then((j) => {
        if (!cancelled && j.success && Array.isArray(j.data)) setUsers(j.data);
      })
      .catch(() => null);
    return () => {
      cancelled = true;
    };
  }, [user, loadNotifications]);

  useEffect(() => {
    const onRt = (e: Event) => {
      const detail = (e as CustomEvent<{ type?: string }>).detail;
      if (detail?.type === 'notification') loadNotifications();
    };
    window.addEventListener('smartapps_realtime', onRt);
    return () => window.removeEventListener('smartapps_realtime', onRt);
  }, [loadNotifications]);

  const handleMarkRead = async (n: SysNotification, navigate = true) => {
    if (!n.isRead) {
      await fetch(`/api/notifications/${n.id}/read`, { method: 'PATCH', credentials: 'include' });
      setItems((prev) => prev.map((x) => (x.id === n.id ? { ...x, isRead: true } : x)));
      setUnreadCount((c) => Math.max(0, c - 1));
      refreshUnreadSummary();
    }
    if (navigate) {
      if (n.linkUrl) router.push(n.linkUrl);
      else if (n.appCode) router.push(`/apps/${n.appCode}`);
    }
  };

  const handleMarkAllRead = async () => {
    await fetch('/api/notifications/read-all', { method: 'POST', credentials: 'include' });
    setItems((prev) => prev.map((x) => ({ ...x, isRead: true })));
    setUnreadCount(0);
    refreshUnreadSummary();
    setSnack({ open: true, message: 'Đã đánh dấu đọc tất cả thông báo.', severity: 'success' });
  };

  const handleClearRead = async () => {
    const res = await fetch('/api/notifications/clear-read', { method: 'POST', credentials: 'include' }).then((r) => r.json());
    await loadNotifications();
    refreshUnreadSummary();
    setSnack({ open: true, message: `Đã dọn ${res.removed || 0} thông báo cá nhân đã đọc.`, severity: 'success' });
  };

  const handleDelete = async (id: string) => {
    const res = await fetch(`/api/notifications/${id}`, { method: 'DELETE', credentials: 'include' }).then((r) => r.json());
    if (res.success) {
      setItems((prev) => prev.filter((x) => x.id !== id));
      refreshUnreadSummary();
    } else {
      setSnack({ open: true, message: res.message || 'Không thể xóa thông báo này', severity: 'error' });
    }
  };

  const handleSendBroadcast = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!bTitle.trim()) return;
    setSendingBroadcast(true);
    try {
      const res = await fetch('/api/notifications/broadcast', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          recipientUsername: bRecipient,
          title: bTitle.trim(),
          message: bMessage.trim(),
          category: 'SYSTEM',
          priority: bPriority,
          linkUrl: bLink.trim() || null,
        }),
      }).then((r) => r.json());
      if (res.success) {
        setShowBroadcast(false);
        setBTitle('');
        setBMessage('');
        setBLink('');
        await loadNotifications();
        refreshUnreadSummary();
        setSnack({ open: true, message: 'Đã phát thông báo thành công!', severity: 'success' });
      }
    } finally {
      setSendingBroadcast(false);
    }
  };

  const visibleItems = items.filter(
    (n) =>
      !search.trim() ||
      n.title.toLowerCase().includes(search.toLowerCase()) ||
      (n.message || '').toLowerCase().includes(search.toLowerCase())
  );

  const taskNotifs = items.filter((n) => n.category === 'TASK').length;
  const chatNotifs = items.filter((n) => n.category === 'CHAT' || n.category === 'MENTION').length;
  const approvalNotifs = items.filter((n) => n.category === 'APPROVAL').length;

  return (
    <div>
      <PageHeader
        title="Trung tâm Thông báo"
        subtitle="Tổng hợp thông báo Giao việc, Chat nội bộ, Phê duyệt hồ sơ & Tin hệ thống theo thời gian thực"
        icon={<NotificationsRoundedIcon sx={{ fontSize: 24 }} />}
        actions={
          <>
            <Link href="/settings?tab=channels" className="btn btn-outline btn-sm">
              Kênh Gmail, Zalo &amp; Telegram
            </Link>
            <Link href="/settings?tab=notifications" className="btn btn-outline btn-sm">
              <SettingsRoundedIcon sx={{ fontSize: 16 }} />
              Cài đặt Thông báo
            </Link>
            {isAdmin && (
              <button type="button" className="btn btn-primary btn-sm" onClick={() => setShowBroadcast(true)}>
                <CampaignRoundedIcon sx={{ fontSize: 16 }} />
                Phát thông báo
              </button>
            )}
          </>
        }
      />

      <div className="page-content">
        <div className="stats-grid">
          <StatCard label="Chưa đọc" value={unreadCount} subtext="Cần bạn xem qua" icon={<NotificationsRoundedIcon />} color="red" />
          <StatCard label="Giao việc (Task)" value={taskNotifs} subtext="Công việc mới & cập nhật" icon={<NotificationsRoundedIcon />} color="yellow" />
          <StatCard label="Chat & Nhắc tên" value={chatNotifs} subtext="Tin nhắn trực tiếp & @mention" icon={<NotificationsRoundedIcon />} color="blue" />
          <StatCard label="Phê duyệt" value={approvalNotifs} subtext="Trình duyệt & kết quả duyệt" icon={<NotificationsRoundedIcon />} color="green" />
        </div>

        <div
          className="card"
          style={{
            padding: '12px 16px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 10,
            flexWrap: 'wrap',
          }}
        >
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {(
              [
                ['ALL', 'Tất cả'],
                ['TASK', 'Giao việc'],
                ['CHAT', 'Chat'],
                ['MENTION', 'Nhắc tên (@)'],
                ['APPROVAL', 'Phê duyệt'],
                ['SYSTEM', 'Hệ thống'],
              ] as const
            ).map(([k, lbl]) => (
              <button
                key={k}
                type="button"
                className={`btn btn-xs ${category === k ? 'btn-primary' : 'btn-outline'}`}
                onClick={() => setCategory(k)}
              >
                {lbl}
              </button>
            ))}
            <button
              type="button"
              className={`btn btn-xs ${unreadOnly ? 'btn-primary' : 'btn-outline'}`}
              onClick={() => setUnreadOnly((p) => !p)}
            >
              Chỉ chưa đọc
            </button>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <input
              className="form-input"
              style={{ width: 210, height: 32, fontSize: 12.5 }}
              placeholder="Tìm nội dung thông báo..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            {unreadCount > 0 && (
              <button type="button" className="btn btn-outline btn-xs" onClick={handleMarkAllRead}>
                <DoneAllRoundedIcon sx={{ fontSize: 14 }} />
                Đọc tất cả
              </button>
            )}
            <button type="button" className="btn btn-outline btn-xs" onClick={handleClearRead}>
              <DeleteSweepRoundedIcon sx={{ fontSize: 14 }} />
              Dọn đã đọc
            </button>
          </div>
        </div>

        <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
          {loading ? (
            <div style={{ padding: 40, textAlign: 'center', color: 'var(--text-muted)' }}>Đang tải thông báo...</div>
          ) : visibleItems.length === 0 ? (
            <div style={{ padding: 40, textAlign: 'center', color: 'var(--text-muted)' }}>
              Không có thông báo nào phù hợp bộ lọc.
            </div>
          ) : (
            visibleItems.map((n) => {
              const cat = CATEGORY_MAP[n.category || 'SYSTEM'] || CATEGORY_MAP.SYSTEM;
              return (
                <div
                  key={n.id}
                  style={{
                    padding: '14px 18px',
                    borderBottom: '1px solid var(--border-light)',
                    backgroundColor: n.isRead ? 'var(--bg-surface)' : 'var(--accent-primary-container)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    gap: 12,
                  }}
                >
                  <div style={{ flex: 1, minWidth: 0, cursor: 'pointer' }} onClick={() => handleMarkRead(n, true)}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                      {!n.isRead && (
                        <span style={{ width: 8, height: 8, borderRadius: '50%', backgroundColor: 'var(--accent-primary)', flexShrink: 0 }} />
                      )}
                      <strong style={{ fontSize: 13.5, color: 'var(--text-primary)' }}>{n.title}</strong>
                      <span className="badge" style={{ backgroundColor: `${cat.color}18`, color: cat.color, fontSize: 10.5 }}>
                        {cat.label}
                      </span>
                      {n.appCode && <span className="badge badge-info">{n.appCode}</span>}
                      {n.priority === 'HIGH' && <span className="badge badge-danger">Quan trọng</span>}
                    </div>
                    {n.message && (
                      <div style={{ fontSize: 12.5, color: 'var(--text-secondary)', marginTop: 4 }}>{n.message}</div>
                    )}
                    <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 4 }}>
                      {new Date(n.createdAt).toLocaleString('vi-VN')}
                      {n.senderUsername && ` • Gửi bởi @${n.senderUsername}`}
                    </div>
                  </div>

                  <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexShrink: 0 }}>
                    {(n.linkUrl || n.appCode) && (
                      <button type="button" className="btn btn-outline btn-xs" onClick={() => handleMarkRead(n, true)}>
                        Mở <OpenInNewRoundedIcon sx={{ fontSize: 13 }} />
                      </button>
                    )}
                    {!n.isRead && (
                      <button type="button" className="btn btn-outline btn-xs" onClick={() => handleMarkRead(n, false)}>
                        Đã đọc
                      </button>
                    )}
                    {(n.recipientUsername !== '*' || isAdmin) && (
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        style={{ color: '#d93025' }}
                        aria-label={`Xóa thông báo ${n.title}`}
                        title="Xóa thông báo"
                        onClick={() => handleDelete(n.id)}
                      >
                        <DeleteOutlineRoundedIcon sx={{ fontSize: 14 }} />
                      </button>
                    )}
                  </div>
                </div>
              );
            })
          )}
        </div>
      </div>

      {showBroadcast && (
        <div className="modal-overlay" onClick={() => setShowBroadcast(false)}>
          <div className="modal-dialog" style={{ maxWidth: 480 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16.5, fontWeight: 700, marginBottom: 12 }}>Phát thông báo hệ thống</h3>
            <form onSubmit={handleSendBroadcast} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                <div>
                  <label className="form-label">Người nhận</label>
                  <select className="form-select" value={bRecipient} onChange={(e) => setBRecipient(e.target.value)}>
                    <option value="*">Toàn công ty (*)</option>
                    {users.map((u) => (
                      <option key={u.username} value={u.username}>
                        {u.fullName} (@{u.username})
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="form-label">Mức độ</label>
                  <select className="form-select" value={bPriority} onChange={(e) => setBPriority(e.target.value)}>
                    <option value="NORMAL">Bình thường</option>
                    <option value="HIGH">Quan trọng</option>
                  </select>
                </div>
              </div>
              <div>
                <label className="form-label">Tiêu đề *</label>
                <input
                  className="form-input"
                  placeholder="Ví dụ: Lịch bảo trì hệ thống tối thứ 7"
                  value={bTitle}
                  onChange={(e) => setBTitle(e.target.value)}
                  required
                  autoFocus
                />
              </div>
              <div>
                <label className="form-label">Nội dung chi tiết</label>
                <textarea
                  className="form-textarea"
                  rows={3}
                  placeholder="Nhập nội dung thông báo..."
                  value={bMessage}
                  onChange={(e) => setBMessage(e.target.value)}
                />
              </div>
              <div>
                <label className="form-label">Đường dẫn chuyển tới khi bấm (tùy chọn)</label>
                <input
                  className="form-input"
                  placeholder="/chat hoặc /apps/OPS"
                  value={bLink}
                  onChange={(e) => setBLink(e.target.value)}
                />
              </div>
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowBroadcast(false)}>
                  Hủy
                </button>
                <button type="submit" disabled={sendingBroadcast} className="btn btn-primary btn-sm">
                  {sendingBroadcast ? 'Đang gửi...' : 'Gửi thông báo'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <NotificationSnackbar
        open={snack.open}
        message={snack.message}
        severity={snack.severity}
        onClose={() => setSnack((s) => ({ ...s, open: false }))}
      />
    </div>
  );
}
