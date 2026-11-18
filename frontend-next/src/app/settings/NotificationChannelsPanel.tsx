'use client';

import React, { useState, useEffect, useCallback } from 'react';
import MailRoundedIcon from '@mui/icons-material/MailRounded';
import SendRoundedIcon from '@mui/icons-material/SendRounded';
import TelegramIcon from '@mui/icons-material/Telegram';
import ChatBubbleRoundedIcon from '@mui/icons-material/ChatBubbleRounded';
import WebhookRoundedIcon from '@mui/icons-material/WebhookRounded';
import GroupsRoundedIcon from '@mui/icons-material/GroupsRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import ExpandLessRoundedIcon from '@mui/icons-material/ExpandLessRounded';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import { BooleanSwitch, StatusBadge } from '@/app/components/ui';
import { NotificationChannel, NotificationDeliveryLog, SysUser, UserSettings } from '@/lib/types';

const EVENT_CATEGORY_OPTIONS = [
  { code: 'ALL', label: 'Tất cả sự kiện' },
  { code: 'TASK', label: 'Giao việc' },
  { code: 'APPROVAL', label: 'Phê duyệt' },
  { code: 'MENTION', label: 'Nhắc tên (@)' },
  { code: 'AUTOMATION', label: 'Tự động hóa' },
  { code: 'CHAT', label: 'Chat nội bộ' },
  { code: 'SYSTEM', label: 'Hệ thống' },
];

const CHANNEL_META: Record<string, { label: string; color: string; bg: string }> = {
  GMAIL_SMTP: { label: 'Gmail / SMTP', color: '#ea4335', bg: '#fce8e6' },
  TELEGRAM: { label: 'Telegram', color: '#0088cc', bg: '#e1f5fe' },
  ZALO: { label: 'Zalo / OA', color: '#0068ff', bg: '#e8f0fe' },
  TEAMS: { label: 'MS Teams', color: '#6264a7', bg: '#ede7f6' },
  SLACK: { label: 'Slack', color: '#4a154b', bg: '#f3e5f5' },
  WEBHOOK: { label: 'Webhook', color: '#00897b', bg: '#e0f2f1' },
};

export default function NotificationChannelsPanel({
  user,
  isAdmin,
  prefs,
  setPrefs,
  onSavePrefs,
  savingPrefs,
  onToast,
}: {
  user: SysUser | null;
  isAdmin: boolean;
  prefs: UserSettings;
  setPrefs: React.Dispatch<React.SetStateAction<UserSettings>>;
  onSavePrefs: (next: UserSettings) => Promise<void>;
  savingPrefs: boolean;
  onToast: (msg: string, severity?: 'success' | 'error' | 'info') => void;
}) {
  const [channels, setChannels] = useState<NotificationChannel[]>([]);
  const [logs, setLogs] = useState<NotificationDeliveryLog[]>([]);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [testingId, setTestingId] = useState<string | null>(null);

  const loadAll = useCallback(async () => {
    setLoading(true);
    try {
      const [cRes, lRes] = await Promise.all([
        fetch('/api/settings/notification-channels', { credentials: 'include' }).then((r) => r.json()),
        fetch('/api/settings/notification-channels/logs?limit=15', { credentials: 'include' }).then((r) => r.json()),
      ]);
      if (cRes.success && Array.isArray(cRes.data)) {
        setChannels(cRes.data);
        setExpandedId((prev) => (prev && cRes.data.some((c: NotificationChannel) => c.id === prev) ? prev : cRes.data[0]?.id || null));
      }
      if (lRes.success && Array.isArray(lRes.data)) setLogs(lRes.data);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  const updateChannelLocal = (id: string, patch: Partial<NotificationChannel>) => {
    setChannels((prev) => prev.map((c) => (c.id === id ? { ...c, ...patch } : c)));
  };

  const updateChannelConfig = (id: string, key: string, value: any) => {
    setChannels((prev) =>
      prev.map((c) => (c.id === id ? { ...c, config: { ...c.config, [key]: value } } : c))
    );
  };

  const toggleCategory = (ch: NotificationChannel, catCode: string) => {
    let next = [...(ch.eventCategories || [])];
    if (catCode === 'ALL') {
      next = ['ALL'];
    } else {
      next = next.filter((x) => x !== 'ALL');
      if (next.includes(catCode)) next = next.filter((x) => x !== catCode);
      else next.push(catCode);
      if (next.length === 0) next = ['ALL'];
    }
    updateChannelLocal(ch.id, { eventCategories: next });
  };

  const handleSaveChannel = async (ch: NotificationChannel) => {
    setSavingId(ch.id);
    try {
      const res = await fetch('/api/settings/notification-channels', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          id: ch.id,
          ownerScope: ch.ownerScope,
          channelType: ch.channelType,
          name: ch.name,
          isEnabled: ch.isEnabled,
          eventCategories: ch.eventCategories,
          config: ch.config,
        }),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setChannels((prev) => prev.map((x) => (x.id === ch.id ? res.data : x)));
        onToast(`Đã lưu kênh "${res.data.name}"!`);
      } else {
        onToast(res.message || 'Lỗi lưu kênh thông báo', 'error');
      }
    } finally {
      setSavingId(null);
    }
  };

  const handleTestChannel = async (ch: NotificationChannel) => {
    setTestingId(ch.id);
    try {
      const res = await fetch(`/api/settings/notification-channels/${encodeURIComponent(ch.id)}/test`, {
        method: 'POST',
        credentials: 'include',
      }).then((r) => r.json());
      onToast(res.message || (res.success ? 'Gửi thử thành công!' : 'Gửi thử thất bại'), res.success ? 'success' : 'error');
      await loadAll();
    } finally {
      setTestingId(null);
    }
  };

  const handleDeleteChannel = async (ch: NotificationChannel) => {
    const res = await fetch(`/api/settings/notification-channels/${encodeURIComponent(ch.id)}`, {
      method: 'DELETE',
      credentials: 'include',
    }).then((r) => r.json());
    if (res.success) {
      setChannels((prev) => prev.filter((x) => x.id !== ch.id));
      onToast(`Đã xóa kênh "${ch.name}"`);
    } else {
      onToast(res.message || 'Không thể xóa kênh', 'error');
    }
  };

  const handleAddChannel = async (channelType: string, scope: 'SYSTEM' | 'USER') => {
    const defaults: Record<string, { name: string; config: Record<string, any> }> = {
      GMAIL_SMTP: {
        name: scope === 'SYSTEM' ? 'Gmail SMTP Nhóm mới' : 'Gmail SMTP Cá nhân',
        config: { smtpHost: 'smtp.gmail.com', smtpPort: 587, useSsl: true, username: '', password: '', fromName: 'SmartApps', toEmails: user?.email || '', useRecipientEmail: false },
      },
      TELEGRAM: {
        name: scope === 'SYSTEM' ? 'Nhóm Telegram mới' : 'Telegram Cá nhân',
        config: { botToken: '', chatId: '' },
      },
      ZALO: {
        name: scope === 'SYSTEM' ? 'Nhóm Zalo / Zalo OA mới' : 'Zalo Cá nhân',
        config: { zaloMode: 'WEBHOOK', webhookUrl: '', secretHeader: '', endpointUrl: 'https://openapi.zalo.me/v3.0/oa/message/cs', accessToken: '', targetId: '' },
      },
      TEAMS: { name: 'Kênh Microsoft Teams', config: { webhookUrl: '' } },
      SLACK: { name: 'Kênh Slack', config: { webhookUrl: '' } },
      WEBHOOK: { name: 'Custom Webhook JSON', config: { webhookUrl: '', secretValue: '' } },
    };
    const preset = defaults[channelType] || defaults.WEBHOOK;
    const res = await fetch('/api/settings/notification-channels', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({
        ownerScope: scope,
        channelType,
        name: preset.name,
        isEnabled: false,
        eventCategories: ['TASK', 'APPROVAL'],
        config: preset.config,
      }),
    }).then((r) => r.json());
    if (res.success && res.data) {
      setChannels((prev) => [...prev, res.data]);
      setExpandedId(res.data.id);
      onToast(`Đã thêm "${res.data.name}". Hãy điền cấu hình và nhấn Lưu!`);
    } else {
      onToast(res.message || 'Không thể tạo kênh mới', 'error');
    }
  };

  const renderChannelIcon = (type: string) => {
    const c = CHANNEL_META[type]?.color || '#1a73e8';
    if (type === 'GMAIL_SMTP') return <MailRoundedIcon sx={{ fontSize: 19, color: c }} />;
    if (type === 'TELEGRAM') return <TelegramIcon sx={{ fontSize: 19, color: c }} />;
    if (type === 'ZALO') return <ChatBubbleRoundedIcon sx={{ fontSize: 18, color: c }} />;
    if (type === 'TEAMS' || type === 'SLACK') return <GroupsRoundedIcon sx={{ fontSize: 19, color: c }} />;
    return <WebhookRoundedIcon sx={{ fontSize: 19, color: c }} />;
  };

  const getChannelSummary = (ch: NotificationChannel): string => {
    if (ch.channelType === 'GMAIL_SMTP') {
      const host = ch.config.smtpHost || 'smtp.gmail.com';
      const sender = ch.config.username || 'Chưa nhập email gửi';
      return `${sender} • ${host}:${ch.config.smtpPort ?? 587}`;
    }
    if (ch.channelType === 'TELEGRAM') {
      return ch.config.chatId ? `Chat ID: ${ch.config.chatId}` : 'Chưa nhập Bot Token / Chat ID';
    }
    if (ch.channelType === 'ZALO') {
      const mode = String(ch.config.zaloMode || 'WEBHOOK').toUpperCase();
      if (mode === 'OA_API') return ch.config.targetId ? `Zalo OA • Target: ${ch.config.targetId}` : 'Zalo OA API v3 (Chưa cấu hình)';
      return ch.config.webhookUrl ? `Group Webhook • ${ch.config.webhookUrl}` : 'Nhóm Zalo Webhook (Chưa cấu hình URL)';
    }
    return ch.config.webhookUrl ? ch.config.webhookUrl : 'Chưa nhập Webhook URL';
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 22 }}>
      <div style={{ borderBottom: '1px solid var(--border-light)', paddingBottom: 14 }}>
        <h2 style={{ fontSize: 17, fontWeight: 700, margin: 0 }}>
          Kênh Gửi Thông báo Ngoài (Gmail, Zalo, Telegram &amp; Webhook)
        </h2>
        <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 4 }}>
          Đồng bộ thông báo giao việc, phê duyệt, nhắc tên &amp; Automation ra email, nhóm Zalo, nhóm Telegram hoặc các kênh cộng tác.
        </p>
      </div>

      {/* 1. Personal Opt-in Preferences */}
      <div style={{ padding: 16, borderRadius: 14, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-light)', display: 'flex', flexDirection: 'column', gap: 14 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
          <div>
            <strong style={{ fontSize: 14 }}>1. Nhận thông báo trực tiếp cho Tài khoản của tôi</strong>
            <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              Khi được giao việc mới hoặc có đơn cần bạn duyệt, hệ thống tự động nhắn về kênh cá nhân bạn đã bật.
            </div>
          </div>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={savingPrefs}
            onClick={() => onSavePrefs(prefs)}
          >
            <SaveRoundedIcon sx={{ fontSize: 15 }} />
            Lưu tùy chọn cá nhân
          </button>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(230px, 1fr))', gap: 12 }}>
          <div style={{ padding: 14, borderRadius: 12, backgroundColor: 'var(--bg-surface)', border: '1px solid var(--border-subtle)', display: 'flex', flexDirection: 'column', gap: 8 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 7, fontWeight: 700, fontSize: 13 }}>
                <MailRoundedIcon sx={{ fontSize: 18, color: '#ea4335' }} /> Gmail cá nhân
              </span>
              <BooleanSwitch
                checked={Boolean(prefs.notifyEmail)}
                onChange={(v) => setPrefs((p) => ({ ...p, notifyEmail: v }))}
                size="sm"
                trueLabel="Bật"
                falseLabel="Tắt"
              />
            </div>
            <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              Địa chỉ nhận: <strong>{user?.email || '(Chưa nhập Email ở tab Hồ sơ)'}</strong>
            </div>
          </div>

          <div style={{ padding: 14, borderRadius: 12, backgroundColor: 'var(--bg-surface)', border: '1px solid var(--border-subtle)', display: 'flex', flexDirection: 'column', gap: 8 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <label htmlFor="pref-tg-chat-id" style={{ display: 'inline-flex', alignItems: 'center', gap: 7, fontWeight: 700, fontSize: 13, cursor: 'pointer' }}>
                <TelegramIcon sx={{ fontSize: 18, color: '#0088cc' }} /> Telegram cá nhân
              </label>
              <BooleanSwitch
                checked={Boolean(prefs.notifyTelegram)}
                onChange={(v) => setPrefs((p) => ({ ...p, notifyTelegram: v }))}
                size="sm"
                trueLabel="Bật"
                falseLabel="Tắt"
              />
            </div>
            <input
              id="pref-tg-chat-id"
              name="telegramChatId"
              className="form-input"
              style={{ height: 34, fontSize: 12.5 }}
              placeholder="Telegram Chat ID (VD: 512345678)"
              value={prefs.telegramChatId || ''}
              onChange={(e) => setPrefs((p) => ({ ...p, telegramChatId: e.target.value }))}
            />
          </div>

          <div style={{ padding: 14, borderRadius: 12, backgroundColor: 'var(--bg-surface)', border: '1px solid var(--border-subtle)', display: 'flex', flexDirection: 'column', gap: 8 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <label htmlFor="pref-zalo-uid" style={{ display: 'inline-flex', alignItems: 'center', gap: 7, fontWeight: 700, fontSize: 13, cursor: 'pointer' }}>
                <ChatBubbleRoundedIcon sx={{ fontSize: 17, color: '#0068ff' }} /> Zalo cá nhân
              </label>
              <BooleanSwitch
                checked={Boolean(prefs.notifyZalo)}
                onChange={(v) => setPrefs((p) => ({ ...p, notifyZalo: v }))}
                size="sm"
                trueLabel="Bật"
                falseLabel="Tắt"
              />
            </div>
            <input
              id="pref-zalo-uid"
              name="zaloUserId"
              className="form-input"
              style={{ height: 34, fontSize: 12.5 }}
              placeholder="Zalo User ID / SĐT liên kết OA"
              value={prefs.zaloUserId || ''}
              onChange={(e) => setPrefs((p) => ({ ...p, zaloUserId: e.target.value }))}
            />
          </div>
        </div>
      </div>

      {/* 2. Collapsible Channel Cards */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
          <div>
            <strong style={{ fontSize: 14 }}>2. Kênh gửi Nhóm &amp; Tích hợp ({channels.length})</strong>
            <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              Nhấn vào từng kênh để mở rộng cấu hình chi tiết hoặc nhấn &ldquo;Gửi thử&rdquo;.
            </div>
          </div>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {(['GMAIL_SMTP', 'TELEGRAM', 'ZALO', 'TEAMS', 'SLACK', 'WEBHOOK'] as const).map((type) => (
              <button
                key={type}
                type="button"
                className="btn btn-outline btn-xs"
                onClick={() => handleAddChannel(type, isAdmin ? 'SYSTEM' : 'USER')}
              >
                <AddRoundedIcon sx={{ fontSize: 13 }} />
                {CHANNEL_META[type].label}
              </button>
            ))}
          </div>
        </div>

        {loading ? (
          <div style={{ padding: 24, textAlign: 'center', color: 'var(--text-muted)' }}>Đang tải danh sách kênh...</div>
        ) : channels.length === 0 ? (
          <div style={{ padding: 20, borderRadius: 12, backgroundColor: 'var(--bg-primary)', fontSize: 13, color: 'var(--text-muted)' }}>
            Bạn chưa tạo kênh thông báo riêng nào. Nhấn các nút <strong>+ Telegram / Zalo / Webhook</strong> ở trên để thêm kênh mới.
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {channels.map((ch) => {
              const meta = CHANNEL_META[ch.channelType] || CHANNEL_META.WEBHOOK;
              const isPreset = ['nch_gmail_sys', 'nch_telegram_sys', 'nch_zalo_sys'].includes(ch.id);
              const isExpanded = expandedId === ch.id;
              const zaloMode = String(ch.config.zaloMode || 'WEBHOOK').toUpperCase();

              return (
                <div
                  key={ch.id}
                  style={{
                    borderRadius: 14,
                    border: `1px solid ${isExpanded ? meta.color : 'var(--border-subtle)'}`,
                    borderLeft: `4px solid ${meta.color}`,
                    backgroundColor: 'var(--bg-surface)',
                    overflow: 'hidden',
                    transition: 'border-color 0.15s ease',
                  }}
                >
                  {/* Accordion Header */}
                  <div
                    onClick={() => setExpandedId(isExpanded ? null : ch.id)}
                    style={{
                      padding: '12px 16px',
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      flexWrap: 'wrap',
                      gap: 10,
                      cursor: 'pointer',
                      backgroundColor: isExpanded ? 'var(--bg-primary)' : 'transparent',
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0, flex: 1 }}>
                      <div
                        style={{
                          width: 34,
                          height: 34,
                          borderRadius: 10,
                          backgroundColor: meta.bg,
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          flexShrink: 0,
                        }}
                      >
                        {renderChannelIcon(ch.channelType)}
                      </div>
                      <div style={{ minWidth: 0 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 7, flexWrap: 'wrap' }}>
                          <strong style={{ fontSize: 13.5 }}>{ch.name}</strong>
                          <span className="badge" style={{ backgroundColor: meta.bg, color: meta.color, fontSize: 10.5 }}>
                            {meta.label}
                          </span>
                          <span className="badge" style={{ backgroundColor: 'var(--bg-secondary)', color: 'var(--text-secondary)', fontSize: 10.5 }}>
                            {ch.ownerScope === 'SYSTEM' ? 'Toàn hệ thống' : 'Cá nhân'}
                          </span>
                        </div>
                        <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 2, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                          {getChannelSummary(ch)}
                        </div>
                      </div>
                    </div>

                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }} onClick={(e) => e.stopPropagation()}>
                      <BooleanSwitch
                        checked={ch.isEnabled}
                        onChange={(v) => updateChannelLocal(ch.id, { isEnabled: v })}
                        size="sm"
                        trueLabel="Bật"
                        falseLabel="Tắt"
                      />
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        disabled={testingId === ch.id}
                        onClick={() => handleTestChannel(ch)}
                      >
                        <SendRoundedIcon sx={{ fontSize: 13 }} />
                        {testingId === ch.id ? 'Đang gửi...' : 'Gửi thử'}
                      </button>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        onClick={() => setExpandedId(isExpanded ? null : ch.id)}
                        title={isExpanded ? 'Thu gọn' : 'Mở cấu hình'}
                      >
                        {isExpanded ? <ExpandLessRoundedIcon sx={{ fontSize: 16 }} /> : <ExpandMoreRoundedIcon sx={{ fontSize: 16 }} />}
                      </button>
                    </div>
                  </div>

                  {/* Expanded Form */}
                  {isExpanded && (
                    <form
                      onSubmit={(e) => {
                        e.preventDefault();
                        handleSaveChannel(ch);
                      }}
                      style={{
                        padding: 16,
                        borderTop: '1px solid var(--border-light)',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: 14,
                      }}
                    >
                      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10 }}>
                        <div>
                          <label htmlFor={`ch-name-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>Tên hiển thị của Kênh</label>
                          <input
                            id={`ch-name-${ch.id}`}
                            name="channelName"
                            className="form-input"
                            value={ch.name}
                            onChange={(e) => updateChannelLocal(ch.id, { name: e.target.value })}
                            required
                          />
                        </div>
                      </div>

                      {ch.channelType === 'GMAIL_SMTP' && (
                        <>
                          <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '8px 12px', borderRadius: 10, backgroundColor: '#fce8e6', color: '#a50e0e', fontSize: 12 }}>
                            <InfoOutlinedIcon sx={{ fontSize: 16 }} />
                            <span>
                              Với Gmail: bật Xác minh 2 bước trên Google Account → tạo <strong>Mật khẩu ứng dụng (App Password 16 ký tự)</strong> để điền bên dưới.
                            </span>
                          </div>
                          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(195px, 1fr))', gap: 10 }}>
                            <div>
                              <label htmlFor={`smtp-host-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>SMTP Host</label>
                              <input
                                id={`smtp-host-${ch.id}`}
                                name="smtpHost"
                                className="form-input"
                                value={ch.config.smtpHost || 'smtp.gmail.com'}
                                onChange={(e) => updateChannelConfig(ch.id, 'smtpHost', e.target.value)}
                              />
                            </div>
                            <div>
                              <label htmlFor={`smtp-port-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>Port (TLS 587 / SSL 465)</label>
                              <input
                                id={`smtp-port-${ch.id}`}
                                name="smtpPort"
                                type="number"
                                className="form-input"
                                value={ch.config.smtpPort ?? 587}
                                onChange={(e) => updateChannelConfig(ch.id, 'smtpPort', Number(e.target.value) || 587)}
                              />
                            </div>
                            <div>
                              <label htmlFor={`smtp-user-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>Gmail / Email gửi</label>
                              <input
                                id={`smtp-user-${ch.id}`}
                                name="smtpUsername"
                                autoComplete="email"
                                className="form-input"
                                placeholder="no-reply@company.vn"
                                value={ch.config.username || ''}
                                onChange={(e) => updateChannelConfig(ch.id, 'username', e.target.value)}
                              />
                            </div>
                            <div>
                              <label htmlFor={`smtp-pw-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>
                                App Password {ch.hasSecret && <span style={{ color: '#137333' }}>(Đã lưu)</span>}
                              </label>
                              <input
                                id={`smtp-pw-${ch.id}`}
                                name="smtpPassword"
                                type="password"
                                autoComplete="new-password"
                                className="form-input"
                                placeholder={ch.hasSecret ? '•••••••• (Giữ nguyên nếu để trống)' : 'Mật khẩu ứng dụng 16 ký tự'}
                                value={ch.config.password || ''}
                                onChange={(e) => updateChannelConfig(ch.id, 'password', e.target.value)}
                              />
                            </div>
                            <div>
                              <label htmlFor={`smtp-from-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>Tên hiển thị người gửi</label>
                              <input
                                id={`smtp-from-${ch.id}`}
                                name="fromName"
                                className="form-input"
                                value={ch.config.fromName || 'SmartApps Enterprise'}
                                onChange={(e) => updateChannelConfig(ch.id, 'fromName', e.target.value)}
                              />
                            </div>
                            <div>
                              <label htmlFor={`smtp-to-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>Email nhận mặc định (CC nhóm)</label>
                              <input
                                id={`smtp-to-${ch.id}`}
                                name="toEmails"
                                className="form-input"
                                placeholder="hr@company.vn, ops@company.vn"
                                value={ch.config.toEmails || ''}
                                onChange={(e) => updateChannelConfig(ch.id, 'toEmails', e.target.value)}
                              />
                            </div>
                          </div>
                          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '8px 12px', borderRadius: 10, backgroundColor: 'var(--bg-primary)' }}>
                            <span style={{ fontSize: 12.5, fontWeight: 600 }}>Tự động gửi tới Email cá nhân của người nhận thông báo (khi họ bật ở Mục 1)</span>
                            <BooleanSwitch
                              checked={Boolean(ch.config.useRecipientEmail)}
                              onChange={(v) => updateChannelConfig(ch.id, 'useRecipientEmail', v)}
                              size="sm"
                              trueLabel="Bật"
                              falseLabel="Tắt"
                            />
                          </div>
                        </>
                      )}

                      {ch.channelType === 'TELEGRAM' && (
                        <>
                          <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '8px 12px', borderRadius: 10, backgroundColor: '#e1f5fe', color: '#01579b', fontSize: 12 }}>
                            <InfoOutlinedIcon sx={{ fontSize: 16 }} />
                            <span>
                              Tạo Bot từ <strong>@BotFather</strong> để lấy Bot Token, thêm Bot vào Nhóm và lấy <strong>Chat ID</strong> (nhóm thường bắt đầu bằng <code>-100...</code>).
                            </span>
                          </div>
                          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 10 }}>
                            <div>
                              <label htmlFor={`tg-token-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>
                                Telegram Bot Token {ch.hasSecret && <span style={{ color: '#137333' }}>(Đã lưu)</span>}
                              </label>
                              <input
                                id={`tg-token-${ch.id}`}
                                name="botToken"
                                type="password"
                                autoComplete="off"
                                className="form-input"
                                placeholder={ch.hasSecret ? '•••••••• (Giữ nguyên nếu để trống)' : '123456789:ABCdef...'}
                                value={ch.config.botToken || ''}
                                onChange={(e) => updateChannelConfig(ch.id, 'botToken', e.target.value)}
                              />
                            </div>
                            <div>
                              <label htmlFor={`tg-chat-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>Chat ID / Group ID nhận tin</label>
                              <input
                                id={`tg-chat-${ch.id}`}
                                name="chatId"
                                className="form-input"
                                placeholder="-1001234567890 hoặc @ten_nhom"
                                value={ch.config.chatId || ''}
                                onChange={(e) => updateChannelConfig(ch.id, 'chatId', e.target.value)}
                              />
                            </div>
                          </div>
                        </>
                      )}

                      {ch.channelType === 'ZALO' && (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                            <button
                              type="button"
                              className={`btn btn-xs ${zaloMode === 'WEBHOOK' ? 'btn-primary' : 'btn-outline'}`}
                              onClick={() => updateChannelConfig(ch.id, 'zaloMode', 'WEBHOOK')}
                            >
                              Nhóm Zalo (Webhook / Bot Bridge)
                            </button>
                            <button
                              type="button"
                              className={`btn btn-xs ${zaloMode === 'OA_API' ? 'btn-primary' : 'btn-outline'}`}
                              onClick={() => updateChannelConfig(ch.id, 'zaloMode', 'OA_API')}
                            >
                              Zalo OA API v3 Chính thức
                            </button>
                          </div>

                          {zaloMode === 'OA_API' ? (
                            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 10 }}>
                              <div>
                                <label htmlFor={`zalo-ep-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>Zalo OA Endpoint</label>
                                <input
                                  id={`zalo-ep-${ch.id}`}
                                  name="endpointUrl"
                                  className="form-input"
                                  value={ch.config.endpointUrl || 'https://openapi.zalo.me/v3.0/oa/message/cs'}
                                  onChange={(e) => updateChannelConfig(ch.id, 'endpointUrl', e.target.value)}
                                />
                              </div>
                              <div>
                                <label htmlFor={`zalo-tok-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>
                                  Access Token {ch.hasSecret && <span style={{ color: '#137333' }}>(Đã lưu)</span>}
                                </label>
                                <input
                                  id={`zalo-tok-${ch.id}`}
                                  name="accessToken"
                                  type="password"
                                  autoComplete="off"
                                  className="form-input"
                                  placeholder={ch.hasSecret ? '••••••••' : 'Zalo OA Access Token'}
                                  value={ch.config.accessToken || ''}
                                  onChange={(e) => updateChannelConfig(ch.id, 'accessToken', e.target.value)}
                                />
                              </div>
                              <div>
                                <label htmlFor={`zalo-tid-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>Zalo User ID / Group ID</label>
                                <input
                                  id={`zalo-tid-${ch.id}`}
                                  name="targetId"
                                  className="form-input"
                                  placeholder="ID người nhận / nhóm"
                                  value={ch.config.targetId || ''}
                                  onChange={(e) => updateChannelConfig(ch.id, 'targetId', e.target.value)}
                                />
                              </div>
                            </div>
                          ) : (
                            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10 }}>
                              <div>
                                <label htmlFor={`zalo-wh-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>Webhook URL nhóm Zalo (Bot / n8n / Relay)</label>
                                <input
                                  id={`zalo-wh-${ch.id}`}
                                  name="webhookUrl"
                                  className="form-input"
                                  placeholder="https://bot.company.vn/zalo/group-notify"
                                  value={ch.config.webhookUrl || ''}
                                  onChange={(e) => updateChannelConfig(ch.id, 'webhookUrl', e.target.value)}
                                />
                              </div>
                              <div>
                                <label htmlFor={`zalo-grp-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>Mã Nhóm Zalo (Target Group ID - tùy chọn)</label>
                                <input
                                  id={`zalo-grp-${ch.id}`}
                                  name="targetId"
                                  className="form-input"
                                  placeholder="ID nhóm Zalo đích"
                                  value={ch.config.targetId || ''}
                                  onChange={(e) => updateChannelConfig(ch.id, 'targetId', e.target.value)}
                                />
                              </div>
                            </div>
                          )}
                        </div>
                      )}

                      {['TEAMS', 'SLACK', 'WEBHOOK'].includes(ch.channelType) && (
                        <div style={{ display: 'grid', gridTemplateColumns: ch.channelType === 'WEBHOOK' ? '2fr 1fr' : '1fr', gap: 10 }}>
                          <div>
                            <label htmlFor={`wh-url-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>Incoming Webhook URL</label>
                            <input
                              id={`wh-url-${ch.id}`}
                              name="webhookUrl"
                              className="form-input"
                              placeholder="https://..."
                              value={ch.config.webhookUrl || ''}
                              onChange={(e) => updateChannelConfig(ch.id, 'webhookUrl', e.target.value)}
                            />
                          </div>
                          {ch.channelType === 'WEBHOOK' && (
                            <div>
                              <label htmlFor={`wh-sec-${ch.id}`} className="form-label" style={{ fontSize: 11.5 }}>X-Webhook-Secret (tùy chọn)</label>
                              <input
                                id={`wh-sec-${ch.id}`}
                                name="secretValue"
                                type="password"
                                autoComplete="off"
                                className="form-input"
                                placeholder={ch.hasSecret ? '••••••••' : 'Secret token'}
                                value={ch.config.secretValue || ''}
                                onChange={(e) => updateChannelConfig(ch.id, 'secretValue', e.target.value)}
                              />
                            </div>
                          )}
                        </div>
                      )}

                      {/* Event Categories */}
                      <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                        <span style={{ fontSize: 11.5, fontWeight: 700, color: 'var(--text-muted)' }}>Sự kiện kích hoạt:</span>
                        {EVENT_CATEGORY_OPTIONS.map((opt) => {
                          const active = (ch.eventCategories || []).includes(opt.code);
                          return (
                            <button
                              key={opt.code}
                              type="button"
                              onClick={() => toggleCategory(ch, opt.code)}
                              className={`badge ${active ? 'badge-info' : ''}`}
                              style={{
                                cursor: 'pointer',
                                border: '1px solid var(--border-subtle)',
                                backgroundColor: active ? undefined : 'var(--bg-primary)',
                                color: active ? undefined : 'var(--text-secondary)',
                              }}
                            >
                              {opt.label}
                            </button>
                          );
                        })}
                      </div>

                      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, paddingTop: 8, borderTop: '1px solid var(--border-light)' }}>
                        {!isPreset && (
                          <button
                            type="button"
                            className="btn btn-outline btn-xs"
                            style={{ color: '#d93025' }}
                            onClick={() => handleDeleteChannel(ch)}
                          >
                            <DeleteOutlineRoundedIcon sx={{ fontSize: 14 }} />
                            Xóa kênh
                          </button>
                        )}
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          disabled={testingId === ch.id}
                          onClick={() => handleTestChannel(ch)}
                        >
                          <SendRoundedIcon sx={{ fontSize: 14 }} />
                          {testingId === ch.id ? 'Đang gửi thử...' : 'Gửi thử ngay'}
                        </button>
                        <button
                          type="submit"
                          className="btn btn-primary btn-xs"
                          disabled={savingId === ch.id}
                        >
                          <SaveRoundedIcon sx={{ fontSize: 14 }} />
                          {savingId === ch.id ? 'Đang lưu...' : 'Lưu cấu hình kênh'}
                        </button>
                      </div>
                    </form>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* 3. Delivery Logs */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <strong style={{ fontSize: 14 }}>3. Lịch sử Gửi thông báo gần đây ({logs.length})</strong>
          <button type="button" className="btn btn-outline btn-xs" onClick={loadAll}>
            <RefreshRoundedIcon sx={{ fontSize: 14 }} /> Làm mới
          </button>
        </div>
        <div className="table-responsive">
          <table className="data-table">
            <thead>
              <tr>
                <th>Thời gian</th>
                <th>Kênh</th>
                <th>Tiêu đề</th>
                <th>Trạng thái</th>
                <th>Chi tiết</th>
              </tr>
            </thead>
            <tbody>
              {logs.map((l) => (
                <tr key={l.id}>
                  <td style={{ fontSize: 11.5, color: 'var(--text-muted)', whiteSpace: 'nowrap' }}>
                    {new Date(l.createdAt).toLocaleString('vi-VN')}
                  </td>
                  <td>
                    <span className="badge badge-info">{l.channelType}</span>{' '}
                    <span style={{ fontSize: 12, fontWeight: 600 }}>{l.channelName}</span>
                  </td>
                  <td style={{ fontSize: 12.5 }}>{l.title}</td>
                  <td>
                    <StatusBadge
                      status={l.status === 'SUCCESS' ? 'ACTIVE' : 'ERROR'}
                      label={l.status === 'SUCCESS' ? 'Thành công' : 'Lỗi'}
                      size="sm"
                    />
                  </td>
                  <td style={{ fontSize: 12, color: 'var(--text-muted)' }}>{l.detail}</td>
                </tr>
              ))}
              {logs.length === 0 && (
                <tr>
                  <td colSpan={5} style={{ textAlign: 'center', color: 'var(--text-muted)', padding: 16 }}>
                    Chưa có lượt gửi thông báo ngoài nào. Nhấn <strong>&ldquo;Gửi thử&rdquo;</strong> trên một kênh để kiểm tra kết nối.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
