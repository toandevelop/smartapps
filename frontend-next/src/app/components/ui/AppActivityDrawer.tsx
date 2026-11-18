'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Drawer from '@mui/material/Drawer';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded';
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded';
import CodeRoundedIcon from '@mui/icons-material/CodeRounded';
import { AppAuditLog, SysColumn } from '@/lib/types';
import { computeAuditFieldDiffs, formatAuditValue, formatRelativeTimeVi, getAuditActionMeta } from '@/lib/audit-utils';

export interface ActivityTableOption {
  id: string;
  displayName: string;
  columns?: SysColumn[];
}

export interface AppActivityDrawerProps {
  open: boolean;
  appId: string;
  appName: string;
  appColor?: string;
  tables?: ActivityTableOption[];
  defaultTableId?: string;
  onClose: () => void;
  onOpenRecord?: (recordId: string, tableId: string) => void;
}

export default function AppActivityDrawer({
  open,
  appId,
  appName,
  appColor = '#1a73e8',
  tables = [],
  defaultTableId = '',
  onClose,
  onOpenRecord,
}: AppActivityDrawerProps) {
  const [logs, setLogs] = useState<AppAuditLog[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [tableFilter, setTableFilter] = useState('');
  const [actionFilter, setActionFilter] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [expandedJsonIds, setExpandedJsonIds] = useState<Record<string, boolean>>({});

  useEffect(() => {
    if (open) {
      setTableFilter('');
      setActionFilter('');
      setSearchQuery('');
      setExpandedJsonIds({});
    }
  }, [open, appId]);

  const tableMap = useMemo(() => {
    const map = new Map<string, ActivityTableOption>();
    tables.forEach((t) => map.set(t.id, t));
    return map;
  }, [tables]);

  const fetchLogs = useCallback(async () => {
    if (!open || !appId) return;
    setLoading(true);
    setError('');
    try {
      const qs = new URLSearchParams({ appId, limit: '120' });
      if (tableFilter) qs.set('tableId', tableFilter);
      if (actionFilter) qs.set('action', actionFilter);
      if (searchQuery.trim()) qs.set('search', searchQuery.trim());
      const res = await fetch(`/api/records/audit-logs?${qs.toString()}`, { credentials: 'include' }).then((r) =>
        r.json()
      );
      if (res.success && Array.isArray(res.data)) {
        setLogs(res.data);
      } else {
        setLogs([]);
        setError(res.message || 'Không tải được lịch sử hoạt động.');
      }
    } catch {
      setLogs([]);
      setError('Không thể kết nối máy chủ để tải nhật ký hoạt động.');
    } finally {
      setLoading(false);
    }
  }, [open, appId, tableFilter, actionFilter, searchQuery]);

  useEffect(() => {
    if (!open) return;
    const t = window.setTimeout(() => {
      fetchLogs();
    }, 150);
    return () => window.clearTimeout(t);
  }, [open, fetchLogs]);

  const toggleJson = (id: string) => {
    setExpandedJsonIds((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  return (
    <Drawer anchor="right" open={open} onClose={onClose}>
      <div
        style={{
          width: 'min(660px, 100vw)',
          height: '100%',
          display: 'flex',
          flexDirection: 'column',
          backgroundColor: 'var(--bg-primary)',
        }}
      >
        {/* Header */}
        <div
          style={{
            padding: '16px 20px',
            backgroundColor: 'var(--bg-surface)',
            borderBottom: '1px solid var(--border-subtle)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 12,
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 11, minWidth: 0 }}>
            <div
              style={{
                width: 38,
                height: 38,
                borderRadius: 10,
                backgroundColor: `${appColor}18`,
                color: appColor,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
              }}
            >
              <HistoryRoundedIcon sx={{ fontSize: 21 }} />
            </div>
            <div style={{ minWidth: 0 }}>
              <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700, color: 'var(--text-primary)' }}>
                Lịch sử hoạt động &amp; Chỉnh sửa
              </h3>
              <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
                {appName} • {logs.length} nhật ký gần nhất
              </div>
            </div>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <button
              type="button"
              className="btn btn-outline btn-xs"
              onClick={fetchLogs}
              disabled={loading}
              title="Làm mới nhật ký"
            >
              <RefreshRoundedIcon sx={{ fontSize: 15 }} />
              Làm mới
            </button>
            <button
              type="button"
              className="btn btn-outline btn-xs"
              onClick={onClose}
              aria-label="Đóng lịch sử hoạt động"
            >
              <CloseRoundedIcon sx={{ fontSize: 16 }} />
            </button>
          </div>
        </div>

        {/* Filter Bar */}
        <div
          style={{
            padding: '12px 20px',
            backgroundColor: 'var(--bg-surface)',
            borderBottom: '1px solid var(--border-light)',
            display: 'flex',
            gap: 8,
            flexWrap: 'wrap',
            alignItems: 'center',
          }}
        >
          <div style={{ position: 'relative', flex: '1 1 200px', minWidth: 180 }}>
            <SearchRoundedIcon
              sx={{
                fontSize: 16,
                color: 'var(--text-muted)',
                position: 'absolute',
                left: 10,
                top: '50%',
                transform: 'translateY(-50%)',
                pointerEvents: 'none',
              }}
            />
            <input
              type="text"
              className="form-input"
              style={{ paddingLeft: 32, height: 34, fontSize: 12.5 }}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Tìm nội dung, @người dùng, mã bản ghi..."
            />
          </div>

          {tables.length > 1 && (
            <select
              className="form-select"
              style={{ width: 175, height: 34, fontSize: 12.5, padding: '4px 10px' }}
              value={tableFilter}
              onChange={(e) => setTableFilter(e.target.value)}
            >
              <option value="">Tất cả bảng ({tables.length})</option>
              {tables.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.displayName}
                  {t.id === defaultTableId ? ' (đang mở)' : ''}
                </option>
              ))}
            </select>
          )}

          <select
            className="form-select"
            style={{ width: 155, height: 34, fontSize: 12.5, padding: '4px 10px' }}
            value={actionFilter}
            onChange={(e) => setActionFilter(e.target.value)}
          >
            <option value="">Tất cả thao tác</option>
            <option value="CREATE">Tạo mới</option>
            <option value="UPDATE">Chỉnh sửa</option>
            <option value="SUBMIT_APPROVAL">Trình duyệt</option>
            <option value="APPROVE">Phê duyệt</option>
            <option value="REJECT">Từ chối</option>
            <option value="SOFT_DELETE">Vào thùng rác</option>
            <option value="RESTORE">Khôi phục</option>
            <option value="DELETE">Đã xóa</option>
          </select>
        </div>

        {/* Timeline Body */}
        <div style={{ flex: 1, overflowY: 'auto', padding: 18, display: 'flex', flexDirection: 'column', gap: 12 }}>
          {error ? (
            <div className="card" style={{ padding: 20, textAlign: 'center', color: 'var(--danger-text)', fontSize: 13 }}>
              {error}
            </div>
          ) : loading ? (
            <div className="card" style={{ padding: 28, textAlign: 'center', color: 'var(--text-muted)', fontSize: 13 }}>
              Đang tải lịch sử hoạt động...
            </div>
          ) : logs.length === 0 ? (
            <div className="card" style={{ padding: 32, textAlign: 'center', color: 'var(--text-muted)', fontSize: 13 }}>
              Chưa có hoạt động nào khớp với bộ lọc hiện tại.
            </div>
          ) : (
            logs.map((log) => {
              const meta = getAuditActionMeta(log.action);
              const tableInfo = tableMap.get(log.tableId);
              const diffs = computeAuditFieldDiffs(log.action, log.oldData, log.newData, tableInfo?.columns);
              const isJsonOpen = Boolean(expandedJsonIds[log.id]);
              const canJump =
                Boolean(onOpenRecord) &&
                log.action !== 'DELETE' &&
                log.action !== 'SOFT_DELETE' &&
                Boolean(log.recordId);

              return (
                <div
                  key={log.id}
                  className="card"
                  style={{
                    padding: 14,
                    display: 'flex',
                    flexDirection: 'column',
                    gap: 8,
                    borderLeft: `3px solid ${appColor}`,
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 10 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 7, flexWrap: 'wrap' }}>
                      <span className={`badge ${meta.badgeClass}`} style={{ fontSize: 11 }}>
                        {meta.label}
                      </span>
                      <strong style={{ fontSize: 13, color: 'var(--text-primary)' }}>@{log.actor}</strong>
                      {tableInfo && (
                        <span
                          className="badge"
                          style={{
                            fontSize: 10.5,
                            backgroundColor: 'var(--bg-secondary)',
                            color: 'var(--text-secondary)',
                          }}
                        >
                          {tableInfo.displayName}
                        </span>
                      )}
                    </div>
                    <span
                      style={{ fontSize: 11.5, color: 'var(--text-muted)', whiteSpace: 'nowrap' }}
                      title={new Date(log.createdAt).toLocaleString('vi-VN')}
                    >
                      {formatRelativeTimeVi(log.createdAt)} • {new Date(log.createdAt).toLocaleString('vi-VN')}
                    </span>
                  </div>

                  {log.summary && (
                    <div style={{ fontSize: 13, fontWeight: 500, color: 'var(--text-primary)' }}>{log.summary}</div>
                  )}

                  {/* Field-level Before -> After diff */}
                  {diffs.length > 0 && (
                    <div
                      style={{
                        borderRadius: 8,
                        backgroundColor: 'var(--bg-primary)',
                        border: '1px solid var(--border-light)',
                        padding: '8px 10px',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: 5,
                      }}
                    >
                      {diffs.slice(0, 8).map((d) => (
                        <div
                          key={d.key}
                          style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: 6,
                            flexWrap: 'wrap',
                            fontSize: 12,
                            lineHeight: 1.45,
                          }}
                        >
                          <span style={{ fontWeight: 600, color: 'var(--text-secondary)', minWidth: 120 }}>
                            {d.label}:
                          </span>
                          {d.kind === 'changed' ? (
                            <>
                              <span
                                style={{
                                  textDecoration: 'line-through',
                                  color: '#c5221f',
                                  backgroundColor: '#fce8e6',
                                  padding: '1px 6px',
                                  borderRadius: 4,
                                  wordBreak: 'break-word',
                                }}
                              >
                                {formatAuditValue(d.oldVal)}
                              </span>
                              <ArrowForwardRoundedIcon sx={{ fontSize: 13, color: 'var(--text-muted)' }} />
                              <span
                                style={{
                                  color: '#137333',
                                  backgroundColor: '#e6f4ea',
                                  padding: '1px 6px',
                                  borderRadius: 4,
                                  fontWeight: 600,
                                  wordBreak: 'break-word',
                                }}
                              >
                                {formatAuditValue(d.newVal)}
                              </span>
                            </>
                          ) : d.kind === 'added' ? (
                            <span
                              style={{
                                color: '#137333',
                                backgroundColor: '#e6f4ea',
                                padding: '1px 6px',
                                borderRadius: 4,
                                fontWeight: 500,
                                wordBreak: 'break-word',
                              }}
                            >
                              {formatAuditValue(d.newVal)}
                            </span>
                          ) : (
                            <span
                              style={{
                                textDecoration: 'line-through',
                                color: '#c5221f',
                                backgroundColor: '#fce8e6',
                                padding: '1px 6px',
                                borderRadius: 4,
                                wordBreak: 'break-word',
                              }}
                            >
                              {formatAuditValue(d.oldVal)}
                            </span>
                          )}
                        </div>
                      ))}
                      {diffs.length > 8 && (
                        <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
                          ...và {diffs.length - 8} trường dữ liệu khác (bấm JSON chi tiết để xem đầy đủ)
                        </div>
                      )}
                    </div>
                  )}

                  {/* Footer Actions */}
                  <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
                    <code style={{ fontSize: 11, color: 'var(--text-muted)' }}>{log.recordId}</code>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                      {(log.oldData || log.newData) && (
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          onClick={() => toggleJson(log.id)}
                          title="Xem dữ liệu JSON gốc"
                        >
                          <CodeRoundedIcon sx={{ fontSize: 13 }} />
                          {isJsonOpen ? 'Ẩn JSON' : 'JSON'}
                        </button>
                      )}
                      {canJump && (
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          onClick={() => {
                            onOpenRecord?.(log.recordId, log.tableId);
                            onClose();
                          }}
                        >
                          <OpenInNewRoundedIcon sx={{ fontSize: 13 }} />
                          Mở bản ghi
                        </button>
                      )}
                    </div>
                  </div>

                  {isJsonOpen && (
                    <pre className="code-box" style={{ fontSize: 11, padding: 10, margin: 0, maxHeight: 220, overflow: 'auto' }}>
                      {JSON.stringify({ oldData: log.oldData, newData: log.newData }, null, 2)}
                    </pre>
                  )}
                </div>
              );
            })
          )}
        </div>
      </div>
    </Drawer>
  );
}
