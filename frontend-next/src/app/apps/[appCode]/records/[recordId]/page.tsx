'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import PrintRoundedIcon from '@mui/icons-material/PrintRounded';
import FactCheckRoundedIcon from '@mui/icons-material/FactCheckRounded';
import SendRoundedIcon from '@mui/icons-material/SendRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import CancelRoundedIcon from '@mui/icons-material/CancelRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import ForumRoundedIcon from '@mui/icons-material/ForumRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import {
  PageHeader,
  StatusBadge,
  NotificationSnackbar,
  PrintableRecordModal,
} from '@/app/components/ui';
import { RecordEditorModal } from '@/app/studio/StudioModals';
import { useAuth } from '@/lib/auth-context';
import { batchFetchCategories, getCategoryItemName } from '@/lib/category-service';
import { batchFetchLookups, resolveLookupLabel } from '@/lib/lookup-service';
import {
  AppAuditLog,
  AppRecord,
  AppRuntimeTree,
  RecordComment,
  RelatedTableGroup,
  SysColumn,
  SysTable,
} from '@/lib/types';

function formatDetailValue(col: SysColumn, val: unknown): React.ReactNode {
  if (val === undefined || val === null || val === '') {
    return <span style={{ color: 'var(--text-muted)' }}>—</span>;
  }
  const dt = (col.dataType || 'STRING').toUpperCase();
  if (dt === 'BOOLEAN') {
    const b = val === true || String(val).toLowerCase() === 'true';
    return <span className={`badge ${b ? 'badge-success' : 'badge-warning'}`}>{b ? 'Có' : 'Không'}</span>;
  }
  if (dt === 'CURRENCY') {
    const num = Number(val);
    return Number.isFinite(num) ? <strong>{num.toLocaleString('vi-VN')} đ</strong> : String(val);
  }
  if (dt === 'NUMBER') {
    const num = Number(val);
    return Number.isFinite(num) ? num.toLocaleString('vi-VN') : String(val);
  }
  if (dt === 'DATE') {
    const s = String(val).slice(0, 10);
    if (/^\d{4}-\d{2}-\d{2}$/.test(s)) {
      const [y, m, d] = s.split('-');
      return `${d}/${m}/${y}`;
    }
    return s;
  }
  if (col.refCategoryCode) {
    const cat = getCategoryItemName(col.refCategoryCode, String(val));
    return typeof cat === 'string' ? cat : cat?.name || String(val);
  }
  if (col.refTableId || dt === 'LOOKUP') {
    return resolveLookupLabel(col, val) || String(val);
  }
  if (dt === 'SUB_TABLE' && Array.isArray(val)) {
    return <span className="badge badge-info">{val.length} dòng chi tiết</span>;
  }
  return String(val);
}

export default function RecordFullDetailPage() {
  const params = useParams();
  const appCode = String(params?.appCode || '');
  const recordId = String(params?.recordId || '');
  const { user, canPerformTableAction } = useAuth();
  const userRoles = user?.roleCodes?.length ? user.roleCodes : user?.roleCode ? [user.roleCode] : [];
  const isUserAdmin = Boolean(user?.isAdmin || userRoles.some((r) => r.toUpperCase() === 'ADMIN'));

  const [tree, setTree] = useState<AppRuntimeTree | null>(null);
  const [record, setRecord] = useState<AppRecord | null>(null);
  const [relations, setRelations] = useState<RelatedTableGroup[]>([]);
  const [audits, setAudits] = useState<AppAuditLog[]>([]);
  const [comments, setComments] = useState<RecordComment[]>([]);
  const [commentText, setCommentText] = useState('');
  const [postingComment, setPostingComment] = useState(false);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState('');

  const [showEditModal, setShowEditModal] = useState(false);
  const [saving, setSaving] = useState(false);
  const [showPrint, setShowPrint] = useState(false);
  const [snack, setSnack] = useState<{ open: boolean; msg: string; type: 'success' | 'error' }>({
    open: false,
    msg: '',
    type: 'success',
  });

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
    message: string;
    data: Record<string, any>;
  } | null>(null);

  const toast = (msg: string, type: 'success' | 'error' = 'success') => setSnack({ open: true, msg, type });

  const loadAll = useCallback(async () => {
    setLoading(true);
    setErrorMsg('');
    try {
      const [appRes, recRes] = await Promise.all([
        fetch(`/api/apps/${encodeURIComponent(appCode)}`).then((r) => r.json()),
        fetch(`/api/records/${encodeURIComponent(recordId)}`).then((r) => r.json()),
      ]);
      if (!appRes.success || !appRes.data) {
        setErrorMsg(appRes.message || 'Không thể tải cấu trúc ứng dụng.');
        return;
      }
      if (!recRes.success || !recRes.data) {
        setErrorMsg(recRes.message || 'Không tìm thấy hồ sơ hoặc bạn không có quyền xem.');
        return;
      }
      const appTree: AppRuntimeTree = appRes.data;
      const rec: AppRecord = recRes.data;
      setTree(appTree);
      setRecord(rec);

      const tblNode = appTree.modules.flatMap((m) => m.tables).find((t) => t.table.id === rec.tableId);
      if (tblNode) {
        const cats = tblNode.columns.map((c) => c.refCategoryCode).filter((x): x is string => Boolean(x));
        await Promise.all([
          cats.length > 0 ? batchFetchCategories(cats) : Promise.resolve(),
          batchFetchLookups(tblNode.columns),
        ]);
      }

      const [relRes, audRes, cmtRes] = await Promise.all([
        fetch(`/api/records/${encodeURIComponent(recordId)}/relations`).then((r) => r.json()),
        fetch(`/api/records/${encodeURIComponent(recordId)}/audit`).then((r) => r.json()),
        fetch(`/api/records/${encodeURIComponent(recordId)}/comments`).then((r) => r.json()),
      ]);
      if (relRes.success) setRelations(relRes.data || []);
      if (audRes.success) setAudits(audRes.data || []);
      if (cmtRes.success) setComments(cmtRes.data || []);

      if (isUserAdmin) {
        fetch(`/api/integrations/apps/${encodeURIComponent(appTree.app.id)}/plugins?enabledOnly=true`)
          .then((r) => (r.ok ? r.json() : null))
          .then((pRes) => {
            if (pRes?.success && Array.isArray(pRes.data)) {
              setEnabledPlugins(
                pRes.data
                  .filter((p: any) => p.category !== 'WEBHOOK_INBOUND' && (!p.tableId || p.tableId === rec.tableId))
                  .map((p: any) => ({
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
      }
    } catch {
      setErrorMsg('Lỗi kết nối tới máy chủ.');
    } finally {
      setLoading(false);
    }
  }, [appCode, recordId, isUserAdmin]);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  const { activeTable, activeColumns } = useMemo<{ activeTable: SysTable | null; activeColumns: SysColumn[] }>(() => {
    if (!tree || !record) return { activeTable: null, activeColumns: [] };
    const found = tree.modules.flatMap((m) => m.tables).find((t) => t.table.id === record.tableId);
    return { activeTable: found?.table || null, activeColumns: found?.columns || [] };
  }, [tree, record]);

  const canEdit = Boolean(tree && activeTable && canPerformTableAction(tree.app.code, activeTable.id, 'edit'));
  const canApprove = Boolean(tree && activeTable && canPerformTableAction(tree.app.code, activeTable.id, 'approve'));

  const handleSaveEdit = async (data: Record<string, any>) => {
    if (!record || !activeTable) return;
    setSaving(true);
    try {
      const res = await fetch('/api/records', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: record.id,
          appId: record.appId,
          moduleId: record.moduleId,
          tableId: record.tableId,
          data,
          expectedVersion: record.version,
        }),
      }).then((r) => r.json());
      if (res.success) {
        setShowEditModal(false);
        toast('Đã cập nhật hồ sơ thành công.');
        await loadAll();
      } else {
        toast(res.message || 'Cập nhật thất bại.', 'error');
      }
    } finally {
      setSaving(false);
    }
  };

  const handleApprovalAction = async (action: 'submit' | 'approve' | 'reject') => {
    if (!record) return;
    const url = action === 'submit'
      ? `/api/records/${record.id}/submit-approval`
      : `/api/records/${record.id}/approve`;
    const body = action === 'submit'
      ? {}
      : { approve: action === 'approve', reason: action === 'reject' ? 'Từ chối từ trang chi tiết' : 'Phê duyệt', expectedVersion: record.version, expectedStepOrder: record.currentStepOrder };
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }).then((r) => r.json());
    if (res.success) {
      toast('Đã cập nhật trạng thái phê duyệt.');
      await loadAll();
    } else {
      toast(res.message || 'Thao tác thất bại.', 'error');
    }
  };

  if (loading) {
    return (
      <div className="page-content">
        <div className="card" style={{ padding: 32, textAlign: 'center', color: 'var(--text-muted)' }}>
          Đang tải chi tiết hồ sơ...
        </div>
      </div>
    );
  }

  if (errorMsg || !tree || !record || !activeTable) {
    return (
      <div className="page-content">
        <div className="card" style={{ padding: 28 }}>
          <div style={{ fontWeight: 700, color: '#c5221f', marginBottom: 10 }}>{errorMsg || 'Không tìm thấy hồ sơ.'}</div>
          <Link href={`/apps/${encodeURIComponent(appCode)}`} className="btn btn-outline btn-sm">
            <ArrowBackRoundedIcon sx={{ fontSize: 16 }} /> Quay lại ứng dụng
          </Link>
        </div>
      </div>
    );
  }

  const approvalStatus = (record.approvalStatus || 'DRAFT').toUpperCase();
  const steps = ['DRAFT', 'PENDING', 'APPROVED'];
  const activeStepIdx = approvalStatus === 'REJECTED' ? 1 : Math.max(0, steps.indexOf(approvalStatus));

  return (
    <>
      <PageHeader
        title={`${activeTable.displayName}: ${record.recordKey || record.id}`}
        subtitle={`${tree.app.name} • Phiên bản v${record.version || 1} • Cập nhật bởi @${record.updatedBy || record.createdBy}`}
        actions={
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
            <Link href={`/apps/${encodeURIComponent(tree.app.code)}`} className="btn btn-outline btn-sm">
              <ArrowBackRoundedIcon sx={{ fontSize: 16 }} /> Quay lại {tree.app.name}
            </Link>
            <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowPrint(true)}>
              <PrintRoundedIcon sx={{ fontSize: 16 }} /> In phiếu A4
            </button>
            {canEdit && (approvalStatus !== 'APPROVED' || isUserAdmin) && (
              <button
                type="button"
                className="btn btn-primary btn-sm"
                style={{ backgroundColor: tree.app.color }}
                onClick={() => setShowEditModal(true)}
              >
                <EditRoundedIcon sx={{ fontSize: 16 }} /> Chỉnh sửa
              </button>
            )}
          </div>
        }
      />

      <div className="page-content" style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        {/* Status & Workflow Stepper */}
        {activeTable.approvalEnabled && (
          <div className="card" style={{ padding: '14px 18px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap' }}>
              <StatusBadge status={record.approvalStatus || 'DRAFT'} />
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12.5 }}>
                {[
                  { key: 'DRAFT', label: '1. Nháp' },
                  { key: 'PENDING', label: record.currentStepName ? `2. ${record.currentStepName}` : '2. Chờ duyệt' },
                  { key: 'APPROVED', label: approvalStatus === 'REJECTED' ? '3. Từ chối' : '3. Đã duyệt' },
                ].map((s, i) => (
                  <React.Fragment key={s.key}>
                    <span
                      style={{
                        padding: '4px 10px',
                        borderRadius: 999,
                        fontWeight: i <= activeStepIdx ? 700 : 500,
                        backgroundColor: i <= activeStepIdx
                          ? (approvalStatus === 'REJECTED' && i === 2 ? '#fce8e6' : '#e8f0fe')
                          : '#f1f5f9',
                        color: i <= activeStepIdx
                          ? (approvalStatus === 'REJECTED' && i === 2 ? '#c5221f' : '#1a73e8')
                          : 'var(--text-muted)',
                      }}
                    >
                      {s.label}
                    </span>
                    {i < 2 && <span style={{ color: 'var(--text-muted)' }}>→</span>}
                  </React.Fragment>
                ))}
              </div>
            </div>

            <div style={{ display: 'flex', gap: 8 }}>
              {(approvalStatus === 'DRAFT' || approvalStatus === 'REJECTED') && canEdit && (
                <button type="button" className="btn btn-primary btn-sm" onClick={() => handleApprovalAction('submit')}>
                  <FactCheckRoundedIcon sx={{ fontSize: 16 }} /> Gửi phê duyệt
                </button>
              )}
              {approvalStatus === 'PENDING' && canApprove && (
                <>
                  <button type="button" className="btn btn-primary btn-sm" style={{ backgroundColor: '#137333' }} onClick={() => handleApprovalAction('approve')}>
                    <CheckCircleRoundedIcon sx={{ fontSize: 16 }} /> Phê duyệt
                  </button>
                  <button type="button" className="btn btn-outline btn-sm" style={{ color: '#c5221f', borderColor: '#f5c2c7' }} onClick={() => handleApprovalAction('reject')}>
                    <CancelRoundedIcon sx={{ fontSize: 16 }} /> Từ chối
                  </button>
                </>
              )}
            </div>
          </div>
        )}

        {/* Main 2-Column Workspace */}
        <div className="detail-split-grid">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <div className="card" style={{ padding: 20 }}>
              <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 14 }}>
                THÔNG TIN HỒ SƠ ({activeColumns.length} trường)
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 14 }}>
                {activeColumns.map((c) => (
                  <div
                    key={c.id}
                    style={{
                      gridColumn: (c.dataType || '').toUpperCase() === 'TEXTAREA' || (c.dataType || '').toUpperCase() === 'SUB_TABLE' ? '1 / -1' : undefined,
                      padding: '10px 12px',
                      borderRadius: 8,
                      backgroundColor: '#f8fafc',
                      border: '1px solid #f1f5f9',
                    }}
                  >
                    <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginBottom: 4 }}>
                      {c.columnName}
                    </div>
                    <div style={{ fontSize: 13.5, fontWeight: 600, color: 'var(--text-primary)', wordBreak: 'break-word' }}>
                      {formatDetailValue(c, record.data?.[c.columnCode])}
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {relations.length > 0 && (
              <div className="card" style={{ padding: 20 }}>
                <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 12, display: 'flex', alignItems: 'center', gap: 6 }}>
                  <AccountTreeRoundedIcon sx={{ fontSize: 17 }} />
                  DỮ LIỆU LIÊN KẾT 360° ({relations.length} bảng)
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                  {relations.map((g, idx) => (
                    <div key={`${g.tableId}_${idx}`} style={{ padding: 12, borderRadius: 8, border: '1px solid var(--border-light)' }}>
                      <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 6 }}>
                        {g.appName} → {g.tableDisplayName} ({g.records.length})
                      </div>
                      {g.records.length === 0 ? (
                        <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Chưa có bản ghi liên kết.</div>
                      ) : (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                          {g.records.slice(0, 5).map((relRec) => (
                            <div key={relRec.id} style={{ fontSize: 12.5, padding: '6px 8px', borderRadius: 6, backgroundColor: '#f8fafc', display: 'flex', justifyContent: 'space-between' }}>
                              <span><code>{relRec.recordKey || relRec.id}</code></span>
                              <span style={{ color: 'var(--text-muted)' }}>
                                {relRec.updatedAt ? new Date(relRec.updatedAt).toLocaleDateString('vi-VN') : '—'}
                              </span>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>

          {/* Right Sidebar: Plugins + Comments + Audit */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            {enabledPlugins.length > 0 && (
              <div className="card" style={{ padding: 16 }}>
                <div style={{ fontSize: 12.5, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 10 }}>
                  TÍCH HỢP 1-CLICK
                </div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                  {enabledPlugins.map((plg) => {
                    const cat = (plg.category || '').toUpperCase();
                    const op = cat === 'EINVOICE' ? 'issue_einvoice' : cat === 'DIGITAL_SIGN' ? 'request_sign' : cat === 'SMS_VN' ? 'send_sms' : 'create_payment';
                    return (
                      <button
                        key={plg.id}
                        type="button"
                        disabled={pluginRunning !== null}
                        className="btn btn-xs btn-outline"
                        onClick={async () => {
                          setPluginRunning(plg.code);
                          try {
                            const d = record.data || {};
                            const fm = plg.fieldMapping || {};
                            const amount = (fm.amountField ? d[fm.amountField] : undefined) ?? d.amount ?? d.tong_tien ?? d.so_tien ?? 0;
                            const phone = (fm.phoneField ? d[fm.phoneField] : undefined) ?? d.phone ?? d.sdt ?? d.so_dien_thoai ?? '';
                            const orderRef = (fm.orderRefField ? d[fm.orderRefField] : undefined) ?? record.recordKey ?? record.id;
                            const res = await fetch(`/api/integrations/plugins/${encodeURIComponent(plg.code)}/execute`, {
                              method: 'POST',
                              headers: { 'Content-Type': 'application/json' },
                              body: JSON.stringify({
                                operation: op,
                                recordId: record.id,
                                tableId: record.tableId,
                                orderRef,
                                amount,
                                phone,
                                message: `Ho so ${orderRef} da duoc xu ly.`,
                              }),
                            }).then((r) => r.json());
                            if (res?.data) setPluginResult(res.data);
                          } finally {
                            setPluginRunning(null);
                          }
                        }}
                      >
                        {pluginRunning === plg.code ? 'Đang chạy...' : plg.name}
                      </button>
                    );
                  })}
                </div>
                {pluginResult && (
                  <div style={{ marginTop: 10, padding: 10, borderRadius: 8, backgroundColor: pluginResult.success ? '#f0fdf4' : '#fef2f2', fontSize: 12 }}>
                    <div style={{ fontWeight: 700 }}>{pluginResult.message}</div>
                    {pluginResult.data?.qrUrl && (
                      <img src={String(pluginResult.data.qrUrl)} alt="VietQR" style={{ maxWidth: 180, marginTop: 8, borderRadius: 6 }} />
                    )}
                  </div>
                )}
              </div>
            )}

            <div className="card" style={{ padding: 16 }}>
              <div style={{ fontSize: 12.5, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 10, display: 'flex', alignItems: 'center', gap: 6 }}>
                <ForumRoundedIcon sx={{ fontSize: 16 }} /> THẢO LUẬN ({comments.length})
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8, maxHeight: 240, overflowY: 'auto', marginBottom: 10 }}>
                {comments.length === 0 ? (
                  <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Chưa có bình luận nào.</div>
                ) : (
                  comments.map((c) => (
                    <div key={c.id} style={{ padding: 8, borderRadius: 6, backgroundColor: '#f8fafc', fontSize: 12.5 }}>
                      <div style={{ fontWeight: 700 }}>@{c.author}</div>
                      <div>{c.content}</div>
                    </div>
                  ))
                )}
              </div>
              <form
                onSubmit={async (e) => {
                  e.preventDefault();
                  if (!commentText.trim()) return;
                  setPostingComment(true);
                  try {
                    const res = await fetch(`/api/records/${record.id}/comments`, {
                      method: 'POST',
                      headers: { 'Content-Type': 'application/json' },
                      body: JSON.stringify({ content: commentText.trim() }),
                    }).then((r) => r.json());
                    if (res.success) {
                      setCommentText('');
                      await loadAll();
                    }
                  } finally {
                    setPostingComment(false);
                  }
                }}
                style={{ display: 'flex', gap: 6 }}
              >
                <input
                  className="form-input"
                  style={{ flex: 1, height: 34, fontSize: 12.5 }}
                  placeholder="Nhập bình luận (@username)..."
                  value={commentText}
                  onChange={(e) => setCommentText(e.target.value)}
                />
                <button type="submit" className="btn btn-primary btn-xs" disabled={postingComment}>
                  <SendRoundedIcon sx={{ fontSize: 14 }} />
                </button>
              </form>
            </div>

            <div className="card" style={{ padding: 16 }}>
              <div style={{ fontSize: 12.5, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 10, display: 'flex', alignItems: 'center', gap: 6 }}>
                <HistoryRoundedIcon sx={{ fontSize: 16 }} /> LỊCH SỬ THAY ĐỔI ({audits.length})
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8, maxHeight: 240, overflowY: 'auto' }}>
                {audits.length === 0 ? (
                  <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Chưa có lịch sử.</div>
                ) : (
                  audits.map((a) => (
                    <div key={a.id} style={{ padding: 8, borderRadius: 6, backgroundColor: '#f8fafc', fontSize: 12 }}>
                      <div style={{ fontWeight: 700 }}>{a.action} • @{a.actor}</div>
                      <div style={{ color: 'var(--text-secondary)' }}>{a.summary}</div>
                    </div>
                  ))
                )}
              </div>
            </div>
          </div>
        </div>
      </div>

      <RecordEditorModal
        open={showEditModal}
        record={record}
        tableName={activeTable.displayName}
        columns={activeColumns}
        formRules={activeTable.formRules || []}
        saving={saving}
        onClose={() => setShowEditModal(false)}
        onSave={handleSaveEdit}
      />

      <PrintableRecordModal
        open={showPrint}
        record={record}
        table={activeTable}
        columns={activeColumns}
        app={tree.app}
        onClose={() => setShowPrint(false)}
      />

      <NotificationSnackbar
        open={snack.open}
        message={snack.msg}
        severity={snack.type}
        onClose={() => setSnack((s) => ({ ...s, open: false }))}
      />

      {/* Mobile Sticky Quick Approve / Submit Bar */}
      {activeTable.approvalEnabled && (
        ((approvalStatus === 'DRAFT' || approvalStatus === 'REJECTED') && canEdit) ||
        (approvalStatus === 'PENDING' && canApprove)
      ) && (
        <div className="mobile-sticky-action-bar">
          {(approvalStatus === 'DRAFT' || approvalStatus === 'REJECTED') && canEdit && (
            <button
              type="button"
              className="btn btn-primary"
              style={{ flex: 1 }}
              onClick={() => handleApprovalAction('submit')}
            >
              <FactCheckRoundedIcon sx={{ fontSize: 18 }} /> Gửi phê duyệt
            </button>
          )}
          {approvalStatus === 'PENDING' && canApprove && (
            <>
              <button
                type="button"
                className="btn btn-primary"
                style={{ flex: 1, backgroundColor: '#137333' }}
                onClick={() => handleApprovalAction('approve')}
              >
                <CheckCircleRoundedIcon sx={{ fontSize: 18 }} /> Phê duyệt nhanh
              </button>
              <button
                type="button"
                className="btn btn-outline"
                style={{ flex: 1, color: '#c5221f', borderColor: '#f5c2c7' }}
                onClick={() => handleApprovalAction('reject')}
              >
                <CancelRoundedIcon sx={{ fontSize: 18 }} /> Từ chối
              </button>
            </>
          )}
        </div>
      )}
    </>
  );
}
