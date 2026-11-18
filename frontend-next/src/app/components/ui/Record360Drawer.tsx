'use client';

import React, { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import Drawer from '@mui/material/Drawer';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded';
import ForumRoundedIcon from '@mui/icons-material/ForumRounded';
import SendRoundedIcon from '@mui/icons-material/SendRounded';
import BadgeRoundedIcon from '@mui/icons-material/BadgeRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import FactCheckRoundedIcon from '@mui/icons-material/FactCheckRounded';
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import CancelRoundedIcon from '@mui/icons-material/CancelRounded';
import UndoRoundedIcon from '@mui/icons-material/UndoRounded';
import BlockRoundedIcon from '@mui/icons-material/BlockRounded';
import { AppAuditLog, AppRecord, RelatedTableGroup, SysColumn, RecordComment } from '@/lib/types';
import { batchFetchLookups, resolveLookupLabel } from '@/lib/lookup-service';
import { computeAuditFieldDiffs, formatAuditValue, getAuditActionMeta } from '@/lib/audit-utils';
import { requestLocalTokenSign } from '@/lib/tauri-bridge';
import { useAuth } from '@/lib/auth-context';
import StatusBadge from './StatusBadge';
import DynamicFieldInput from './DynamicFieldInput';

export interface Record360DrawerProps {
  open: boolean;
  record: AppRecord | null;
  columns: SysColumn[];
  tableDisplayName: string;
  appCode?: string;
  canEdit?: boolean;
  canApprove?: boolean;
  onClose: () => void;
  onChanged?: () => void;
}

function formatFieldDisplay(col: SysColumn, rawVal: unknown): React.ReactNode {
  if (rawVal === undefined || rawVal === null || rawVal === '') return <span style={{ color: 'var(--text-muted)' }}>—</span>;
  const dt = (col.dataType || 'STRING').toUpperCase();
  if (dt === 'BOOLEAN') {
    const b = rawVal === true || String(rawVal).toLowerCase() === 'true';
    return (
      <span className={`badge ${b ? 'badge-success' : 'badge-warning'}`} style={{ fontSize: 11 }}>
        {b ? 'Có / Đang áp dụng' : 'Không'}
      </span>
    );
  }
  if (dt === 'CURRENCY') {
    const num = Number(rawVal);
    return Number.isFinite(num) ? `${num.toLocaleString('vi-VN')} đ` : String(rawVal);
  }
  if (dt === 'NUMBER') {
    const num = Number(rawVal);
    return Number.isFinite(num) ? num.toLocaleString('vi-VN') : String(rawVal);
  }
  if (dt === 'DATE') {
    const s = String(rawVal).slice(0, 10);
    if (/^\d{4}-\d{2}-\d{2}$/.test(s)) {
      const [y, m, d] = s.split('-');
      return `${d}/${m}/${y}`;
    }
    return s;
  }
  if (col.refTableId || dt === 'LOOKUP') {
    return resolveLookupLabel(col, rawVal) || String(rawVal);
  }
  return String(rawVal);
}

export default function Record360Drawer({
  open,
  record,
  columns,
  tableDisplayName,
  appCode,
  canEdit,
  canApprove,
  onClose,
  onChanged,
}: Record360DrawerProps) {
  const { user, canPerformTableAction } = useAuth();
  const userRoles = user?.roleCodes?.length ? user.roleCodes : (user?.roleCode ? [user.roleCode] : []);
  const isUserAdmin = Boolean(user?.isAdmin || userRoles.some((r) => r.toUpperCase() === 'ADMIN'));
  const [liveRecordState, setLiveRecordState] = useState<{ baseId: string; rec: AppRecord } | null>(null);
  const liveRecord = liveRecordState && record && liveRecordState.baseId === record.id ? liveRecordState.rec : null;
  const setLiveRecord = (next: AppRecord | null) => {
    setLiveRecordState(next && record ? { baseId: record.id, rec: next } : null);
  };
  const activeRecord = liveRecord || record;
  const effectiveCanEdit = canEdit ?? (appCode && activeRecord ? canPerformTableAction(appCode, activeRecord.tableId, 'edit') : isUserAdmin);
  const effectiveCanApprove = canApprove ?? (appCode && activeRecord ? canPerformTableAction(appCode, activeRecord.tableId, 'approve') : isUserAdmin);
  const [tab, setTab] = useState<string>('info');
  const [wfBusy, setWfBusy] = useState(false);
  const [wfActionMode, setWfActionMode] = useState<'reject' | 'return' | null>(null);
  const [wfRejectReason, setWfRejectReason] = useState('');
  const [wfFeedback, setWfFeedback] = useState<{ ok: boolean; msg: string } | null>(null);
  const [restoringAuditKey, setRestoringAuditKey] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [groups, setGroups] = useState<RelatedTableGroup[]>([]);
  const [audits, setAudits] = useState<AppAuditLog[]>([]);
  const [comments, setComments] = useState<RecordComment[]>([]);
  const [commentText, setCommentText] = useState('');
  const [postingComment, setPostingComment] = useState(false);

  // Inline quick-create or edit child record inside any linked tab
  const [quickFormKey, setQuickFormKey] = useState<string | null>(null);
  const [editingChildId, setEditingChildId] = useState<string | null>(null);
  const [quickFormData, setQuickFormData] = useState<Record<string, unknown>>({});
  const [quickSaving, setQuickSaving] = useState(false);
  const [quickError, setQuickError] = useState('');
  const [enabledPlugins, setEnabledPlugins] = useState<Array<{
    id: string;
    code: string;
    name: string;
    category: string;
    tableId?: string | null;
    fieldMapping?: Record<string, string>;
  }>>([]);
  const [pluginRunning, setPluginRunning] = useState<string | null>(null);
  const [pluginResult, setPluginResult] = useState<{
    success: boolean;
    providerCode: string;
    operation: string;
    message: string;
    data: Record<string, unknown>;
  } | null>(null);
  const [aiSummary, setAiSummary] = useState<{ summary: string; engine: string } | null>(null);
  const [aiLoading, setAiLoading] = useState(false);
  const [aiTriage, setAiTriage] = useState<{
    riskLevel: string;
    riskScore: number;
    recommendation: string;
    recommendationLabel: string;
    summaryBullets: string[];
    anomalies: string[];
    historicalSampleSize: number;
    engine: string;
  } | null>(null);
  const [triageLoading, setTriageLoading] = useState(false);

  const loadRelationsAndLogs = useCallback(async () => {
    if (!record) return;
    setPluginResult(null);
    setAiSummary(null);
    setAiTriage(null);
    const canRunPlugins = isUserAdmin || effectiveCanEdit || effectiveCanApprove;
    if (canRunPlugins) {
      fetch(`/api/integrations/apps/${encodeURIComponent(record.appId)}/plugins?enabledOnly=true`)
        .then((r) => (r.ok ? r.json() : null))
        .then((res) => {
          if (res?.success && Array.isArray(res.data)) {
            const bindings = res.data as Array<{
              id: string;
              pluginCode: string;
              pluginName: string;
              category: string;
              tableId?: string | null;
              fieldMapping?: Record<string, string>;
            }>;
            setEnabledPlugins(
              bindings
                .filter((p) => p.category !== 'WEBHOOK_INBOUND' && (!p.tableId || p.tableId === record.tableId))
                .map((p) => ({
                  id: p.id,
                  code: p.pluginCode,
                  name: p.pluginName,
                  category: p.category,
                  tableId: p.tableId,
                  fieldMapping: p.fieldMapping || {},
                }))
            );
          }
        })
        .catch(() => {});
    } else {
      setEnabledPlugins([]);
    }
    setLoading(true);
    try {
      const [relRes, audRes, cmtRes] = await Promise.all([
        fetch(`/api/records/${encodeURIComponent(record.id)}/relations`, { credentials: 'include' }).then((r) => r.json()),
        fetch(`/api/records/${encodeURIComponent(record.id)}/audit`, { credentials: 'include' }).then((r) => r.json()),
        fetch(`/api/records/${encodeURIComponent(record.id)}/comments`, { credentials: 'include' }).then((r) => r.json()),
      ]);
      const relGroups: RelatedTableGroup[] = relRes.success && Array.isArray(relRes.data) ? relRes.data : [];
      setGroups(relGroups);
      setAudits(audRes.success && Array.isArray(audRes.data) ? audRes.data : []);
      setComments(cmtRes.success && Array.isArray(cmtRes.data) ? cmtRes.data : []);

      const allCols = [...columns, ...relGroups.flatMap((g) => g.columns)];
      if (allCols.some((c) => c.refTableId)) {
        await batchFetchLookups(allCols);
      }
    } finally {
      setLoading(false);
    }
  }, [record, columns, isUserAdmin, effectiveCanEdit, effectiveCanApprove]);

  useEffect(() => {
    if (!open || !record) return;
    let cancelled = false;
    void Promise.resolve().then(() => {
      if (cancelled) return;
      setWfActionMode(null);
      setWfRejectReason('');
      setWfFeedback(null);
      setTab('info');
      setQuickFormKey(null);
      setEditingChildId(null);
      setQuickError('');
      void loadRelationsAndLogs();
    });
    return () => {
      cancelled = true;
    };
  }, [open, record, loadRelationsAndLogs]);

  const runDrawerApproval = async (
    action: 'submit' | 'approve' | 'reject' | 'return' | 'recall' | 'cancel',
    reasonText?: string
  ) => {
    if (!activeRecord) return;
    setWfBusy(true);
    setWfFeedback(null);
    try {
      const url = action === 'submit'
        ? `/api/records/${activeRecord.id}/submit-approval`
        : action === 'recall'
          ? `/api/records/${activeRecord.id}/recall-approval`
          : action === 'cancel'
            ? `/api/records/${activeRecord.id}/cancel-approval`
            : `/api/records/${activeRecord.id}/approve`;
      const body = action === 'submit'
        ? {}
        : action === 'recall' || action === 'cancel'
          ? { expectedVersion: activeRecord.version, reason: reasonText }
          : {
              approve: action === 'approve',
              action: action === 'return' ? 'RETURN' : action === 'reject' ? 'REJECT' : 'APPROVE',
              reason: action === 'reject'
                ? (reasonText?.trim() || 'Từ chối phê duyệt')
                : action === 'return'
                  ? (reasonText?.trim() || 'Trả lại bước trước để bổ sung')
                  : 'Phê duyệt',
              expectedVersion: activeRecord.version,
              expectedStepOrder: activeRecord.currentStepOrder,
            };
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(body),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setLiveRecord(res.data);
        setWfActionMode(null);
        setWfRejectReason('');
        setWfFeedback({
          ok: true,
          msg: action === 'recall'
            ? 'Đã thu hồi trình duyệt.'
            : action === 'cancel'
              ? 'Đã hủy hồ sơ.'
              : action === 'return'
                ? 'Đã trả lại bước trước.'
                : 'Đã cập nhật trạng thái quy trình.',
        });
        onChanged?.();
      } else {
        setWfFeedback({ ok: false, msg: res.message || 'Thao tác thất bại.' });
      }
    } finally {
      setWfBusy(false);
    }
  };

  const restoreAuditField = async (auditId: string, fieldCode: string) => {
    if (!activeRecord || restoringAuditKey) return;
    const key = `${auditId}_${fieldCode}`;
    setRestoringAuditKey(key);
    try {
      const res = await fetch(`/api/records/${activeRecord.id}/restore-fields`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          auditId,
          fields: [fieldCode],
          expectedVersion: activeRecord.version,
        }),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setLiveRecord(res.data);
        await loadRelationsAndLogs();
        onChanged?.();
        setWfFeedback({ ok: true, msg: `Đã khôi phục trường '${fieldCode}' từ lịch sử.` });
      } else {
        setWfFeedback({ ok: false, msg: res.message || 'Không thể khôi phục trường dữ liệu này.' });
      }
    } finally {
      setRestoringAuditKey(null);
    }
  };

  const openQuickCreate = (g: RelatedTableGroup, groupKey: string) => {
    if (quickFormKey === groupKey && !editingChildId) {
      setQuickFormKey(null);
      return;
    }
    const init: Record<string, unknown> = {};
    g.columns.forEach((c) => {
      if (c.defaultValue !== null && c.defaultValue !== undefined) {
        if (c.dataType === 'BOOLEAN') init[c.columnCode] = c.defaultValue === 'true';
        else if (c.dataType === 'NUMBER' || c.dataType === 'CURRENCY') init[c.columnCode] = Number(c.defaultValue);
        else init[c.columnCode] = c.defaultValue;
      }
    });
    if (g.viaColumnCode && g.targetMatchValue) {
      init[g.viaColumnCode] = g.targetMatchValue;
    }
    setEditingChildId(null);
    setQuickFormData(init);
    setQuickError('');
    setQuickFormKey(groupKey);
  };

  const openQuickEdit = (g: RelatedTableGroup, groupKey: string, rec: AppRecord) => {
    setEditingChildId(rec.id);
    setQuickFormData({ ...(rec.data || {}) });
    setQuickError('');
    setQuickFormKey(groupKey);
  };

  const submitQuickForm = async (e: React.FormEvent, g: RelatedTableGroup) => {
    e.preventDefault();
    if (!g.moduleId) return;
    setQuickSaving(true);
    setQuickError('');
    try {
      const payload = { ...quickFormData };
      if (!editingChildId && g.viaColumnCode && g.targetMatchValue) {
        payload[g.viaColumnCode] = g.targetMatchValue;
      }
      const keyCol = g.columns.find((c) => c.isIdentityKey)?.columnCode;
      const res = await fetch('/api/records', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          id: editingChildId || undefined,
          appId: g.appId,
          moduleId: g.moduleId,
          tableId: g.tableId,
          recordKey: keyCol ? payload[keyCol] : undefined,
          data: payload,
        }),
      }).then((r) => r.json());
      if (!res.success) {
        setQuickError(res.message || 'Không thể lưu bản ghi liên kết.');
        return;
      }
      setQuickFormKey(null);
      setEditingChildId(null);
      await loadRelationsAndLogs();
      onChanged?.();
    } finally {
      setQuickSaving(false);
    }
  };

  const deleteChildRecord = async (recId: string) => {
    const res = await fetch(`/api/records/${encodeURIComponent(recId)}`, {
      method: 'DELETE',
      credentials: 'include',
    }).then((r) => r.json());
    if (res.success) {
      await loadRelationsAndLogs();
      onChanged?.();
    }
  };

  const submitComment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!record || !commentText.trim()) return;
    setPostingComment(true);
    try {
      const res = await fetch(`/api/records/${encodeURIComponent(record.id)}/comments`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ content: commentText.trim() }),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setComments((prev) => [...prev, res.data]);
        setCommentText('');
      }
    } finally {
      setPostingComment(false);
    }
  };

  const titleColumn = columns.find((c) => !c.isIdentityKey && (c.dataType === 'STRING' || c.columnCode.includes('ten') || c.columnCode.includes('tieu_de')));
  const headlineText = titleColumn && activeRecord?.data?.[titleColumn.columnCode]
    ? `${activeRecord.data[titleColumn.columnCode]} (${activeRecord?.recordKey || activeRecord?.id})`
    : `${activeRecord?.recordKey || activeRecord?.id || ''}`;

  const activeRelationGroup = groups.find((_, idx) => tab === `rel_${idx}`);

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 800, lg: 940 }, p: 0 } } }}
    >
      <div style={{ display: 'flex', flexDirection: 'column', height: '100%', backgroundColor: 'var(--bg-primary)' }}>
        {/* Header */}
        <div
          style={{
            padding: '16px 22px',
            backgroundColor: 'var(--bg-surface)',
            borderBottom: '1px solid var(--border-subtle)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 12,
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, minWidth: 0 }}>
            <div
              style={{
                width: 40,
                height: 40,
                borderRadius: 10,
                backgroundColor: '#e8f0fe',
                color: '#1a73e8',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
              }}
            >
              <AccountTreeRoundedIcon sx={{ fontSize: 21 }} />
            </div>
            <div style={{ minWidth: 0 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                <span style={{ fontSize: 16, fontWeight: 700, color: 'var(--text-primary)' }}>
                  {headlineText}
                </span>
                {activeRecord && <StatusBadge status={activeRecord.approvalStatus || activeRecord.status} size="sm" />}
              </div>
              <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
                {tableDisplayName} • Hồ sơ chi tiết đa Tab ({groups.length} bảng liên kết)
              </div>
            </div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexShrink: 0 }}>
            {record && (
              <Link
                href={`/apps/${encodeURIComponent(record.appId)}/records/${encodeURIComponent(record.id)}`}
                className="btn btn-outline btn-xs"
                onClick={onClose}
                title="Mở trang chi tiết toàn màn hình"
              >
                <OpenInNewRoundedIcon sx={{ fontSize: 14 }} />
                Toàn trang
              </Link>
            )}
            <button type="button" className="btn btn-outline btn-xs" onClick={onClose} aria-label="Đóng chi tiết bản ghi">
              <CloseRoundedIcon sx={{ fontSize: 16 }} />
            </button>
          </div>
        </div>

        {/* Multi-tab Navigation Strip */}
        <div
          style={{
            padding: '10px 20px',
            backgroundColor: 'var(--bg-surface)',
            borderBottom: '1px solid var(--border-light)',
            display: 'flex',
            gap: 6,
            overflowX: 'auto',
            flexShrink: 0,
          }}
        >
          <button
            type="button"
            className={`btn btn-xs ${tab === 'info' ? 'btn-primary' : 'btn-outline'}`}
            style={{ whiteSpace: 'nowrap', flexShrink: 0 }}
            onClick={() => setTab('info')}
          >
            <BadgeRoundedIcon sx={{ fontSize: 14 }} />
            Thông tin chung
          </button>

          {groups.map((g, idx) => {
            const tabId = `rel_${idx}`;
            const isSelected = tab === tabId;
            return (
              <button
                key={`${g.tableId}_${idx}`}
                type="button"
                className={`btn btn-xs ${isSelected ? 'btn-primary' : 'btn-outline'}`}
                style={{ whiteSpace: 'nowrap', flexShrink: 0 }}
                onClick={() => {
                  setTab(tabId);
                  setQuickFormKey(null);
                  setEditingChildId(null);
                }}
              >
                <span>{g.tableDisplayName}</span>
                <span
                  style={{
                    marginLeft: 4,
                    padding: '0 6px',
                    borderRadius: 999,
                    fontSize: 10.5,
                    fontWeight: 700,
                    backgroundColor: isSelected ? 'rgba(255,255,255,0.22)' : 'var(--info-bg)',
                    color: isSelected ? '#ffffff' : 'var(--info-text)',
                  }}
                >
                  {g.records.length}
                </span>
              </button>
            );
          })}

          <button
            type="button"
            className={`btn btn-xs ${tab === 'workflow' ? 'btn-primary' : 'btn-outline'}`}
            style={{ whiteSpace: 'nowrap', flexShrink: 0 }}
            onClick={() => setTab('workflow')}
          >
            <FactCheckRoundedIcon sx={{ fontSize: 14 }} />
            Quy trình ({record?.workflowHistory?.length || 0})
          </button>

          <button
            type="button"
            className={`btn btn-xs ${tab === 'comments' ? 'btn-primary' : 'btn-outline'}`}
            style={{ whiteSpace: 'nowrap', flexShrink: 0 }}
            onClick={() => setTab('comments')}
          >
            <ForumRoundedIcon sx={{ fontSize: 14 }} />
            Thảo luận ({comments.length})
          </button>

          <button
            type="button"
            className={`btn btn-xs ${tab === 'audit' ? 'btn-primary' : 'btn-outline'}`}
            style={{ whiteSpace: 'nowrap', flexShrink: 0 }}
            onClick={() => setTab('audit')}
          >
            <HistoryRoundedIcon sx={{ fontSize: 14 }} />
            Lịch sử ({audits.length})
          </button>
        </div>

        {/* Tab Content Body */}
        <div style={{ padding: 20, overflowY: 'auto', flex: 1, display: 'flex', flexDirection: 'column', gap: 16 }}>
          {loading && tab !== 'info' ? (
            <div className="card" style={{ textAlign: 'center', padding: 32, color: 'var(--text-muted)' }}>
              Đang tải dữ liệu liên kết...
            </div>
          ) : tab === 'info' ? (
            record && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                {enabledPlugins.length > 0 && (
                  <div className="card" style={{ padding: 14, backgroundColor: 'var(--bg-secondary)', border: '1px solid var(--border-subtle)' }}>
                    <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 8 }}>
                      THAO TÁC TÍCH HỢP 1-CLICK
                    </div>
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
                      {enabledPlugins.map((plg) => {
                        const cat = (plg.category || '').toUpperCase();
                        const op = cat === 'EINVOICE'
                          ? 'issue_einvoice'
                          : cat === 'DIGITAL_SIGN'
                            ? 'request_sign'
                            : cat === 'SMS_VN'
                              ? 'send_sms'
                              : 'create_payment';
                        const label = cat === 'EINVOICE'
                          ? `Xuất HĐĐT (${plg.name})`
                          : cat === 'DIGITAL_SIGN'
                            ? `Ký số (${plg.name})`
                            : cat === 'SMS_VN'
                              ? `Gửi SMS (${plg.name})`
                              : `Thanh toán (${plg.name})`;
                        return (
                          <button
                            key={plg.id}
                            type="button"
                            disabled={pluginRunning !== null}
                            className="btn btn-xs btn-outline"
                            style={{ backgroundColor: 'var(--bg-surface)' }}
                            onClick={async () => {
                              setPluginRunning(plg.code);
                              setPluginResult(null);
                              try {
                                const d = record.data || {};
                                const fm = plg.fieldMapping || {};
                                const amount = (fm.amountField ? d[fm.amountField] : undefined) ?? d.amount ?? d.tong_tien ?? d.so_tien ?? d.thanh_tien ?? 0;
                                const phone = (fm.phoneField ? d[fm.phoneField] : undefined) ?? d.phone ?? d.sdt ?? d.so_dien_thoai ?? '';
                                const orderRef = (fm.orderRefField ? d[fm.orderRefField] : undefined) ?? record.recordKey ?? record.id;
                                const res = await fetch(`/api/integrations/plugins/${encodeURIComponent(plg.code)}/execute`, {
                                  method: 'POST',
                                  headers: { 'Content-Type': 'application/json' },
                                  credentials: 'include',
                                  body: JSON.stringify({
                                    operation: op,
                                    appId: activeRecord?.appId || record.appId,
                                    recordId: record.id,
                                    tableId: record.tableId,
                                    orderRef,
                                    amount,
                                    phone,
                                    message: `Ho so ${orderRef} da duoc xu ly.`,
                                  }),
                                }).then((r) => r.json());
                                if (res?.data) {
                                  setPluginResult(res.data);
                                  if (res.updatedRecord) {
                                    setLiveRecord(res.updatedRecord);
                                    onChanged?.();
                                  }
                                } else {
                                  setPluginResult({
                                    success: false,
                                    providerCode: plg.code,
                                    operation: op,
                                    message: res?.message || 'Thực thi plugin thất bại.',
                                    data: {},
                                  });
                                }
                              } catch (err) {
                                setPluginResult({
                                  success: false,
                                  providerCode: plg.code,
                                  operation: op,
                                  message: err instanceof Error ? err.message : 'Lỗi kết nối.',
                                  data: {},
                                });
                              } finally {
                                setPluginRunning(null);
                              }
                            }}
                          >
                            {pluginRunning === plg.code ? 'Đang xử lý...' : label}
                          </button>
                        );
                      })}
                    </div>

                    {pluginResult && (
                      <div
                        style={{
                          marginTop: 10,
                          padding: 12,
                          borderRadius: 8,
                          backgroundColor: pluginResult.success ? 'var(--success-bg)' : 'var(--danger-bg)',
                          border: `1px solid ${pluginResult.success ? 'var(--success-border)' : 'var(--danger-border)'}`,
                          fontSize: 12.5,
                        }}
                      >
                        <div style={{ fontWeight: 700, color: pluginResult.success ? 'var(--success-text)' : 'var(--danger-text)', marginBottom: 4 }}>
                          {pluginResult.message}
                        </div>
                        {Boolean(pluginResult.data?.qrUrl) && (
                          <div style={{ marginTop: 8 }}>
                            <img
                              src={String(pluginResult.data.qrUrl)}
                              alt="VietQR"
                              style={{ maxWidth: 200, borderRadius: 8, border: '1px solid var(--border-subtle)', backgroundColor: '#fff' }}
                            />
                          </div>
                        )}
                        {Boolean(pluginResult.data?.paymentUrl) && (
                          <div style={{ marginTop: 6 }}>
                            <a href={String(pluginResult.data.paymentUrl)} target="_blank" rel="noreferrer" style={{ color: 'var(--accent-primary)', fontWeight: 600 }}>
                              Mở liên kết thanh toán ↗
                            </a>
                          </div>
                        )}
                        {Boolean(pluginResult.data?.invoiceNo) && (
                          <div style={{ marginTop: 4, color: 'var(--text-secondary)' }}>
                            Số HĐ: <strong>{String(pluginResult.data.invoiceNo)}</strong> • Mã tra cứu: <strong>{String(pluginResult.data.lookupCode ?? '')}</strong>
                          </div>
                        )}
                        {Boolean(pluginResult.data?.signTransactionId) && (
                          <div style={{ marginTop: 4, color: 'var(--text-secondary)', display: 'flex', flexDirection: 'column', gap: 6 }}>
                            <div>
                              Mã GD ký số: <strong>{String(pluginResult.data.signTransactionId)}</strong> ({String(pluginResult.data.signStatus ?? '')})
                            </div>
                            {pluginResult.data.signStatus === 'PENDING_USER_CONFIRM' && Boolean(pluginResult.data.documentHash) && (
                              <div>
                                <button
                                  type="button"
                                  className="btn btn-outline btn-xs"
                                  disabled={pluginRunning !== null}
                                  onClick={async () => {
                                    setPluginRunning(pluginResult.providerCode || 'local-sign');
                                    try {
                                      const signOut = await requestLocalTokenSign({
                                        documentId: record.recordKey || record.id,
                                        documentHash: String(pluginResult.data?.documentHash || ''),
                                        providerCode: pluginResult.providerCode,
                                      });
                                      if (!signOut.success || !signOut.signatureBase64) {
                                        setPluginResult({
                                          success: false,
                                          providerCode: pluginResult.providerCode,
                                          operation: 'complete_local_sign',
                                          message: signOut.message,
                                          data: pluginResult.data || {},
                                        });
                                        return;
                                      }
                                      const completed = await fetch(`/api/integrations/plugins/${encodeURIComponent(pluginResult.providerCode)}/execute`, {
                                        method: 'POST',
                                        headers: { 'Content-Type': 'application/json' },
                                        credentials: 'include',
                                        body: JSON.stringify({
                                          operation: 'complete_local_sign',
                                          appId: activeRecord?.appId || record.appId,
                                          recordId: record.id,
                                          tableId: record.tableId,
                                          signTransactionId: signOut.signTransactionId || pluginResult.data?.signTransactionId,
                                          documentHash: pluginResult.data?.documentHash,
                                          signatureBase64: signOut.signatureBase64,
                                          certificateSubject: signOut.certificateSubject,
                                          certificateSerial: signOut.certificateSerial,
                                          signedAt: signOut.signedAt,
                                        }),
                                      }).then((r) => r.json());
                                      if (completed?.data) {
                                        setPluginResult(completed.data);
                                        if (completed.updatedRecord) {
                                          setLiveRecord(completed.updatedRecord);
                                          onChanged?.();
                                        }
                                      }
                                    } finally {
                                      setPluginRunning(null);
                                    }
                                  }}
                                >
                                  Ký bằng USB Token cục bộ (PKCS#11)
                                </button>
                              </div>
                            )}
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                )}

                <div className="card" style={{ padding: 18 }}>
                  <div style={{ fontSize: 12.5, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 14, display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
                    <span>TOÀN BỘ THÔNG TIN HỒ SƠ ({columns.length} trường)</span>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        disabled={aiLoading}
                        onClick={async () => {
                          setAiLoading(true);
                          try {
                            const res = await fetch(`/api/records/${encodeURIComponent(record.id)}/ai-summary`, {
                              method: 'POST',
                              credentials: 'include',
                            }).then((r) => r.json());
                            if (res.success && res.data) setAiSummary(res.data);
                          } finally {
                            setAiLoading(false);
                          }
                        }}
                      >
                        {aiLoading ? 'Đang tóm tắt...' : '✨ Tóm tắt AI'}
                      </button>
                      <span style={{ fontSize: 11.5, fontWeight: 500, color: 'var(--text-muted)' }}>
                        Cập nhật: {activeRecord?.updatedAt ? new Date(activeRecord.updatedAt).toLocaleString('vi-VN') : '—'} bởi @{activeRecord?.updatedBy || activeRecord?.createdBy}
                      </span>
                    </div>
                  </div>
                  {aiSummary && (
                    <div style={{ marginBottom: 14, padding: 12, borderRadius: 8, backgroundColor: 'var(--info-bg)', border: '1px solid var(--info-border)', fontSize: 12.5, whiteSpace: 'pre-wrap' }}>
                      <div style={{ fontWeight: 700, color: 'var(--info-text)', marginBottom: 4 }}>
                        Tóm tắt nhanh ({aiSummary.engine})
                      </div>
                      <div style={{ color: 'var(--text-primary)', lineHeight: 1.5 }}>{aiSummary.summary}</div>
                    </div>
                  )}
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '14px 18px' }}>
                    {columns.map((c) => (
                      <div
                        key={c.id}
                        style={{
                          gridColumn: (c.dataType || '').toUpperCase() === 'TEXTAREA' ? '1 / -1' : undefined,
                          padding: '8px 10px',
                          borderRadius: 8,
                          backgroundColor: 'var(--bg-secondary)',
                          border: '1px solid var(--border-light)',
                        }}
                      >
                        <div style={{ color: 'var(--text-muted)', fontSize: 11.5, marginBottom: 3 }}>
                          {c.columnName}
                          {c.isIdentityKey && (
                            <span style={{ marginLeft: 6, fontSize: 10, color: '#1a73e8', fontWeight: 700 }}>KEY</span>
                          )}
                        </div>
                        <div style={{ fontWeight: 600, fontSize: 13.5, color: 'var(--text-primary)', wordBreak: 'break-word' }}>
                          {formatFieldDisplay(c, activeRecord?.data?.[c.columnCode])}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>

                {/* Quick Summary Overview Cards of All Related Tabs */}
                {groups.length > 0 && (
                  <div className="card" style={{ padding: 16 }}>
                    <div style={{ fontSize: 12.5, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 10 }}>
                      TỔNG HỢP DỮ LIỆU LIÊN KẾT THEO CHUYÊN MỤC (nhấn để mở Tab)
                    </div>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(185px, 1fr))', gap: 10 }}>
                      {groups.map((g, idx) => (
                        <button
                          key={`${g.tableId}_sum_${idx}`}
                          type="button"
                          onClick={() => setTab(`rel_${idx}`)}
                          style={{
                            padding: '10px 12px',
                            borderRadius: 10,
                            border: '1px solid var(--border-subtle)',
                            borderLeft: `4px solid ${g.appColor}`,
                            backgroundColor: 'var(--bg-surface)',
                            textAlign: 'left',
                            cursor: 'pointer',
                          }}
                        >
                          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 4 }}>
                            <span className="badge" style={{ fontSize: 10.5, backgroundColor: 'var(--bg-secondary)', color: g.appColor }}>
                              {g.appCode}
                            </span>
                            <strong style={{ fontSize: 16, color: 'var(--text-primary)' }}>{g.records.length}</strong>
                          </div>
                          <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-primary)' }}>
                            {g.tableDisplayName}
                          </div>
                        </button>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )
          ) : activeRelationGroup ? (
            (() => {
              const g = activeRelationGroup;
              const isQuickOpen = quickFormKey === tab;
              const showRowActions = Boolean(g.canEdit || g.canDelete);
              const numericSums = g.columns.map((col) => {
                const dt = (col.dataType || '').toUpperCase();
                if ((dt !== 'CURRENCY' && dt !== 'NUMBER') || col.isIdentityKey) return null;
                let sum = 0;
                let count = 0;
                for (const rec of g.records) {
                  const val = Number(rec.data?.[col.columnCode]);
                  if (Number.isFinite(val) && rec.data?.[col.columnCode] !== null && rec.data?.[col.columnCode] !== '') {
                    sum += val;
                    count++;
                  }
                }
                return count > 0 ? { dt, sum } : null;
              });
              const hasAnySummary = g.records.length > 0 && numericSums.some((x) => x !== null);

              return (
                <div className="card" style={{ padding: 18, borderTop: `3px solid ${g.appColor}` }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10, marginBottom: 14 }}>
                    <div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <span className="badge" style={{ backgroundColor: 'var(--bg-secondary)', color: g.appColor }}>
                          {g.appCode}
                        </span>
                        <strong style={{ fontSize: 15 }}>{g.tableDisplayName}</strong>
                        <span className="badge badge-info">{g.records.length} bản ghi</span>
                      </div>
                      <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 3 }}>
                        Liên kết qua trường: <strong>{g.viaColumnName}</strong> = <code>{g.targetMatchValue || record?.recordKey}</code>
                      </div>
                    </div>
                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                      {g.canCreate && g.moduleId && g.viaColumnCode && (
                        <button
                          type="button"
                          className="btn btn-primary btn-xs"
                          onClick={() => openQuickCreate(g, tab)}
                        >
                          <AddRoundedIcon sx={{ fontSize: 14 }} />
                          {isQuickOpen && !editingChildId ? 'Đóng form thêm' : `Thêm ${g.tableDisplayName}`}
                        </button>
                      )}
                      <Link href={`/apps/${g.appCode}?table=${encodeURIComponent(g.tableId)}`} className="btn btn-outline btn-xs">
                        Mở bảng đầy đủ <OpenInNewRoundedIcon sx={{ fontSize: 13 }} />
                      </Link>
                    </div>
                  </div>

                  {/* Inline Quick-Create or Edit Child Record Form */}
                  {isQuickOpen && (
                    <form
                      onSubmit={(e) => submitQuickForm(e, g)}
                      style={{
                        marginBottom: 16,
                        padding: 14,
                        borderRadius: 10,
                        backgroundColor: 'var(--bg-secondary)',
                        border: '1px solid var(--border-subtle)',
                      }}
                    >
                      <div style={{ fontSize: 13, fontWeight: 700, marginBottom: 10, color: 'var(--text-primary)' }}>
                        {editingChildId ? `Chỉnh sửa bản ghi — ${g.tableDisplayName}` : `Thêm nhanh bản ghi vào ${g.tableDisplayName}`}
                      </div>
                      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12 }}>
                        {g.columns
                          .filter((col) => col.showInForm !== false)
                          .map((col) => {
                            const isLockedParent = col.columnCode === g.viaColumnCode;
                            const isRoleReadOnly = !isUserAdmin && (col.readOnlyForRoles || []).some((rr) =>
                              userRoles.some((ur) => ur.toUpperCase() === rr.toUpperCase()),
                            );
                            const isDisabled = isLockedParent || isRoleReadOnly;
                            return (
                              <div
                                key={col.id}
                                className="form-group"
                                style={{
                                  marginBottom: 0,
                                  gridColumn: col.dataType === 'TEXTAREA' ? '1 / -1' : undefined,
                                }}
                              >
                                <label className="form-label">
                                  {col.columnName} {col.isRequired && !isDisabled && <span style={{ color: '#d93025' }}>*</span>}
                                  {!isLockedParent && isRoleReadOnly && (
                                    <span style={{ fontSize: 10.5, color: 'var(--text-muted)', marginLeft: 5 }}>(Chỉ đọc)</span>
                                  )}
                                </label>
                                <DynamicFieldInput
                                  column={col}
                                  value={quickFormData[col.columnCode]}
                                  disabled={isDisabled}
                                  onChange={(val) => setQuickFormData((prev) => ({ ...prev, [col.columnCode]: val }))}
                                  onSelectDetail={(opt) => {
                                    if (!opt.data) return;
                                    setQuickFormData((prev) => {
                                      const next = { ...prev };
                                      for (const c of g.columns) {
                                        if (c.columnCode === col.columnCode || c.isIdentityKey || c.formula || c.columnCode === g.viaColumnCode) continue;
                                        const cur = next[c.columnCode];
                                        if (cur === undefined || cur === null || cur === '') {
                                          const v = opt.data?.[c.columnCode];
                                          if (v !== undefined && v !== null && v !== '') next[c.columnCode] = v;
                                        }
                                      }
                                      return next;
                                    });
                                  }}
                                />
                              </div>
                            );
                          })}
                      </div>
                      {quickError && (
                        <div style={{ marginTop: 10, padding: '8px 10px', borderRadius: 8, backgroundColor: 'var(--danger-bg)', color: 'var(--danger-text)', fontSize: 12 }}>
                          {quickError}
                        </div>
                      )}
                      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 12 }}>
                        <button type="button" className="btn btn-outline btn-xs" onClick={() => { setQuickFormKey(null); setEditingChildId(null); }}>
                          Hủy
                        </button>
                        <button type="submit" className="btn btn-primary btn-xs" disabled={quickSaving}>
                          <SaveRoundedIcon sx={{ fontSize: 14 }} />
                          {quickSaving ? 'Đang lưu...' : (editingChildId ? 'Cập nhật' : 'Lưu bản ghi')}
                        </button>
                      </div>
                    </form>
                  )}

                  {g.records.length === 0 ? (
                    <div style={{ padding: 24, textAlign: 'center', color: 'var(--text-muted)', fontSize: 13, backgroundColor: 'var(--bg-secondary)', borderRadius: 10 }}>
                      Chưa có dữ liệu nào trong mục <strong>{g.tableDisplayName}</strong> cho hồ sơ này.
                    </div>
                  ) : (
                    <div className="table-responsive">
                      <table className="data-table">
                        <thead>
                          <tr>
                            {g.columns.map((col) => (
                              <th key={col.id}>{col.columnName}</th>
                            ))}
                            <th>Trạng thái</th>
                            {showRowActions && <th style={{ textAlign: 'center' }}>Thao tác</th>}
                          </tr>
                        </thead>
                        <tbody>
                          {g.records.map((rec) => (
                            <tr key={rec.id}>
                              {g.columns.map((col) => (
                                <td key={col.id} style={{ maxWidth: 260, wordBreak: 'break-word' }}>
                                  {formatFieldDisplay(col, rec.data?.[col.columnCode])}
                                </td>
                              ))}
                              <td>
                                <StatusBadge status={rec.status} size="sm" />
                              </td>
                              {showRowActions && (
                                <td style={{ textAlign: 'center', whiteSpace: 'nowrap' }}>
                                  <div style={{ display: 'inline-flex', gap: 4 }}>
                                    {g.canEdit && (
                                      <button
                                        type="button"
                                        className="btn btn-outline btn-xs"
                                        title="Sửa dòng này"
                                        onClick={() => openQuickEdit(g, tab, rec)}
                                      >
                                        <EditRoundedIcon sx={{ fontSize: 13 }} />
                                      </button>
                                    )}
                                    {g.canDelete && (
                                      <button
                                        type="button"
                                        className="btn btn-outline btn-xs"
                                        style={{ color: '#d93025' }}
                                        title="Xóa vào thùng rác"
                                        onClick={() => deleteChildRecord(rec.id)}
                                      >
                                        <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} />
                                      </button>
                                    )}
                                  </div>
                                </td>
                              )}
                            </tr>
                          ))}
                        </tbody>
                        {hasAnySummary && (
                          <tfoot>
                            <tr style={{ backgroundColor: 'var(--bg-secondary)', fontWeight: 700 }}>
                              {g.columns.map((col, ci) => {
                                const info = numericSums[ci];
                                if (ci === 0 && !info) {
                                  return <td key={col.id} style={{ fontSize: 12 }}>Tổng cộng ({g.records.length})</td>;
                                }
                                return (
                                  <td key={col.id} style={{ fontSize: 12.5, color: 'var(--text-primary)' }}>
                                    {info
                                      ? info.dt === 'CURRENCY'
                                        ? `${info.sum.toLocaleString('vi-VN')} đ`
                                        : info.sum.toLocaleString('vi-VN')
                                      : ''}
                                  </td>
                                );
                              })}
                              <td />
                              {showRowActions && <td />}
                            </tr>
                          </tfoot>
                        )}
                      </table>
                    </div>
                  )}
                </div>
              );
            })()
          ) : tab === 'workflow' ? (() => {
            const st = (activeRecord?.approvalStatus || 'DRAFT').toUpperCase();
            const isOwner = Boolean(
              user?.username && activeRecord?.createdBy && user.username.toLowerCase() === activeRecord.createdBy.toLowerCase()
            );
            const canRecallOrCancel = isOwner || isUserAdmin;
            return (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <div className="card" style={{ padding: 14 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
                  <strong style={{ fontSize: 13.5 }}>Trạng thái Quy trình Hiện tại</strong>
                  <StatusBadge
                    status={
                      st === 'APPROVED'
                        ? 'ACTIVE'
                        : st === 'REJECTED' || st === 'CANCELLED'
                        ? 'ERROR'
                        : 'PENDING'
                    }
                    label={activeRecord?.approvalStatus || 'DRAFT'}
                    size="sm"
                  />
                </div>
                {st === 'PENDING' && activeRecord && (
                  <div style={{ fontSize: 12.5, color: 'var(--text-secondary)', marginBottom: 8 }}>
                    Đang chờ tại:{' '}
                    <strong>
                      {activeRecord.totalWorkflowSteps && activeRecord.totalWorkflowSteps > 1
                        ? `Bước ${activeRecord.currentStepOrder || 1}/${activeRecord.totalWorkflowSteps}: `
                        : ''}
                      {activeRecord.currentStepName || 'Phê duyệt'}
                    </strong>
                    {activeRecord.currentStepApproverRoles && activeRecord.currentStepApproverRoles.length > 0 && (
                      <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 4 }}>
                        Vai trò được duyệt: {activeRecord.currentStepApproverRoles.join(', ')}
                      </div>
                    )}
                    {activeRecord.stepApprovals && activeRecord.stepApprovals.length > 0 && (
                      <div style={{ fontSize: 11.5, color: 'var(--info-text)', marginTop: 4 }}>
                        Phiếu đồng duyệt bước hiện tại ({activeRecord.stepApprovals.length}):{' '}
                        {activeRecord.stepApprovals.map((v) => `@${v.actor}${v.delegatedFrom ? ` (thay @${v.delegatedFrom})` : ''}`).join(', ')}
                      </div>
                    )}
                  </div>
                )}
                {activeRecord?.rejectionReason && (
                  <div style={{ marginTop: 6, marginBottom: 8, padding: '6px 10px', borderRadius: 8, backgroundColor: 'var(--danger-bg)', color: 'var(--danger-text)', fontSize: 12 }}>
                    Lý do: {activeRecord.rejectionReason}
                  </div>
                )}

                {wfFeedback && (
                  <div
                    style={{
                      marginBottom: 8,
                      padding: '6px 10px',
                      borderRadius: 8,
                      fontSize: 12,
                      backgroundColor: wfFeedback.ok ? 'rgba(19, 115, 51, 0.1)' : 'rgba(197, 34, 31, 0.1)',
                      color: wfFeedback.ok ? '#137333' : '#c5221f',
                    }}
                  >
                    {wfFeedback.msg}
                  </div>
                )}

                <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 8 }}>
                  {(st === 'DRAFT' || st === 'REJECTED' || st === 'RECALLED') && effectiveCanEdit && (
                    <button
                      type="button"
                      className="btn btn-primary btn-xs"
                      disabled={wfBusy}
                      onClick={() => void runDrawerApproval('submit')}
                    >
                      <FactCheckRoundedIcon sx={{ fontSize: 14 }} /> {st === 'DRAFT' ? 'Trình duyệt' : 'Nộp lại duyệt'}
                    </button>
                  )}
                  {st === 'PENDING' && (effectiveCanApprove || isUserAdmin) && (
                    <>
                      <button
                        type="button"
                        className="btn btn-primary btn-xs"
                        style={{ backgroundColor: '#137333' }}
                        disabled={wfBusy}
                        onClick={() => void runDrawerApproval('approve')}
                      >
                        <CheckCircleRoundedIcon sx={{ fontSize: 14 }} /> Phê duyệt
                      </button>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        style={{ color: '#b06000', borderColor: 'var(--warning-border)' }}
                        disabled={wfBusy}
                        onClick={() => {
                          setWfActionMode((m) => (m === 'return' ? null : 'return'));
                        }}
                      >
                        <UndoRoundedIcon sx={{ fontSize: 14 }} /> Trả lại bước trước
                      </button>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        style={{ color: '#c5221f', borderColor: '#f5c2c7' }}
                        disabled={wfBusy}
                        onClick={() => {
                          setWfActionMode((m) => (m === 'reject' ? null : 'reject'));
                        }}
                      >
                        <CancelRoundedIcon sx={{ fontSize: 14 }} /> Từ chối
                      </button>
                    </>
                  )}
                  {st === 'PENDING' && canRecallOrCancel && (
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      disabled={wfBusy}
                      onClick={() => void runDrawerApproval('recall')}
                    >
                      <UndoRoundedIcon sx={{ fontSize: 14 }} /> Thu hồi
                    </button>
                  )}
                  {(st === 'PENDING' || st === 'REJECTED' || st === 'RECALLED') && canRecallOrCancel && (
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      style={{ color: 'var(--text-muted)' }}
                      disabled={wfBusy}
                      onClick={() => void runDrawerApproval('cancel')}
                    >
                      <BlockRoundedIcon sx={{ fontSize: 14 }} /> Hủy hồ sơ
                    </button>
                  )}
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    disabled={triageLoading || !activeRecord?.id}
                    onClick={async () => {
                      if (!activeRecord?.id) return;
                      setTriageLoading(true);
                      try {
                        const res = await fetch(`/api/records/${encodeURIComponent(activeRecord.id)}/ai-triage`, {
                          method: 'POST',
                          credentials: 'include',
                        }).then((r) => r.json());
                        if (res?.success && res?.data) setAiTriage(res.data);
                      } finally {
                        setTriageLoading(false);
                      }
                    }}
                  >
                    {triageLoading ? 'AI đang chấm rủi ro...' : '🛡️ AI Đánh giá rủi ro'}
                  </button>
                </div>

                {aiTriage && (
                  <div
                    style={{
                      marginTop: 10,
                      padding: 12,
                      borderRadius: 8,
                      border: '1px solid var(--border-subtle)',
                      backgroundColor:
                        aiTriage.riskLevel === 'HIGH'
                          ? 'var(--danger-bg)'
                          : aiTriage.riskLevel === 'MEDIUM'
                            ? 'var(--warning-bg)'
                            : 'var(--info-bg)',
                      fontSize: 12.5,
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 6,
                    }}
                  >
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 6 }}>
                      <strong>{aiTriage.recommendationLabel}</strong>
                      <span className="badge" style={{ fontSize: 11 }}>
                        Rủi ro: {aiTriage.riskLevel} ({aiTriage.riskScore}/100) • {aiTriage.historicalSampleSize} mẫu LS
                      </span>
                    </div>
                    {aiTriage.anomalies.length > 0 && (
                      <ul style={{ margin: 0, paddingLeft: 18, color: 'var(--danger-text)' }}>
                        {aiTriage.anomalies.map((a, idx) => (
                          <li key={idx}>{a}</li>
                        ))}
                      </ul>
                    )}
                    {aiTriage.summaryBullets.length > 0 && (
                      <ul style={{ margin: 0, paddingLeft: 18, color: 'var(--text-primary)' }}>
                        {aiTriage.summaryBullets.map((b, idx) => (
                          <li key={idx}>{b}</li>
                        ))}
                      </ul>
                    )}
                    <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>Nguồn: {aiTriage.engine}</div>
                  </div>
                )}

                {wfActionMode && st === 'PENDING' && (
                  <div style={{ marginTop: 10, padding: 10, borderRadius: 8, backgroundColor: 'var(--bg-secondary)', border: '1px solid var(--border-light)', display: 'flex', flexDirection: 'column', gap: 8 }}>
                    <input
                      className="form-input"
                      placeholder={wfActionMode === 'return' ? 'Nhập ghi chú yêu cầu bổ sung (khi trả lại bước trước)...' : 'Nhập lý do từ chối...'}
                      value={wfRejectReason}
                      onChange={(e) => setWfRejectReason(e.target.value)}
                    />
                    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 6 }}>
                      <button type="button" className="btn btn-outline btn-xs" onClick={() => setWfActionMode(null)}>Đóng</button>
                      <button
                        type="button"
                        className="btn btn-xs"
                        style={{ backgroundColor: wfActionMode === 'return' ? '#b06000' : '#d93025', color: '#fff', border: 'none' }}
                        disabled={wfBusy}
                        onClick={() => void runDrawerApproval(wfActionMode, wfRejectReason)}
                      >
                        {wfActionMode === 'return' ? 'Xác nhận trả lại' : 'Xác nhận từ chối'}
                      </button>
                    </div>
                  </div>
                )}
              </div>

              {!activeRecord?.workflowHistory || activeRecord.workflowHistory.length === 0 ? (
                <div className="card" style={{ textAlign: 'center', padding: 24, color: 'var(--text-muted)', fontSize: 13 }}>
                  Hồ sơ này chưa có lượt trình duyệt / phê duyệt nào.
                </div>
              ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                  {activeRecord.workflowHistory.map((h, i) => (
                    <div key={i} className="card" style={{ padding: 12, borderLeft: `4px solid ${h.action === 'APPROVE' ? '#1e8e3e' : (h.action === 'REJECT' || h.action === 'CANCEL') ? '#d93025' : h.action === 'RECALL' ? '#f59e0b' : '#1a73e8'}` }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 4 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                          <span className={`badge ${h.action === 'APPROVE' ? 'badge-success' : (h.action === 'REJECT' || h.action === 'CANCEL') ? 'badge-danger' : h.action === 'RECALL' ? 'badge-warning' : 'badge-info'}`}>
                            {h.action === 'APPROVE' ? 'Đã duyệt' : h.action === 'REJECT' ? 'Từ chối' : h.action === 'RECALL' ? 'Thu hồi' : h.action === 'CANCEL' ? 'Đã hủy' : 'Trình duyệt'}
                          </span>
                          <strong style={{ fontSize: 13 }}>{h.stepName}</strong>
                        </div>
                        <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>
                          {new Date(h.at).toLocaleString('vi-VN')}
                        </span>
                      </div>
                      <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                        Thực hiện bởi: <strong>@{h.actor}</strong>
                        {h.actorRole && <> ({h.actorRole})</>}
                      </div>
                      {h.note && <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4 }}>Ghi chú: {h.note}</div>}
                    </div>
                  ))}
                </div>
              )}
            </div>
            );
          })() : tab === 'comments' ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <form onSubmit={submitComment} className="card" style={{ padding: 14, display: 'flex', flexDirection: 'column', gap: 8 }}>
                <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
                  <span style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>Nhắc nhanh:</span>
                  {['admin', 'hr_manager', 'asset_lead', 'staff'].map((u) => (
                    <button
                      key={u}
                      type="button"
                      className="btn btn-outline btn-xs"
                      onClick={() => setCommentText((prev) => `${prev ? prev + ' ' : ''}@${u} `)}
                    >
                      @{u}
                    </button>
                  ))}
                </div>
                <textarea
                  className="form-textarea"
                  rows={2}
                  value={commentText}
                  onChange={(e) => setCommentText(e.target.value)}
                  placeholder="Nhập ghi chú, trao đổi hoặc @username để gửi thông báo trực tiếp..."
                />
                <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                  <button type="submit" className="btn btn-primary btn-sm" disabled={postingComment || !commentText.trim()}>
                    <SendRoundedIcon sx={{ fontSize: 15 }} />
                    {postingComment ? 'Đang gửi...' : 'Gửi bình luận'}
                  </button>
                </div>
              </form>

              {comments.length === 0 ? (
                <div className="card" style={{ textAlign: 'center', padding: 24, color: 'var(--text-muted)', fontSize: 13 }}>
                  Chưa có bình luận nào trên hồ sơ này.
                </div>
              ) : (
                comments.map((c) => (
                  <div key={c.id} className="card" style={{ padding: 12 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
                      <strong style={{ fontSize: 12.5, color: '#1a73e8' }}>@{c.author}</strong>
                      <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>
                        {new Date(c.createdAt).toLocaleString('vi-VN')}
                      </span>
                    </div>
                    <div style={{ fontSize: 13, color: 'var(--text-primary)', whiteSpace: 'pre-wrap' }}>{c.content}</div>
                  </div>
                ))
              )}
            </div>
          ) : audits.length === 0 ? (
            <div className="card" style={{ textAlign: 'center', padding: 28, color: 'var(--text-muted)', fontSize: 13 }}>
              Chưa có lịch sử chỉnh sửa nào được ghi nhận cho bản ghi này.
            </div>
          ) : (
            audits.map((log) => {
              const meta = getAuditActionMeta(log.action);
              const diffs = computeAuditFieldDiffs(log.action, log.oldData, log.newData, columns);
              return (
                <div key={log.id} className="card" style={{ padding: 14, display: 'flex', flexDirection: 'column', gap: 8 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <span className={`badge ${meta.badgeClass}`}>{meta.label}</span>
                      <strong style={{ fontSize: 13 }}>@{log.actor}</strong>
                    </div>
                    <span style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
                      {new Date(log.createdAt).toLocaleString('vi-VN')}
                    </span>
                  </div>
                  {log.summary && (
                    <div style={{ fontSize: 12.5, color: 'var(--text-secondary)' }}>{log.summary}</div>
                  )}
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
                      {diffs.map((d) => (
                        <div key={d.key} style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap', fontSize: 12 }}>
                          <span style={{ fontWeight: 600, color: 'var(--text-secondary)', minWidth: 110 }}>{d.label}:</span>
                          {d.kind === 'changed' ? (
                            <>
                              <span style={{ textDecoration: 'line-through', color: 'var(--danger-text)', backgroundColor: 'var(--danger-bg)', padding: '1px 6px', borderRadius: 4 }}>
                                {formatAuditValue(d.oldVal)}
                              </span>
                              <ArrowForwardRoundedIcon sx={{ fontSize: 13, color: 'var(--text-muted)' }} />
                              <span style={{ color: 'var(--success-text)', backgroundColor: 'var(--success-bg)', padding: '1px 6px', borderRadius: 4, fontWeight: 600 }}>
                                {formatAuditValue(d.newVal)}
                              </span>
                            </>
                          ) : d.kind === 'added' ? (
                            <span style={{ color: 'var(--success-text)', backgroundColor: 'var(--success-bg)', padding: '1px 6px', borderRadius: 4, fontWeight: 500 }}>
                              {formatAuditValue(d.newVal)}
                            </span>
                          ) : (
                            <span style={{ textDecoration: 'line-through', color: 'var(--danger-text)', backgroundColor: 'var(--danger-bg)', padding: '1px 6px', borderRadius: 4 }}>
                              {formatAuditValue(d.oldVal)}
                            </span>
                          )}
                          {effectiveCanEdit && log.oldData && (d.kind === 'changed' || d.kind === 'removed') && (
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              style={{ marginLeft: 'auto', fontSize: 10.5, padding: '1px 7px' }}
                              disabled={restoringAuditKey === `${log.id}_${d.key}`}
                              onClick={() => void restoreAuditField(log.id, d.key)}
                              title="Khôi phục giá trị trước khi thay đổi"
                            >
                              {restoringAuditKey === `${log.id}_${d.key}` ? 'Đang khôi phục...' : 'Khôi phục trường'}
                            </button>
                          )}
                        </div>
                      ))}
                    </div>
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
