'use client';

import React, { useMemo, useState } from 'react';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import CancelRoundedIcon from '@mui/icons-material/CancelRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import { AppRecord, BatchOperationResult, SysColumn } from '@/lib/types';
import { useEscapeKey } from '@/lib/ui-hooks';
import DynamicFieldInput from './DynamicFieldInput';

interface FloatingBulkActionBarProps {
  tableId: string;
  columns: SysColumn[];
  selectedRecords: AppRecord[];
  canEdit: boolean;
  canDelete: boolean;
  approvalEnabled: boolean;
  canApprove: boolean;
  onClearSelection: () => void;
  onBatchDelete: (ids: string[]) => void;
  onCompleted: (summary: string, severity?: 'success' | 'error' | 'info') => Promise<void> | void;
}

export default function FloatingBulkActionBar({
  tableId,
  columns,
  selectedRecords,
  canEdit,
  canDelete,
  approvalEnabled,
  canApprove,
  onClearSelection,
  onBatchDelete,
  onCompleted,
}: FloatingBulkActionBarProps) {
  const [showEditModal, setShowEditModal] = useState(false);
  const [showRejectModal, setShowRejectModal] = useState(false);
  const [targetColumnCode, setTargetColumnCode] = useState('');
  const [patchValue, setPatchValue] = useState<unknown>('');
  const [rejectReason, setRejectReason] = useState('');
  const [busy, setBusy] = useState(false);

  const count = selectedRecords.length;
  const selectedIds = useMemo(() => selectedRecords.map((r) => r.id), [selectedRecords]);
  const pendingCount = useMemo(
    () => selectedRecords.filter((r) => (r.approvalStatus || '').toUpperCase() === 'PENDING').length,
    [selectedRecords],
  );

  const editableColumns = useMemo(
    () =>
      columns.filter((c) => {
        const dt = (c.dataType || 'STRING').toUpperCase();
        return !c.formula && !['AUTO_NUMBER', 'SUB_TABLE', 'FILE', 'IMAGE'].includes(dt) && c.showInForm !== false;
      }),
    [columns],
  );

  const selectedColObj = editableColumns.find((c) => c.columnCode === targetColumnCode) || editableColumns[0] || null;

  useEscapeKey(count > 0 && !showEditModal && !showRejectModal, onClearSelection);
  useEscapeKey(showEditModal, () => setShowEditModal(false));
  useEscapeKey(showRejectModal, () => setShowRejectModal(false));

  if (count === 0) return null;

  const runBatchPatch = async () => {
    if (!selectedColObj) return;
    setBusy(true);
    try {
      const res = await fetch('/api/records/batch-patch', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          tableId,
          recordIds: selectedIds,
          columnCode: selectedColObj.columnCode,
          value: patchValue,
        }),
      }).then((r) => r.json());

      if (res.data) {
        const d = res.data as BatchOperationResult;
        setShowEditModal(false);
        const msg =
          d.failed > 0
            ? `Đã sửa hàng loạt ${d.succeeded}/${d.requested} bản ghi (${d.failed} bản ghi bị chặn: ${d.errors[0]?.message || 'không hợp lệ'}).`
            : `Đã cập nhật trường "${selectedColObj.columnName}" cho ${d.succeeded} bản ghi!`;
        await onCompleted(msg, d.failed > 0 && d.succeeded === 0 ? 'error' : 'success');
      } else {
        await onCompleted(res.message || 'Không thể cập nhật hàng loạt.', 'error');
      }
    } finally {
      setBusy(false);
    }
  };

  const runBatchApprove = async (approve: boolean, reason?: string) => {
    setBusy(true);
    try {
      const res = await fetch('/api/records/batch-approve', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          recordIds: selectedIds,
          approve,
          reason: reason?.trim() || undefined,
        }),
      }).then((r) => r.json());

      if (res.data) {
        const d = res.data as BatchOperationResult;
        setShowRejectModal(false);
        setRejectReason('');
        const verb = approve ? 'phê duyệt' : 'từ chối';
        const msg =
          d.failed > 0
            ? `Đã ${verb} ${d.succeeded}/${d.requested} hồ sơ (${d.failed} bỏ qua: ${d.errors[0]?.message || 'không đủ điều kiện'}).`
            : `Đã ${verb} thành công ${d.succeeded} hồ sơ!`;
        await onCompleted(msg, d.failed > 0 && d.succeeded === 0 ? 'error' : 'success');
      } else {
        await onCompleted(res.message || 'Không thể xử lý phê duyệt hàng loạt.', 'error');
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <div className="floating-bulk-bar no-print" role="region" aria-label="Thanh thao tác hàng loạt">
        <span className="badge badge-info" style={{ fontSize: 12, padding: '4px 11px' }}>
          Đã chọn {count} bản ghi
        </span>

        {canEdit && editableColumns.length > 0 && (
          <button
            type="button"
            className="btn btn-outline btn-xs"
            disabled={busy}
            onClick={() => {
              const first = editableColumns[0];
              setTargetColumnCode(first?.columnCode || '');
              setPatchValue(first?.dataType === 'BOOLEAN' ? false : '');
              setShowEditModal(true);
            }}
          >
            <EditRoundedIcon sx={{ fontSize: 14 }} />
            Sửa hàng loạt
          </button>
        )}

        {approvalEnabled && canApprove && pendingCount > 0 && (
          <>
            <button
              type="button"
              className="btn btn-xs"
              disabled={busy}
              style={{ backgroundColor: 'var(--success)', color: '#ffffff', border: 'none' }}
              onClick={() => void runBatchApprove(true)}
            >
              <CheckCircleRoundedIcon sx={{ fontSize: 14 }} />
              Duyệt ({pendingCount})
            </button>
            <button
              type="button"
              className="btn btn-xs"
              disabled={busy}
              style={{ backgroundColor: 'var(--warning)', color: '#ffffff', border: 'none' }}
              onClick={() => setShowRejectModal(true)}
            >
              <CancelRoundedIcon sx={{ fontSize: 14 }} />
              Từ chối
            </button>
          </>
        )}

        {canDelete && (
          <button
            type="button"
            className="btn btn-xs"
            disabled={busy}
            style={{ backgroundColor: 'var(--danger-bg)', color: 'var(--danger-text)', border: '1px solid var(--danger-border)' }}
            onClick={() => onBatchDelete(selectedIds)}
          >
            <DeleteOutlineRoundedIcon sx={{ fontSize: 14 }} />
            Xóa ({count})
          </button>
        )}

        <button
          type="button"
          className="btn btn-outline btn-xs"
          onClick={onClearSelection}
          title="Bỏ chọn tất cả (Esc)"
          aria-label="Bỏ chọn tất cả"
        >
          <CloseRoundedIcon sx={{ fontSize: 14 }} />
          Bỏ chọn
        </button>
      </div>

      {/* Bulk Patch Modal */}
      {showEditModal && selectedColObj && (
        <div className="modal-overlay" onClick={() => setShowEditModal(false)} role="dialog" aria-modal="true" aria-label="Sửa dữ liệu hàng loạt">
          <div className="modal-dialog" style={{ maxWidth: 480 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16, fontWeight: 700, marginBottom: 6 }}>
              Sửa hàng loạt ({count} bản ghi)
            </h3>
            <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginBottom: 14 }}>
              Mọi thay đổi đều đi qua quy tắc kiểm tra nghiệp vụ, tính lại công thức, khóa phê duyệt và ghi Nhật ký Audit.
            </p>

            <div className="form-group">
              <label htmlFor="bulk-edit-col" className="form-label">Chọn trường cần cập nhật</label>
              <select
                id="bulk-edit-col"
                className="form-select"
                value={selectedColObj.columnCode}
                onChange={(e) => {
                  const nextCol = editableColumns.find((c) => c.columnCode === e.target.value);
                  setTargetColumnCode(e.target.value);
                  setPatchValue(nextCol?.dataType === 'BOOLEAN' ? false : '');
                }}
              >
                {editableColumns.map((c) => (
                  <option key={c.id} value={c.columnCode}>
                    {c.columnName} ({c.columnCode})
                  </option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <label className="form-label">Giá trị mới áp dụng</label>
              <DynamicFieldInput
                column={selectedColObj}
                value={patchValue}
                onChange={(val) => setPatchValue(val)}
              />
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 16 }}>
              <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowEditModal(false)} disabled={busy}>
                Hủy
              </button>
              <button type="button" className="btn btn-primary btn-sm" onClick={() => void runBatchPatch()} disabled={busy}>
                {busy ? 'Đang cập nhật...' : `Áp dụng cho ${count} bản ghi`}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Bulk Reject Reason Modal */}
      {showRejectModal && (
        <div className="modal-overlay" onClick={() => setShowRejectModal(false)} role="dialog" aria-modal="true" aria-label="Từ chối hàng loạt">
          <div className="modal-dialog" style={{ maxWidth: 440 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16, fontWeight: 700, marginBottom: 6 }}>
              Từ chối phê duyệt hàng loạt ({pendingCount} hồ sơ chờ duyệt)
            </h3>
            <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginBottom: 12 }}>
              Nhập lý do từ chối chung để gửi thông báo lại cho người lập hồ sơ:
            </p>
            <textarea
              className="form-textarea"
              rows={3}
              placeholder="Ví dụ: Thiếu chứng từ đính kèm, vui lòng bổ sung và trình lại..."
              value={rejectReason}
              onChange={(e) => setRejectReason(e.target.value)}
              autoFocus
            />
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 14 }}>
              <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowRejectModal(false)} disabled={busy}>
                Hủy
              </button>
              <button
                type="button"
                className="btn btn-sm"
                disabled={busy}
                style={{ backgroundColor: 'var(--danger)', color: '#ffffff', border: 'none' }}
                onClick={() => void runBatchApprove(false, rejectReason)}
              >
                {busy ? 'Đang xử lý...' : 'Xác nhận từ chối'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
