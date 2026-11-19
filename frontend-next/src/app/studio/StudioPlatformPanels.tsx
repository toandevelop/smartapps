'use client';

import React, { useCallback, useEffect, useState } from 'react';
import RocketLaunchRoundedIcon from '@mui/icons-material/RocketLaunchRounded';
import RestoreRoundedIcon from '@mui/icons-material/RestoreRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded';
import ErrorOutlineRoundedIcon from '@mui/icons-material/ErrorOutlineRounded';
import ExtensionRoundedIcon from '@mui/icons-material/ExtensionRounded';
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded';
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import PrintRoundedIcon from '@mui/icons-material/PrintRounded';
import { AppRecord, AppUpgradeCandidate, AppUpgradeExecutionResult, SysApp, SysColumn, SysTable } from '@/lib/types';
import { getPagePrintCss, PRINT_TEMPLATE_PRESETS, renderPrintTemplateHtml, SysPrintTemplate } from '@/lib/print-template';
import {
  compilePrintBuilderToHtml,
  createDefaultBuilderSchema,
  parseBuilderSchemaFromHtml,
  PrintBuilderSchema,
} from '@/lib/print-builder';
import PrintTemplateBuilder from './PrintTemplateBuilder';
import { useEscapeKey } from '@/lib/ui-hooks';

/* =========================================================
 * 1. APP PUBLISH & RELEASE HISTORY MODAL (FUN-09)
 * ========================================================= */
export function AppPublishModal({
  open,
  app,
  onClose,
  onPublished,
  toast,
}: {
  open: boolean;
  app: SysApp | null;
  onClose: () => void;
  onPublished: () => Promise<void> | void;
  toast: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [validating, setValidating] = useState(false);
  const [validation, setValidation] = useState<{ canPublish: boolean; errors: string[]; warnings: string[]; summary: string } | null>(null);
  const [releases, setReleases] = useState<Array<{
    id: string;
    versionNumber: number;
    notes?: string | null;
    publishedBy: string;
    publishedAt: string;
    tableCount: number;
    columnCount: number;
  }>>([]);
  const [diff, setDiff] = useState<{
    hasPublishedRelease: boolean;
    publishedVersion?: number | null;
    addedTables: string[];
    removedTables: string[];
    addedColumns: string[];
    removedColumns: string[];
    modifiedColumns: string[];
  } | null>(null);
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);

  const loadPublishState = useCallback(async () => {
    if (!app) return;
    setValidating(true);
    try {
      const [valRes, relRes, diffRes] = await Promise.all([
        fetch(`/api/apps/${encodeURIComponent(app.id)}/validate-publish`, { method: 'POST', credentials: 'include' }).then((r) => r.json()),
        fetch(`/api/apps/${encodeURIComponent(app.id)}/releases`, { credentials: 'include' }).then((r) => r.json()),
        fetch(`/api/apps/${encodeURIComponent(app.id)}/releases/diff`, { credentials: 'include' }).then((r) => r.ok ? r.json() : null).catch(() => null),
      ]);
      if (valRes.success) setValidation(valRes.data);
      if (relRes.success) setReleases(relRes.data || []);
      if (diffRes?.success) setDiff(diffRes.data);
    } finally {
      setValidating(false);
    }
  }, [app]);

  useEscapeKey(open && Boolean(app), onClose);

  useEffect(() => {
    if (!open || !app) return;
    let cancelled = false;
    Promise.resolve().then(() => {
      if (cancelled) return;
      setNotes('');
      void loadPublishState();
    });
    return () => {
      cancelled = true;
    };
  }, [open, app, loadPublishState]);

  if (!open || !app) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 680 }} onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <RocketLaunchRoundedIcon sx={{ color: app.color || '#1a73e8' }} />
            <div>
              <h3 style={{ margin: 0, fontSize: 16 }}>Phát hành Ứng dụng: {app.name}</h3>
              <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                Trạng thái hiện tại: <strong>{app.publishStatus || 'PUBLISHED'}</strong> • Phiên bản v{app.publishedVersion || 1}
              </div>
            </div>
          </div>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>Đóng</button>
        </div>

        <div className="modal-body" style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          {validating ? (
            <div style={{ fontSize: 13, color: 'var(--text-muted)' }}>Đang kiểm tra tính hợp lệ cấu trúc App...</div>
          ) : validation && (
            <div
              style={{
                padding: 12,
                borderRadius: 8,
                backgroundColor: validation.canPublish ? 'var(--success-bg)' : 'var(--danger-bg)',
                border: `1px solid ${validation.canPublish ? 'var(--success-border)' : 'var(--danger-border)'}`,
                fontSize: 12.5,
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontWeight: 700, color: validation.canPublish ? 'var(--success-text)' : 'var(--danger-text)' }}>
                {validation.canPublish ? <CheckCircleRoundedIcon sx={{ fontSize: 16 }} /> : <ErrorOutlineRoundedIcon sx={{ fontSize: 16 }} />}
                {validation.summary}
              </div>
              {validation.errors.length > 0 && (
                <ul style={{ margin: '6px 0 0 18px', color: 'var(--danger-text)' }}>
                  {validation.errors.map((e, i) => <li key={i}>{e}</li>)}
                </ul>
              )}
              {validation.warnings.length > 0 && (
                <ul style={{ margin: '6px 0 0 18px', color: 'var(--warning-text)' }}>
                  {validation.warnings.map((w, i) => (
                    <li key={i}><WarningAmberRoundedIcon sx={{ fontSize: 13, verticalAlign: 'middle', mr: 0.5 }} />{w}</li>
                  ))}
                </ul>
              )}
            </div>
          )}

          {diff && diff.hasPublishedRelease && (
            <div style={{ padding: 12, borderRadius: 8, border: '1px solid var(--border-subtle)', backgroundColor: 'var(--bg-secondary)', fontSize: 12 }}>
              <div style={{ fontWeight: 700, marginBottom: 6 }}>
                So sánh thay đổi Draft vs Bản phát hành v{diff.publishedVersion ?? 1}
              </div>
              {diff.addedTables.length === 0 && diff.removedTables.length === 0 && diff.addedColumns.length === 0 && diff.removedColumns.length === 0 && diff.modifiedColumns.length === 0 ? (
                <div style={{ color: 'var(--text-muted)' }}>Chưa có thay đổi cấu trúc bảng/cột nào kể từ bản phát hành gần nhất.</div>
              ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                  {diff.addedTables.map((t, i) => <div key={`at_${i}`} style={{ color: '#137333' }}>+ Bảng mới: {t}</div>)}
                  {diff.removedTables.map((t, i) => <div key={`rt_${i}`} style={{ color: '#c5221f' }}>- Bảng đã gỡ: {t}</div>)}
                  {diff.addedColumns.map((c, i) => <div key={`ac_${i}`} style={{ color: '#137333' }}>+ Cột mới: {c}</div>)}
                  {diff.modifiedColumns.map((c, i) => <div key={`mc_${i}`} style={{ color: '#b45309' }}>~ Sửa cột: {c}</div>)}
                  {diff.removedColumns.map((c, i) => <div key={`rc_${i}`} style={{ color: '#c5221f' }}>- Cột đã gỡ: {c}</div>)}
                </div>
              )}
            </div>
          )}

          <div style={{ display: 'flex', gap: 8 }}>
            <input
              className="form-input"
              style={{ flex: 1 }}
              placeholder="Ghi chú phiên bản phát hành (vd: Bổ sung quy tắc duyệt 2 bước)..."
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
            <button
              type="button"
              className="btn btn-primary btn-sm"
              disabled={busy || !validation?.canPublish}
              onClick={async () => {
                setBusy(true);
                try {
                  const res = await fetch(`/api/apps/${encodeURIComponent(app.id)}/publish`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'include',
                    body: JSON.stringify({ notes }),
                  }).then((r) => r.json());
                  if (res.success) {
                    toast(`Đã phát hành phiên bản v${res.data.versionNumber}`);
                    setNotes('');
                    await loadPublishState();
                    await onPublished();
                  } else {
                    toast(res.message || 'Phát hành thất bại', 'error');
                  }
                } finally {
                  setBusy(false);
                }
              }}
            >
              <RocketLaunchRoundedIcon sx={{ fontSize: 15 }} /> Phát hành
            </button>
          </div>

          <div>
            <div style={{ fontSize: 12.5, fontWeight: 700, marginBottom: 8 }}>Lịch sử phiên bản ({releases.length})</div>
            {releases.length === 0 ? (
              <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Chưa có bản phát hành nào được lưu.</div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6, maxHeight: 240, overflowY: 'auto' }}>
                {releases.map((rel) => (
                  <div
                    key={rel.id}
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      padding: '8px 12px',
                      borderRadius: 8,
                      border: '1px solid var(--border-light)',
                      backgroundColor: 'var(--bg-primary)',
                      fontSize: 12.5,
                    }}
                  >
                    <div>
                      <strong>v{rel.versionNumber}</strong> • {rel.tableCount} bảng, {rel.columnCount} cột
                      {rel.notes && <div style={{ color: 'var(--text-secondary)', fontSize: 12 }}>{rel.notes}</div>}
                      <div style={{ color: 'var(--text-muted)', fontSize: 11 }}>
                        Bởi @{rel.publishedBy} • {new Date(rel.publishedAt).toLocaleString('vi-VN')}
                      </div>
                    </div>
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      disabled={busy}
                      onClick={async () => {
                        setBusy(true);
                        try {
                          const res = await fetch(`/api/apps/${encodeURIComponent(app.id)}/releases/${rel.versionNumber}/rollback`, {
                            method: 'POST',
                            credentials: 'include',
                          }).then((r) => r.json());
                          if (res.success) {
                            toast(`Đã khôi phục về cấu trúc v${rel.versionNumber}`);
                            await loadPublishState();
                            await onPublished();
                          } else {
                            toast(res.message || 'Khôi phục thất bại', 'error');
                          }
                        } finally {
                          setBusy(false);
                        }
                      }}
                    >
                      <RestoreRoundedIcon sx={{ fontSize: 14 }} /> Khôi phục
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

/* =========================================================
 * 2. VIETNAM PLUGINS & CONNECTORS PANEL
 * ========================================================= */
export function PluginsMarketplacePanel({
  apps,
  selectedAppId,
  toast,
}: {
  apps: SysApp[];
  selectedAppId?: string;
  toast: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [catalog, setCatalog] = useState<Array<{
    code: string;
    name: string;
    category: string;
    description: string;
    requiredConfigKeys: string[];
  }>>([]);
  const [installed, setInstalled] = useState<Array<{
    id: string;
    code: string;
    name: string;
    category: string;
    config: Record<string, unknown>;
    hasSecret: boolean;
    appId?: string | null;
    isEnabled: boolean;
    lastTestStatus?: string | null;
    lastTestMessage?: string | null;
  }>>([]);
  const [logs, setLogs] = useState<Array<{
    id: string;
    direction: string;
    sourceType: string;
    sourceId: string;
    statusCode: number;
    success: boolean;
    summary?: string | null;
    createdAt: string;
  }>>([]);
  const [categoryFilter, setCategoryFilter] = useState('ALL');
  const [editingItem, setEditingItem] = useState<{
    code: string;
    name: string;
    category: string;
    requiredConfigKeys: string[];
    config: Record<string, string>;
    secretValue: string;
    appId: string;
    isEnabled: boolean;
  } | null>(null);
  const [saving, setSaving] = useState(false);

  const loadPlugins = useCallback(async () => {
    const [catRes, plgRes, logRes] = await Promise.all([
      fetch('/api/integrations/plugins/catalog', { credentials: 'include' }).then((r) => r.json()),
      fetch('/api/integrations/plugins', { credentials: 'include' }).then((r) => r.json()),
      fetch('/api/integrations/logs?limit=20', { credentials: 'include' }).then((r) => r.json()),
    ]);
    if (catRes.success) setCatalog(catRes.data || []);
    if (plgRes.success) setInstalled(plgRes.data || []);
    if (logRes.success) setLogs(logRes.data || []);
  }, []);

  useEffect(() => {
    let cancelled = false;
    Promise.resolve().then(() => {
      if (!cancelled) void loadPlugins();
    });
    return () => {
      cancelled = true;
    };
  }, [loadPlugins]);

  const categories = ['ALL', 'EINVOICE', 'DIGITAL_SIGN', 'PAYMENT', 'BANKING', 'SMS_VN', 'NOTIFICATION', 'WEBHOOK_INBOUND', 'ERP_CRM'];

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {/* Installed Plugins */}
      <div className="form-section-card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
          <strong style={{ fontSize: 13.5 }}>
            <ExtensionRoundedIcon sx={{ fontSize: 16, verticalAlign: 'middle', mr: 0.5 }} />
            Plugins đã cài đặt ({installed.length})
          </strong>
          <button type="button" className="btn btn-outline btn-xs" onClick={loadPlugins}>
            <RefreshRoundedIcon sx={{ fontSize: 14 }} /> Làm mới
          </button>
        </div>

        {installed.length === 0 ? (
          <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Chưa có plugin nào được cài đặt. Chọn từ danh mục bên dưới.</div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {installed.map((plg) => (
              <div
                key={plg.id}
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  flexWrap: 'wrap',
                  gap: 10,
                  padding: '10px 12px',
                  borderRadius: 8,
                  backgroundColor: 'var(--bg-surface)',
                  border: '1px solid var(--border-light)',
                }}
              >
                <div>
                  <div style={{ fontWeight: 700, fontSize: 13 }}>
                    {plg.name} <code style={{ fontSize: 11 }}>{plg.code}</code>{' '}
                    <span className={`badge ${plg.isEnabled ? 'badge-success' : 'badge-warning'}`}>
                      {plg.isEnabled ? 'Đang bật' : 'Tắt'}
                    </span>
                  </div>
                  <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
                    Nhóm: {plg.category} • Môi trường: {String(plg.config?.environment || 'sandbox')} • Secret: {plg.hasSecret ? 'Đã cấu hình' : 'Chưa có'}
                    {plg.lastTestMessage && ` • Test gần nhất: ${plg.lastTestStatus} (${plg.lastTestMessage})`}
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 6 }}>
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    onClick={() => {
                      const catMatch = catalog.find((c) => c.code === plg.code);
                      const cfg: Record<string, string> = {};
                      for (const k of catMatch?.requiredConfigKeys || Object.keys(plg.config || {})) {
                        cfg[k] = String(plg.config?.[k] ?? '');
                      }
                      setEditingItem({
                        code: plg.code,
                        name: plg.name,
                        category: plg.category,
                        requiredConfigKeys: catMatch?.requiredConfigKeys || Object.keys(cfg),
                        config: cfg,
                        secretValue: '',
                        appId: plg.appId || selectedAppId || '',
                        isEnabled: plg.isEnabled,
                      });
                    }}
                  >
                    Cấu hình
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    onClick={async () => {
                      const res = await fetch(`/api/integrations/plugins/${plg.id}/test`, { method: 'POST', credentials: 'include' }).then((r) => r.json());
                      toast(res.message || (res.success ? 'Kết nối OK' : 'Kết nối lỗi'), res.success ? 'success' : 'error');
                      await loadPlugins();
                    }}
                  >
                    <PlayArrowRoundedIcon sx={{ fontSize: 13 }} /> Test
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    onClick={async () => {
                      await fetch(`/api/integrations/plugins/${plg.id}/toggle`, {
                        method: 'PATCH',
                        headers: { 'Content-Type': 'application/json' },
                        credentials: 'include',
                        body: JSON.stringify({ isEnabled: !plg.isEnabled }),
                      });
                      await loadPlugins();
                    }}
                  >
                    {plg.isEnabled ? 'Tắt' : 'Bật'}
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    style={{ color: '#d93025' }}
                    onClick={async () => {
                      await fetch(`/api/integrations/plugins/${plg.id}`, { method: 'DELETE', credentials: 'include' });
                      await loadPlugins();
                      toast('Đã gỡ plugin');
                    }}
                  >
                    <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Inline Plugin Config Editor */}
      {editingItem && (
        <div style={{ padding: 14, borderRadius: 10, border: '1px solid var(--info-border)', backgroundColor: 'var(--info-bg)' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
            <strong style={{ fontSize: 13.5 }}>Cấu hình Plugin: {editingItem.name} ({editingItem.code})</strong>
            <button type="button" className="btn btn-outline btn-xs" onClick={() => setEditingItem(null)}>Đóng</button>
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 10 }}>
            {editingItem.requiredConfigKeys.map((k) => (
              <div key={k}>
                <label className="form-label" style={{ fontSize: 11.5 }}>{k}</label>
                {k === 'environment' ? (
                  <select
                    className="form-select"
                    value={editingItem.config[k] || 'sandbox'}
                    onChange={(e) => setEditingItem((prev) => prev && { ...prev, config: { ...prev.config, [k]: e.target.value } })}
                  >
                    <option value="sandbox">sandbox (Thử nghiệm)</option>
                    <option value="production">production (Chính thức)</option>
                  </select>
                ) : (
                  <input
                    className="form-input"
                    value={editingItem.config[k] || ''}
                    onChange={(e) => setEditingItem((prev) => prev && { ...prev, config: { ...prev.config, [k]: e.target.value } })}
                  />
                )}
              </div>
            ))}
            <div>
              <label className="form-label" style={{ fontSize: 11.5 }}>Secret / Token (để trống nếu giữ nguyên)</label>
              <input
                type="password"
                className="form-input"
                placeholder="••••••••"
                value={editingItem.secretValue}
                onChange={(e) => setEditingItem((prev) => prev && { ...prev, secretValue: e.target.value })}
              />
            </div>
            <div>
              <label className="form-label" style={{ fontSize: 11.5 }}>Phạm vi App</label>
              <select
                className="form-select"
                value={editingItem.appId}
                onChange={(e) => setEditingItem((prev) => prev && { ...prev, appId: e.target.value })}
              >
                <option value="">Toàn nền tảng</option>
                {apps.map((a) => <option key={a.id} value={a.id}>{a.name} ({a.code})</option>)}
              </select>
            </div>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 12 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5 }}>
              <input
                type="checkbox"
                checked={editingItem.isEnabled}
                onChange={(e) => setEditingItem((prev) => prev && { ...prev, isEnabled: e.target.checked })}
              />
              Kích hoạt ngay sau khi lưu
            </label>
            <button
              type="button"
              className="btn btn-primary btn-sm"
              disabled={saving}
              onClick={async () => {
                setSaving(true);
                try {
                  const res = await fetch('/api/integrations/plugins', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'include',
                    body: JSON.stringify({
                      code: editingItem.code,
                      name: editingItem.name,
                      category: editingItem.category,
                      config: editingItem.config,
                      secretValue: editingItem.secretValue || undefined,
                      appId: editingItem.appId || null,
                      isEnabled: editingItem.isEnabled,
                    }),
                  }).then((r) => r.json());
                  if (res.success) {
                    toast(`Đã lưu cấu hình plugin ${editingItem.name}`);
                    setEditingItem(null);
                    await loadPlugins();
                  } else {
                    toast(res.message || 'Lưu thất bại', 'error');
                  }
                } finally {
                  setSaving(false);
                }
              }}
            >
              {saving ? 'Đang lưu...' : 'Lưu cấu hình Plugin'}
            </button>
          </div>
        </div>
      )}

      {/* Catalog */}
      <div>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8, marginBottom: 10 }}>
          <strong style={{ fontSize: 13.5 }}>Danh mục Plugin Việt Nam &amp; Connectors ({catalog.length})</strong>
          <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
            {categories.map((cat) => (
              <button
                key={cat}
                type="button"
                className={`btn btn-xs ${categoryFilter === cat ? 'btn-primary' : 'btn-outline'}`}
                onClick={() => setCategoryFilter(cat)}
              >
                {cat === 'ALL' ? 'Tất cả' : cat}
              </button>
            ))}
          </div>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(250px, 1fr))', gap: 10 }}>
          {catalog
            .filter((c) => categoryFilter === 'ALL' || c.category === categoryFilter)
            .map((item) => (
              <div
                key={item.code}
                style={{
                  padding: 12,
                  borderRadius: 10,
                  border: '1px solid var(--border-light)',
                  backgroundColor: 'var(--bg-surface)',
                  display: 'flex',
                  flexDirection: 'column',
                  justifyContent: 'space-between',
                  gap: 8,
                }}
              >
                <div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6 }}>
                    <strong style={{ fontSize: 13 }}>{item.name}</strong>
                    <span className="badge badge-info" style={{ fontSize: 10 }}>{item.category}</span>
                  </div>
                  <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>{item.description}</div>
                </div>
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  onClick={() => {
                    const initCfg: Record<string, string> = {};
                    for (const k of item.requiredConfigKeys) {
                      initCfg[k] = k === 'environment' ? 'sandbox' : '';
                    }
                    setEditingItem({
                      code: item.code,
                      name: item.name,
                      category: item.category,
                      requiredConfigKeys: item.requiredConfigKeys,
                      config: initCfg,
                      secretValue: '',
                      appId: selectedAppId || '',
                      isEnabled: true,
                    });
                  }}
                >
                  Cài đặt / Cấu hình
                </button>
              </div>
            ))}
        </div>
      </div>

      {/* Integration Logs */}
      {logs.length > 0 && (
        <div>
          <strong style={{ fontSize: 13, display: 'block', marginBottom: 6 }}>Nhật ký tích hợp gần đây ({logs.length})</strong>
          <div className="table-responsive">
            <table className="data-table">
              <thead>
                <tr><th>Thời gian</th><th>Hướng</th><th>Loại</th><th>Trạng thái</th><th>Chi tiết</th></tr>
              </thead>
              <tbody>
                {logs.map((l) => (
                  <tr key={l.id}>
                    <td style={{ fontSize: 11.5 }}>{new Date(l.createdAt).toLocaleString('vi-VN')}</td>
                    <td><span className="badge badge-info">{l.direction}</span></td>
                    <td><code>{l.sourceType}</code></td>
                    <td>
                      <span className={`badge ${l.success ? 'badge-success' : 'badge-danger'}`}>
                        {l.statusCode}
                      </span>
                    </td>
                    <td style={{ fontSize: 12 }}>{l.summary}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

/* =========================================================
 * 2B. PER-APP PLUGIN BINDINGS & FIELD MAPPING (STUDIO)
 * ========================================================= */
export interface AppPluginBindingItem {
  id: string;
  appId: string;
  pluginCode: string;
  pluginName: string;
  category: string;
  description?: string | null;
  isSystemEnabled: boolean;
  hasSecret: boolean;
  lastTestStatus?: string | null;
  isAppEnabled: boolean;
  tableId?: string | null;
  fieldMapping: Record<string, string>;
}

export function AppPluginBindingsPanel({
  app,
  tables,
  toast,
}: {
  app: SysApp | null;
  tables: SysTable[];
  toast: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [items, setItems] = useState<AppPluginBindingItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [editingCode, setEditingCode] = useState<string | null>(null);
  const [draftTableId, setDraftTableId] = useState('');
  const [draftMapping, setDraftMapping] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);

  const appId = app?.id;
  const loadBindings = useCallback(async () => {
    if (!appId) return;
    setLoading(true);
    try {
      const res = await fetch(`/api/integrations/apps/${encodeURIComponent(appId)}/plugins`, {
        credentials: 'include',
      }).then((r) => r.json());
      if (res?.success && Array.isArray(res.data)) {
        setItems(res.data);
      }
    } catch {
      // ignore
    } finally {
      setLoading(false);
    }
  }, [appId]);

  useEffect(() => {
    let cancelled = false;
    Promise.resolve().then(() => {
      if (!cancelled) void loadBindings();
    });
    return () => {
      cancelled = true;
    };
  }, [loadBindings]);

  const saveBinding = async (
    pluginCode: string,
    isEnabled: boolean,
    tableId?: string | null,
    fieldMapping?: Record<string, string>
  ) => {
    if (!app?.id) return;
    setSaving(true);
    try {
      const res = await fetch(
        `/api/integrations/apps/${encodeURIComponent(app.id)}/plugins/${encodeURIComponent(pluginCode)}`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({
            isEnabled,
            tableId: tableId || null,
            fieldMapping: fieldMapping || {},
          }),
        }
      ).then((r) => r.json());
      if (res?.success) {
        toast(isEnabled ? `Đã bật ${pluginCode} cho ${app.name}` : `Đã tắt ${pluginCode} trên ${app.name}`);
        setEditingCode(null);
        await loadBindings();
      } else {
        toast(res?.message || 'Không lưu được cấu hình plugin cho app.', 'error');
      }
    } finally {
      setSaving(false);
    }
  };

  if (!app) return null;

  return (
    <div className="form-section-card" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
        <div>
          <strong style={{ fontSize: 14 }}>
            <ExtensionRoundedIcon sx={{ fontSize: 17, verticalAlign: 'middle', mr: 0.5 }} />
            Plugin kích hoạt cho {app.name} ({items.filter((i) => i.isAppEnabled && i.isSystemEnabled).length}/{items.length})
          </strong>
          <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
            Chọn các plugin đã kết nối ở cấp Hệ thống để bật nút thao tác và map trường dữ liệu cho riêng ứng dụng này.
          </div>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <a href="/settings?tab=plugins" className="btn btn-outline btn-xs">
            Quản lý kết nối Hệ thống
          </a>
          <button type="button" className="btn btn-outline btn-xs" onClick={loadBindings} disabled={loading}>
            <RefreshRoundedIcon sx={{ fontSize: 14 }} /> Làm mới
          </button>
        </div>
      </div>

      {/* Dynamic OpenAPI 3.0 & MCP Server Gateway Strip */}
      <div
        style={{
          padding: '10px 12px',
          borderRadius: 10,
          backgroundColor: 'var(--bg-secondary)',
          border: '1px solid var(--border-subtle)',
          display: 'flex',
          flexDirection: 'column',
          gap: 6,
          fontSize: 12,
        }}
      >
        <div style={{ fontWeight: 700, color: 'var(--text-primary)' }}>
          🌐 OpenAPI 3.0 &amp; MCP AI Agent Gateway cho App {app.code}
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'center' }}>
          <span>
            OpenAPI Spec:{' '}
            <a
              href={`/api/apps/${encodeURIComponent(app.code)}/openapi.json`}
              target="_blank"
              rel="noreferrer"
              style={{ color: 'var(--accent-primary)', fontWeight: 600 }}
            >
              /api/apps/{app.code}/openapi.json
            </a>
          </span>
          <span>
            MCP JSON-RPC:{' '}
            <code>POST /api/mcp/{app.code}</code>
          </span>
        </div>
      </div>

      {items.length === 0 ? (
        <div
          style={{
            padding: 16,
            borderRadius: 10,
            backgroundColor: 'var(--bg-surface)',
            border: '1px dashed var(--border-light)',
            fontSize: 12.5,
            color: 'var(--text-muted)',
          }}
        >
          Chưa có plugin nào được cài đặt ở cấp Hệ thống. Vui lòng vào{' '}
          <a href="/settings?tab=plugins" style={{ color: 'var(--accent-primary)', fontWeight: 600 }}>
            Cài đặt → Tích hợp &amp; Plugin
          </a>{' '}
          để kết nối trước.
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          {items.map((item) => {
            const isEditing = editingCode === item.pluginCode;
            return (
              <div
                key={item.pluginCode}
                style={{
                  padding: '10px 12px',
                  borderRadius: 10,
                  backgroundColor: 'var(--bg-surface)',
                  border: '1px solid var(--border-light)',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 8,
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
                  <div>
                    <div style={{ fontWeight: 700, fontSize: 13, display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                      <span>{item.pluginName}</span>
                      <code style={{ fontSize: 11 }}>{item.pluginCode}</code>
                      <span className="badge badge-info">{item.category}</span>
                      {!item.isSystemEnabled ? (
                        <span className="badge badge-warning">Chưa bật ở Hệ thống</span>
                      ) : item.isAppEnabled ? (
                        <span className="badge badge-success">Đang bật cho App</span>
                      ) : (
                        <span className="badge" style={{ backgroundColor: '#e2e8f0', color: '#475569' }}>Đang tắt ở App</span>
                      )}
                    </div>
                    {item.description && (
                      <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>{item.description}</div>
                    )}
                  </div>

                  <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      disabled={saving}
                      onClick={() => {
                        if (isEditing) {
                          setEditingCode(null);
                        } else {
                          setEditingCode(item.pluginCode);
                          setDraftTableId(item.tableId || '');
                          setDraftMapping({ ...(item.fieldMapping || {}) });
                        }
                      }}
                    >
                      {isEditing ? 'Đóng' : 'Map trường'}
                    </button>
                    <button
                      type="button"
                      className={`btn btn-xs ${item.isAppEnabled ? 'btn-outline' : 'btn-primary'}`}
                      disabled={saving || !item.isSystemEnabled}
                      title={!item.isSystemEnabled ? 'Cần bật plugin ở Cài đặt Hệ thống trước' : ''}
                      onClick={() =>
                        saveBinding(item.pluginCode, !item.isAppEnabled, item.tableId, item.fieldMapping)
                      }
                    >
                      {item.isAppEnabled ? 'Tắt ở App' : 'Bật cho App'}
                    </button>
                  </div>
                </div>

                {isEditing && (
                  <div
                    style={{
                      paddingTop: 8,
                      borderTop: '1px solid var(--border-light)',
                      display: 'grid',
                      gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
                      gap: 8,
                      alignItems: 'end',
                    }}
                  >
                    <div>
                      <label style={{ fontSize: 11.5, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                        Bảng áp dụng (tùy chọn)
                      </label>
                      <select
                        className="form-select"
                        value={draftTableId}
                        onChange={(e) => setDraftTableId(e.target.value)}
                      >
                        <option value="">Tất cả bảng trong {app.code}</option>
                        {tables.map((t) => (
                          <option key={t.id} value={t.id}>
                            {t.displayName} ({t.tableName})
                          </option>
                        ))}
                      </select>
                    </div>
                    <div>
                      <label style={{ fontSize: 11.5, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                        Cột Số tiền (amountField)
                      </label>
                      <input
                        className="form-input"
                        placeholder="VD: tong_tien"
                        value={draftMapping.amountField || ''}
                        onChange={(e) => setDraftMapping((m) => ({ ...m, amountField: e.target.value }))}
                      />
                    </div>
                    <div>
                      <label style={{ fontSize: 11.5, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                        Cột SĐT nhận tin (phoneField)
                      </label>
                      <input
                        className="form-input"
                        placeholder="VD: so_dien_thoai"
                        value={draftMapping.phoneField || ''}
                        onChange={(e) => setDraftMapping((m) => ({ ...m, phoneField: e.target.value }))}
                      />
                    </div>
                    <div>
                      <label style={{ fontSize: 11.5, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                        Cột Mã chứng từ (orderRefField)
                      </label>
                      <input
                        className="form-input"
                        placeholder="VD: ma_hop_dong"
                        value={draftMapping.orderRefField || ''}
                        onChange={(e) => setDraftMapping((m) => ({ ...m, orderRefField: e.target.value }))}
                      />
                    </div>
                    <div>
                      <label style={{ fontSize: 11.5, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                        Ghi ngược Số HĐ/Mã GD (tùy chọn)
                      </label>
                      <input
                        className="form-input"
                        placeholder="VD: so_hoa_don"
                        value={draftMapping.writeBackRefField || ''}
                        onChange={(e) => setDraftMapping((m) => ({ ...m, writeBackRefField: e.target.value }))}
                      />
                    </div>
                    <div>
                      <label style={{ fontSize: 11.5, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                        Ghi ngược Link QR/Thanh toán
                      </label>
                      <input
                        className="form-input"
                        placeholder="VD: link_thanh_toan"
                        value={draftMapping.writeBackUrlField || ''}
                        onChange={(e) => setDraftMapping((m) => ({ ...m, writeBackUrlField: e.target.value }))}
                      />
                    </div>
                    <div>
                      <label style={{ fontSize: 11.5, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                        Ghi ngược Trạng thái tích hợp
                      </label>
                      <input
                        className="form-input"
                        placeholder="VD: trang_thai_tich_hop"
                        value={draftMapping.writeBackStatusField || ''}
                        onChange={(e) => setDraftMapping((m) => ({ ...m, writeBackStatusField: e.target.value }))}
                      />
                    </div>
                    <div style={{ display: 'flex', gap: 6 }}>
                      <button
                        type="button"
                        className="btn btn-primary btn-xs"
                        disabled={saving}
                        onClick={() =>
                          saveBinding(
                            item.pluginCode,
                            item.isSystemEnabled ? true : item.isAppEnabled,
                            draftTableId,
                            draftMapping
                          )
                        }
                      >
                        {saving ? 'Đang lưu...' : 'Lưu Mapping'}
                      </button>
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

/* =========================================================
 * 3. OUTBOX WEBHOOK MONITOR PANEL (DEV-04)
 * ========================================================= */
export function OutboxMonitorPanel({
  toast,
}: {
  toast: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [events, setEvents] = useState<Array<{
    id: string;
    eventType: string;
    aggregateId: string;
    status: string;
    attemptCount: number;
    maxAttempts: number;
    lastError?: string | null;
    nextAttemptAt: string;
    createdAt: string;
  }>>([]);
  const [statusFilter, setStatusFilter] = useState<'ALL' | 'PENDING' | 'FAILED' | 'DEAD_LETTER' | 'SUCCEEDED'>('ALL');
  const [retryingAll, setRetryingAll] = useState(false);

  const loadOutbox = useCallback(async () => {
    const stParam = statusFilter !== 'ALL' ? `&status=${statusFilter}` : '';
    const res = await fetch(`/api/automations/outbox?limit=80${stParam}`, { credentials: 'include' }).then((r) => r.json());
    if (res.success) setEvents(res.data || []);
  }, [statusFilter]);

  useEffect(() => {
    let cancelled = false;
    Promise.resolve().then(() => {
      if (!cancelled) void loadOutbox();
    });
    return () => {
      cancelled = true;
    };
  }, [loadOutbox]);

  const deadLetterCount = events.filter((e) => e.status === 'DEAD_LETTER').length;

  return (
    <div className="form-section-card" style={{ marginTop: 14 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8, marginBottom: 8 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <strong style={{ fontSize: 13.5 }}>Hàng đợi Webhook Outbox ({events.length})</strong>
          <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
            {(['ALL', 'PENDING', 'FAILED', 'DEAD_LETTER', 'SUCCEEDED'] as const).map((st) => (
              <button
                key={st}
                type="button"
                className={`btn btn-xs ${statusFilter === st ? 'btn-primary' : 'btn-outline'}`}
                onClick={() => setStatusFilter(st)}
              >
                {st === 'ALL' ? 'Tất cả' : st}
              </button>
            ))}
          </div>
        </div>
        <div style={{ display: 'flex', gap: 6 }}>
          {deadLetterCount > 0 && (
            <button
              type="button"
              className="btn btn-outline btn-xs"
              style={{ color: '#c5221f', borderColor: '#fad2cf' }}
              disabled={retryingAll}
              onClick={async () => {
                setRetryingAll(true);
                try {
                  const res = await fetch('/api/automations/outbox/retry-dead-letters', { method: 'POST', credentials: 'include' }).then((r) => r.json());
                  if (res.success) {
                    toast(`Đã đưa ${res.retried || 0} sự kiện Dead-Letter về hàng đợi thử lại.`);
                    await loadOutbox();
                  }
                } finally {
                  setRetryingAll(false);
                }
              }}
            >
              {retryingAll ? 'Đang đưa lại...' : `Thử lại tất cả Dead-Letter (${deadLetterCount})`}
            </button>
          )}
          <button type="button" className="btn btn-outline btn-xs" onClick={loadOutbox}>
            <RefreshRoundedIcon sx={{ fontSize: 14 }} /> Tải lại
          </button>
        </div>
      </div>
      {events.length === 0 ? (
        <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Chưa có sự kiện webhook nào trong hàng đợi Outbox.</div>
      ) : (
        <div className="table-responsive">
          <table className="data-table">
            <thead>
              <tr><th>ID</th><th>Loại</th><th>Trạng thái</th><th>Lần thử</th><th>Lỗi gần nhất</th><th>Thao tác</th></tr>
            </thead>
            <tbody>
              {events.map((ev) => (
                <tr key={ev.id}>
                  <td><code>{ev.id}</code></td>
                  <td>{ev.eventType}</td>
                  <td>
                    <span className={`badge ${ev.status === 'SUCCEEDED' ? 'badge-success' : ev.status === 'PENDING' ? 'badge-info' : 'badge-danger'}`}>
                      {ev.status}
                    </span>
                  </td>
                  <td>{ev.attemptCount}/{ev.maxAttempts}</td>
                  <td style={{ fontSize: 11.5, color: 'var(--danger-text)', maxWidth: 260 }}>{ev.lastError || '—'}</td>
                  <td>
                    {(ev.status === 'FAILED' || ev.status === 'DEAD_LETTER') && (
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        onClick={async () => {
                          const res = await fetch(`/api/automations/outbox/${ev.id}/retry`, { method: 'POST', credentials: 'include' }).then((r) => r.json());
                          if (res.success) {
                            toast('Đã đưa sự kiện về hàng đợi thử lại.');
                            await loadOutbox();
                          }
                        }}
                      >
                        Thử lại
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/* =========================================================
 * 4. PRINT TEMPLATE DESIGNER MODAL
 * ========================================================= */
export function PrintTemplateDesignerModal({
  open,
  app,
  table,
  columns,
  sampleRecord,
  onClose,
  toast,
}: {
  open: boolean;
  app: SysApp | null;
  table: SysTable | null;
  columns: SysColumn[];
  sampleRecord?: AppRecord | null;
  onClose: () => void;
  toast: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [templates, setTemplates] = useState<SysPrintTemplate[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [paperSize, setPaperSize] = useState('A4');
  const [orientation, setOrientation] = useState<'portrait' | 'landscape'>('portrait');
  const [isDefault, setIsDefault] = useState(true);
  const [watermarkDraft, setWatermarkDraft] = useState(true);
  const [requireApproved, setRequireApproved] = useState(false);
  const [editorMode, setEditorMode] = useState<'visual' | 'code'>('visual');
  const [builderSchema, setBuilderSchema] = useState<PrintBuilderSchema>(() =>
    createDefaultBuilderSchema(table?.displayName || 'Phiếu', columns),
  );
  const [htmlTemplate, setHtmlTemplate] = useState(PRINT_TEMPLATE_PRESETS[0].htmlTemplate);
  const [cssStyles, setCssStyles] = useState('');
  const [saving, setSaving] = useState(false);
  const templateRef = React.useRef<HTMLTextAreaElement | null>(null);
  const latestColumnsRef = React.useRef(columns);

  useEffect(() => {
    latestColumnsRef.current = columns;
  }, [columns]);

  const handleBuilderChange = (next: PrintBuilderSchema) => {
    setBuilderSchema(next);
    setHtmlTemplate(compilePrintBuilderToHtml(next));
  };

  const insertTokenAtCursor = (token: string) => {
    const el = templateRef.current;
    if (!el) {
      setHtmlTemplate((prev) => `${prev}${token}`);
      return;
    }
    const start = el.selectionStart ?? htmlTemplate.length;
    const end = el.selectionEnd ?? htmlTemplate.length;
    const next = `${htmlTemplate.slice(0, start)}${token}${htmlTemplate.slice(end)}`;
    setHtmlTemplate(next);
    setTimeout(() => {
      el.focus();
      el.setSelectionRange(start + token.length, start + token.length);
    }, 0);
  };

  const loadTemplates = useCallback(async () => {
    if (!table) return;
    const res = await fetch(`/api/tables/${encodeURIComponent(table.id)}/print-templates`, { credentials: 'include' }).then((r) => r.json());
    if (res.success) setTemplates(res.data || []);
  }, [table]);

  useEscapeKey(open && Boolean(table), onClose);

  useEffect(() => {
    if (!open || !table) return;
    let cancelled = false;
    Promise.resolve().then(() => {
      if (cancelled) return;
      void loadTemplates();
      setEditingId(null);
      setName(`Mẫu in ${table.displayName}`);
      setPaperSize('A4');
      setOrientation('portrait');
      setIsDefault(true);
      setWatermarkDraft(true);
      setRequireApproved(false);
      setCssStyles('');
      const defSchema = createDefaultBuilderSchema(table.displayName, latestColumnsRef.current);
      setBuilderSchema(defSchema);
      setEditorMode('visual');
      setHtmlTemplate(compilePrintBuilderToHtml(defSchema));
    });
    return () => {
      cancelled = true;
    };
  }, [open, table, loadTemplates]);

  if (!open || !table || !app) return null;

  const previewHtml = renderPrintTemplateHtml(htmlTemplate, {
    appName: app.name,
    appDepartment: app.department,
    tableName: table.displayName,
    recordKey: sampleRecord?.recordKey || 'REC-2026-0001',
    approvalStatus: sampleRecord?.approvalStatus || 'DRAFT',
    createdBy: sampleRecord?.createdBy || 'nhanvien',
    approvedBy: sampleRecord?.approvedBy || 'giamdoc',
    data: sampleRecord?.data || Object.fromEntries(columns.map((c) => [c.columnCode, c.dataType === 'CURRENCY' ? 1250000 : `[${c.columnName}]`])),
  });

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 1020 }} onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <PrintRoundedIcon sx={{ color: app.color || '#1a73e8' }} />
            <div>
              <h3 style={{ margin: 0, fontSize: 16 }}>Thiết kế Mẫu In: {table.displayName}</h3>
              <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                Hỗ trợ biến <code>{'{{ma_cot}}'}</code>, <code>{'{{tong_tien | currency}}'}</code>, <code>{'{{tong_tien | vnd_words}}'}</code>, và vòng lặp <code>{'{{#each chi_tiet}}...{{/each}}'}</code>
              </div>
            </div>
          </div>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>Đóng</button>
        </div>

        <div className="modal-body" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(360px, 1fr))', gap: 16 }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {templates.length > 0 && (
              <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
                <span style={{ fontSize: 11.5, fontWeight: 700, color: 'var(--text-muted)' }}>MẪU ĐÃ LƯU:</span>
                {templates.map((t) => (
                  <div key={t.id} style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                    <button
                      type="button"
                      className={`btn btn-xs ${editingId === t.id ? 'btn-primary' : 'btn-outline'}`}
                      onClick={() => {
                        setEditingId(t.id);
                        setName(t.name);
                        setPaperSize(t.paperSize);
                        setOrientation(t.orientation === 'landscape' ? 'landscape' : 'portrait');
                        setIsDefault(t.isDefault);
                        setWatermarkDraft(t.watermarkDraft);
                        setRequireApproved(t.requireApproved);
                        setCssStyles(t.cssStyles || '');
                        setHtmlTemplate(t.htmlTemplate);
                        const parsed = parseBuilderSchemaFromHtml(t.htmlTemplate);
                        if (parsed) {
                          setBuilderSchema(parsed);
                          setEditorMode('visual');
                        } else {
                          setEditorMode('code');
                        }
                      }}
                    >
                      {t.name}{t.isDefault ? ' ★' : ''}
                    </button>
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      style={{ color: '#d93025' }}
                      onClick={async () => {
                        await fetch(`/api/print-templates/${t.id}`, { method: 'DELETE', credentials: 'include' });
                        if (editingId === t.id) setEditingId(null);
                        await loadTemplates();
                        toast('Đã xóa mẫu in');
                      }}
                    >
                      ×
                    </button>
                  </div>
                ))}
              </div>
            )}

            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
              <span style={{ fontSize: 11.5, fontWeight: 700, color: 'var(--text-muted)' }}>MẪU KHỞI TẠO:</span>
              {PRINT_TEMPLATE_PRESETS.map((p) => (
                <button
                  key={p.key}
                  type="button"
                  className="btn btn-outline btn-xs"
                  onClick={() => {
                    setEditingId(null);
                    setName(p.name);
                    setPaperSize(p.paperSize);
                    setOrientation(p.orientation === 'landscape' ? 'landscape' : 'portrait');
                    setCssStyles('');
                    setEditorMode('code');
                    setHtmlTemplate(p.htmlTemplate);
                  }}
                >
                  {p.name}
                </button>
              ))}
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 1fr', gap: 8 }}>
              <div>
                <label className="form-label">Tên mẫu in</label>
                <input className="form-input" value={name} onChange={(e) => setName(e.target.value)} />
              </div>
              <div>
                <label className="form-label">Khổ giấy</label>
                <select className="form-select" value={paperSize} onChange={(e) => setPaperSize(e.target.value)}>
                  <option value="A4">A4</option>
                  <option value="A5">A5</option>
                  <option value="K80">K80 (80mm)</option>
                  <option value="LABEL_50X30">Tem 50x30mm</option>
                </select>
              </div>
              <div>
                <label className="form-label">Hướng giấy</label>
                <select
                  className="form-select"
                  value={orientation}
                  onChange={(e) => setOrientation(e.target.value as 'portrait' | 'landscape')}
                >
                  <option value="portrait">Dọc (Portrait)</option>
                  <option value="landscape">Ngang (Landscape)</option>
                </select>
              </div>
            </div>

            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', fontSize: 12.5 }}>
                <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  <input type="checkbox" checked={isDefault} onChange={(e) => setIsDefault(e.target.checked)} /> Mặc định
                </label>
                <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  <input type="checkbox" checked={watermarkDraft} onChange={(e) => setWatermarkDraft(e.target.checked)} /> Watermark nếu chưa duyệt
                </label>
                <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  <input type="checkbox" checked={requireApproved} onChange={(e) => setRequireApproved(e.target.checked)} /> Chỉ cho in khi APPROVED
                </label>
              </div>
              <div style={{ display: 'inline-flex', gap: 4 }}>
                <button
                  type="button"
                  className={`btn btn-xs ${editorMode === 'visual' ? 'btn-primary' : 'btn-outline'}`}
                  onClick={() => {
                    const parsed = parseBuilderSchemaFromHtml(htmlTemplate);
                    if (parsed) {
                      setBuilderSchema(parsed);
                    } else {
                      const fresh = createDefaultBuilderSchema(table.displayName, columns);
                      setBuilderSchema(fresh);
                    }
                    setEditorMode('visual');
                  }}
                >
                  Trực quan (Builder)
                </button>
                <button
                  type="button"
                  className={`btn btn-xs ${editorMode === 'code' ? 'btn-primary' : 'btn-outline'}`}
                  onClick={() => setEditorMode('code')}
                >
                  Mã HTML / CSS
                </button>
              </div>
            </div>

            {editorMode === 'visual' ? (
              <PrintTemplateBuilder schema={builderSchema} columns={columns} onChange={handleBuilderChange} />
            ) : (
              <div>
                <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginBottom: 6 }}>
                  <strong>Token Picker</strong> (nhấn để chèn nhanh vào vị trí con trỏ):
                </div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4, marginBottom: 8, maxHeight: 112, overflowY: 'auto', padding: 6, borderRadius: 8, backgroundColor: 'var(--bg-secondary)', border: '1px solid var(--border-subtle)' }}>
                  {[
                    { label: 'Mã hồ sơ', token: '{{record_key}}' },
                    { label: 'Barcode Mã HS', token: '{{barcode:record_key}}' },
                    { label: 'Ngày in', token: '{{print_date}}' },
                    { label: 'Người lập', token: '{{created_by}}' },
                    { label: 'Người duyệt', token: '{{approved_by}}' },
                    ...columns.map((c) => {
                      const dt = (c.dataType || '').toUpperCase();
                      if (dt === 'SUB_TABLE') {
                        return {
                          label: `Lặp bảng: ${c.columnName}`,
                          token: `{{#each ${c.columnCode}}}\n  <tr><td>{{@index1}}</td><td>...</td></tr>\n{{/each}}`,
                        };
                      }
                      if (dt === 'CURRENCY') return { label: `${c.columnName} (Tiền)`, token: `{{${c.columnCode} | currency}}` };
                      if (dt === 'DATE') return { label: `${c.columnName} (Ngày)`, token: `{{${c.columnCode} | date}}` };
                      if (dt === 'IMAGE') return { label: `${c.columnName} (Ảnh)`, token: `{{${c.columnCode} | image}}` };
                      return { label: c.columnName, token: `{{${c.columnCode}}}` };
                    }),
                    ...columns
                      .filter((c) => (c.dataType || '').toUpperCase() === 'CURRENCY')
                      .map((c) => ({ label: `${c.columnName} (Bằng chữ)`, token: `{{${c.columnCode} | vnd_words}}` })),
                  ].map((item, idx) => (
                    <button
                      key={`${item.label}_${idx}`}
                      type="button"
                      className="btn btn-outline btn-xs"
                      style={{ fontSize: 11, padding: '2px 7px' }}
                      onClick={() => insertTokenAtCursor(item.token)}
                      title={item.token}
                    >
                      + {item.label}
                    </button>
                  ))}
                </div>
                <textarea
                  ref={templateRef}
                  className="form-input"
                  rows={10}
                  style={{ fontFamily: 'monospace', fontSize: 12, marginBottom: 8 }}
                  value={htmlTemplate}
                  onChange={(e) => setHtmlTemplate(e.target.value)}
                />
                <label className="form-label">CSS tùy chỉnh (tùy chọn)</label>
                <textarea
                  className="form-input"
                  rows={3}
                  style={{ fontFamily: 'monospace', fontSize: 11.5 }}
                  placeholder="vd: h1 { color: #1e3a8a; }"
                  value={cssStyles}
                  onChange={(e) => setCssStyles(e.target.value)}
                />
              </div>
            )}

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
              <button
                type="button"
                className="btn btn-primary btn-sm"
                disabled={saving}
                onClick={async () => {
                  setSaving(true);
                  try {
                    const res = await fetch('/api/print-templates', {
                      method: 'POST',
                      headers: { 'Content-Type': 'application/json' },
                      credentials: 'include',
                      body: JSON.stringify({
                        id: editingId,
                        tableId: table.id,
                        name,
                        paperSize,
                        orientation,
                        htmlTemplate,
                        cssStyles: cssStyles.trim() || null,
                        isDefault,
                        watermarkDraft,
                        requireApproved,
                      }),
                    }).then((r) => r.json());
                    if (res.success) {
                      toast('Đã lưu mẫu in');
                      setEditingId(res.data.id);
                      await loadTemplates();
                    } else {
                      toast(res.message || 'Lưu mẫu in thất bại', 'error');
                    }
                  } finally {
                    setSaving(false);
                  }
                }}
              >
                {saving ? 'Đang lưu...' : 'Lưu Mẫu In'}
              </button>
            </div>
          </div>

          {/* Live Preview */}
          <div className="form-section-card" style={{ overflowY: 'auto', maxHeight: 620 }}>
            <div style={{ fontSize: 11.5, fontWeight: 700, color: 'var(--text-muted)', marginBottom: 8 }}>
              XEM TRƯỚC TRỰC TIẾP ({paperSize} • {orientation === 'landscape' ? 'Ngang' : 'Dọc'})
            </div>
            <style>{getPagePrintCss(paperSize, orientation, cssStyles)}</style>
            <div
              style={{
                backgroundColor: '#fff',
                color: '#111827',
                padding: 18,
                borderRadius: 8,
                border: '1px solid #cbd5e1',
                maxWidth: paperSize === 'K80' ? 320 : paperSize === 'LABEL_50X30' ? 360 : '100%',
                margin: '0 auto',
              }}
              dangerouslySetInnerHTML={{ __html: previewHtml }}
            />
          </div>
        </div>
      </div>
    </div>
  );
}

/* =========================================================
 * 5. AUTOMATED BACKUP & CLOUD SYNC PANEL (LOCAL / S3 / GDRIVE)
 * ========================================================= */
export function BackupManagerPanel({
  toast,
}: {
  toast: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [isEnabled, setIsEnabled] = useState(false);
  const [intervalHours, setIntervalHours] = useState(24);
  const [retentionCount, setRetentionCount] = useState(7);
  const [destinationType, setDestinationType] = useState<'LOCAL' | 'S3' | 'GOOGLE_DRIVE' | 'CUSTOM_WEBHOOK'>('LOCAL');
  const [destConfig, setDestConfig] = useState<Record<string, string>>({});
  const [destSecret, setDestSecret] = useState('');
  const [includeUploads, setIncludeUploads] = useState(true);
  const [hasSecret, setHasSecret] = useState(false);
  const [runs, setRuns] = useState<Array<{
    id: string;
    triggerType: string;
    destinationType: string;
    fileName: string;
    remoteUri?: string | null;
    sizeBytes: number;
    status: string;
    message?: string | null;
    createdAt: string;
  }>>([]);
  const [busy, setBusy] = useState(false);

  const loadBackups = useCallback(async () => {
    const [cfgRes, runsRes] = await Promise.all([
      fetch('/api/backups/config', { credentials: 'include' }).then((r) => r.json()),
      fetch('/api/backups/runs?limit=15', { credentials: 'include' }).then((r) => r.json()),
    ]);
    if (cfgRes.success && cfgRes.data) {
      setIsEnabled(Boolean(cfgRes.data.isEnabled));
      setIntervalHours(Number(cfgRes.data.intervalHours || 24));
      setRetentionCount(Number(cfgRes.data.retentionCount || 7));
      setDestinationType((cfgRes.data.destinationType || 'LOCAL') as 'LOCAL' | 'S3' | 'GOOGLE_DRIVE' | 'CUSTOM_WEBHOOK');
      const rawCfg = cfgRes.data.destinationConfig || {};
      setDestConfig(Object.fromEntries(Object.entries(rawCfg).map(([k, v]) => [k, String(v ?? '')])));
      setHasSecret(Boolean(cfgRes.data.hasSecret));
      setIncludeUploads(Boolean(cfgRes.data.includeUploads ?? true));
    }
    if (runsRes.success) setRuns(runsRes.data || []);
  }, []);

  useEffect(() => {
    let cancelled = false;
    Promise.resolve().then(() => {
      if (!cancelled) void loadBackups();
    });
    return () => {
      cancelled = true;
    };
  }, [loadBackups]);

  return (
    <div className="form-section-card" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
        <div>
          <strong style={{ fontSize: 14 }}>Sao lưu Tự động &amp; Đẩy lên Đám mây (S3 / Google Drive / Webhook)</strong>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
            Đóng gói toàn bộ dữ liệu PostgreSQL + tệp đính kèm (.zip kèm SHA-256) theo lịch tự động.
          </div>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={busy}
            onClick={async () => {
              setBusy(true);
              try {
                const res = await fetch('/api/backups/run', { method: 'POST', credentials: 'include' }).then((r) => r.json());
                toast(res.data?.message || (res.success ? 'Đã sao lưu thành công' : 'Sao lưu thất bại'), res.success ? 'success' : 'error');
                await loadBackups();
              } finally {
                setBusy(false);
              }
            }}
          >
            {busy ? 'Đang sao lưu...' : 'Sao lưu ngay'}
          </button>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 10 }}>
        <div>
          <label className="form-label">Đích lưu trữ</label>
          <select
            className="form-select"
            value={destinationType}
            onChange={(e) => setDestinationType(e.target.value as 'LOCAL' | 'S3' | 'GOOGLE_DRIVE' | 'CUSTOM_WEBHOOK')}
          >
            <option value="LOCAL">Cục bộ (Local Disk)</option>
            <option value="S3">Amazon S3 / MinIO / R2</option>
            <option value="GOOGLE_DRIVE">Google Drive (OAuth)</option>
            <option value="CUSTOM_WEBHOOK">Webhook / Storage HTTP</option>
          </select>
        </div>
        <div>
          <label className="form-label">Chu kỳ tự động (giờ)</label>
          <input type="number" min={1} max={720} className="form-input" value={intervalHours} onChange={(e) => setIntervalHours(Number(e.target.value) || 24)} />
        </div>
        <div>
          <label className="form-label">Giữ lại (số bản cục bộ)</label>
          <input type="number" min={1} max={100} className="form-input" value={retentionCount} onChange={(e) => setRetentionCount(Number(e.target.value) || 7)} />
        </div>
      </div>

      {destinationType === 'S3' && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 10 }}>
          <input className="form-input" placeholder="Endpoint (vd: http://minio:9000)" value={destConfig.endpoint || ''} onChange={(e) => setDestConfig((p) => ({ ...p, endpoint: e.target.value }))} />
          <input className="form-input" placeholder="Bucket (vd: smartapps-backups)" value={destConfig.bucket || ''} onChange={(e) => setDestConfig((p) => ({ ...p, bucket: e.target.value }))} />
          <input className="form-input" placeholder="Access Key" value={destConfig.accessKey || ''} onChange={(e) => setDestConfig((p) => ({ ...p, accessKey: e.target.value }))} />
          <input type="password" className="form-input" placeholder={hasSecret ? 'Secret Key (đã lưu — nhập để đổi)' : 'Secret Key'} value={destSecret} onChange={(e) => setDestSecret(e.target.value)} />
        </div>
      )}

      {destinationType === 'GOOGLE_DRIVE' && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10 }}>
          <input className="form-input" placeholder="Google Drive Folder ID (tùy chọn)" value={destConfig.folderId || ''} onChange={(e) => setDestConfig((p) => ({ ...p, folderId: e.target.value }))} />
          <input type="password" className="form-input" placeholder={hasSecret ? 'OAuth Access Token (đã lưu)' : 'Google Drive OAuth Access Token'} value={destSecret} onChange={(e) => setDestSecret(e.target.value)} />
        </div>
      )}

      {destinationType === 'CUSTOM_WEBHOOK' && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10 }}>
          <input className="form-input" placeholder="Webhook URL nhận file .zip" value={destConfig.webhookUrl || ''} onChange={(e) => setDestConfig((p) => ({ ...p, webhookUrl: e.target.value }))} />
          <input type="password" className="form-input" placeholder={hasSecret ? 'Bearer Token (đã lưu)' : 'Bearer Token (tùy chọn)'} value={destSecret} onChange={(e) => setDestSecret(e.target.value)} />
        </div>
      )}

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
        <div style={{ display: 'flex', gap: 16, fontSize: 12.5 }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <input type="checkbox" checked={isEnabled} onChange={(e) => setIsEnabled(e.target.checked)} /> Bật lịch sao lưu tự động
          </label>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <input type="checkbox" checked={includeUploads} onChange={(e) => setIncludeUploads(e.target.checked)} /> Kèm tệp đính kèm (uploads)
          </label>
        </div>
        <button
          type="button"
          className="btn btn-outline btn-xs"
          disabled={busy}
          onClick={async () => {
            setBusy(true);
            try {
              const res = await fetch('/api/backups/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify({
                  isEnabled,
                  intervalHours,
                  retentionCount,
                  destinationType,
                  destinationConfig: destConfig,
                  destinationSecret: destSecret || undefined,
                  includeUploads,
                }),
              }).then((r) => r.json());
              if (res.success) {
                setDestSecret('');
                toast('Đã lưu cấu hình sao lưu tự động');
                await loadBackups();
              }
            } finally {
              setBusy(false);
            }
          }}
        >
          Lưu cấu hình
        </button>
      </div>

      {runs.length > 0 && (
        <div className="table-responsive">
          <table className="data-table">
            <thead>
              <tr><th>Thời gian</th><th>Loại</th><th>Đích</th><th>Dung lượng</th><th>Trạng thái</th><th>Tệp</th></tr>
            </thead>
            <tbody>
              {runs.map((r) => (
                <tr key={r.id}>
                  <td style={{ fontSize: 11.5 }}>{new Date(r.createdAt).toLocaleString('vi-VN')}</td>
                  <td><span className="badge badge-info">{r.triggerType}</span></td>
                  <td>{r.destinationType}</td>
                  <td style={{ fontSize: 12 }}>{(r.sizeBytes / 1024).toFixed(1)} KB</td>
                  <td><span className={`badge ${r.status === 'SUCCEEDED' ? 'badge-success' : 'badge-danger'}`}>{r.status}</span></td>
                  <td>
                    <a href={`/api/backups/runs/${r.id}/download`} className="btn btn-outline btn-xs">Tải .zip</a>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/* =========================================================
 * 6. UPGRADE APPS TO V2.0 MODAL
 * ========================================================= */
export function AppUpgradeV2Modal({
  open,
  onClose,
  onUpgraded,
  toast,
}: {
  open: boolean;
  onClose: () => void;
  onUpgraded: () => Promise<void> | void;
  toast: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [candidates, setCandidates] = useState<AppUpgradeCandidate[]>([]);
  const [results, setResults] = useState<AppUpgradeExecutionResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const loadPreview = useCallback(async () => {
    setLoading(true);
    setErrorMsg(null);
    try {
      const res = await fetch('/api/apps/upgrade-v2/preview', { credentials: 'include' }).then((r) => r.json());
      if (res.success) {
        setCandidates(res.data || []);
      } else {
        setErrorMsg(res.message || 'Không thể tải danh sách nâng cấp v2.0');
      }
    } catch {
      setErrorMsg('Lỗi kết nối khi tải danh sách nâng cấp v2.0');
    } finally {
      setLoading(false);
    }
  }, []);

  useEscapeKey(open, onClose);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    Promise.resolve().then(() => {
      if (cancelled) return;
      setResults([]);
      void loadPreview();
    });
    return () => {
      cancelled = true;
    };
  }, [open, loadPreview]);

  if (!open) return null;

  const pendingCount = candidates.filter((c) => c.needsUpgrade).length;

  const upgradeOne = async (appId: string) => {
    setBusyId(appId);
    try {
      const res = await fetch(`/api/apps/${encodeURIComponent(appId)}/upgrade-v2`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({}),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setResults((prev) => [res.data, ...prev.filter((r) => r.appId !== appId)]);
        toast(`Đã nâng cấp ${res.data.appCode} lên v${res.data.newVersion}!`);
        await loadPreview();
        await onUpgraded();
      } else {
        toast(res.message || 'Nâng cấp thất bại', 'error');
      }
    } finally {
      setBusyId(null);
    }
  };

  const upgradeAll = async () => {
    const ids = candidates.filter((c) => c.needsUpgrade).map((c) => c.appId);
    if (ids.length === 0) return;
    setBusyId('__ALL__');
    try {
      const res = await fetch('/api/apps/upgrade-v2', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ appIds: ids }),
      }).then((r) => r.json());
      if (res.success && Array.isArray(res.data)) {
        setResults(res.data);
        toast(`Đã nâng cấp thành công ${res.data.length} ứng dụng lên v2.0.0!`);
        await loadPreview();
        await onUpgraded();
      } else {
        toast(res.message || 'Nâng cấp hàng loạt thất bại', 'error');
      }
    } finally {
      setBusyId(null);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 820, maxHeight: '88vh', display: 'flex', flexDirection: 'column' }} onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <RocketLaunchRoundedIcon sx={{ color: '#1a73e8' }} />
            <div>
              <h3 style={{ margin: 0, fontSize: 16 }}>Nâng cấp Ứng dụng lên Phiên bản 2.0.0</h3>
              <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                Không mất dữ liệu • Typed JSON SUB_TABLE • FormRules • Workflow • Mẫu in A4/K80 • Saved Views • Plugin VN
              </div>
            </div>
          </div>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose} aria-label="Đóng">
            ✕
          </button>
        </div>

        <div className="modal-body" style={{ overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
            <div style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
              {pendingCount > 0
                ? <>Phát hiện <strong>{pendingCount}/{candidates.length}</strong> ứng dụng cần bổ sung tính năng chuẩn v2.0.0.</>
                : <>Tất cả <strong>{candidates.length}</strong> ứng dụng đang đạt chuẩn phiên bản v2.0.0.</>}
            </div>
            <button type="button" className="btn btn-outline btn-xs" onClick={() => void loadPreview()} disabled={loading || busyId !== null}>
              <RefreshRoundedIcon sx={{ fontSize: 15 }} /> Kiểm tra lại
            </button>
          </div>

          {errorMsg && (
            <div className="alert alert-error" style={{ fontSize: 12.5 }}>
              {errorMsg}
            </div>
          )}

          {loading ? (
            <div style={{ padding: 24, textAlign: 'center', color: 'var(--text-muted)', fontSize: 13 }}>
              Đang phân tích cấu trúc &amp; tính năng từng ứng dụng...
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {candidates.map((c) => (
                <div
                  key={c.appId}
                  style={{
                    padding: '10px 14px',
                    borderRadius: 10,
                    border: '1px solid var(--border-subtle)',
                    backgroundColor: c.needsUpgrade ? 'var(--bg-primary)' : 'var(--bg-surface)',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    gap: 12,
                    flexWrap: 'wrap',
                  }}
                >
                  <div style={{ flex: 1, minWidth: 260 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                      <strong style={{ fontSize: 13.5, color: 'var(--text-primary)' }}>{c.appName}</strong>
                      <span className="badge badge-info" style={{ fontSize: 10.5 }}>{c.appCode}</span>
                      <span className={`badge ${c.needsUpgrade ? 'badge-warning' : 'badge-success'}`} style={{ fontSize: 10.5 }}>
                        v{c.currentVersion} {c.needsUpgrade ? `→ ${c.targetVersion}` : '• Đạt chuẩn 2.0'}
                      </span>
                    </div>
                    <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4 }}>
                      {c.missingFeatures.length > 0 ? c.missingFeatures.join(' • ') : c.summary}
                    </div>
                  </div>
                  <button
                    type="button"
                    className={`btn btn-sm ${c.needsUpgrade ? 'btn-primary' : 'btn-outline'}`}
                    disabled={busyId !== null}
                    onClick={() => void upgradeOne(c.appId)}
                  >
                    {busyId === c.appId ? 'Đang nâng cấp...' : c.needsUpgrade ? 'Nâng cấp lên 2.0.0' : 'Đồng bộ lại'}
                  </button>
                </div>
              ))}
            </div>
          )}

          {results.length > 0 && (
            <div style={{ padding: 12, borderRadius: 10, backgroundColor: 'var(--success-bg)', border: '1px solid var(--border-subtle)', fontSize: 12 }}>
              <strong style={{ display: 'block', marginBottom: 6 }}>Kết quả nâng cấp vừa thực hiện:</strong>
              {results.map((r) => (
                <div key={r.appId} style={{ color: 'var(--text-primary)', marginBottom: 3 }}>
                  <strong>{r.appCode}</strong> (v{r.previousVersion} → v{r.newVersion}):
                  +{r.columnsUpgraded} cột, +{r.formRulesAttached} rule, +{r.workflowsAdded} WF, +{r.printTemplatesAdded} mẫu in, +{r.savedViewsAdded} view
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="modal-footer" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <button type="button" className="btn btn-outline btn-sm" onClick={onClose} disabled={busyId !== null}>
            Đóng
          </button>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={loading || busyId !== null || pendingCount === 0}
            onClick={() => void upgradeAll()}
          >
            <RocketLaunchRoundedIcon sx={{ fontSize: 15 }} />
            {busyId === '__ALL__' ? 'Đang nâng cấp hàng loạt...' : `Nâng cấp Tất cả (${pendingCount})`}
          </button>
        </div>
      </div>
    </div>
  );
}

