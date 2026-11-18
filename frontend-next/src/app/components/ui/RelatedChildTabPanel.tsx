'use client';

import React, { useMemo, useState } from 'react';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import UndoRoundedIcon from '@mui/icons-material/UndoRounded';
import CheckRoundedIcon from '@mui/icons-material/CheckRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import { RelatedTableGroup, SysColumn } from '@/lib/types';
import { resolveLookupLabel } from '@/lib/lookup-service';
import { formDefaultValues } from '@/lib/form-rules';
import { useAuth } from '@/lib/auth-context';
import DynamicFieldInput from './DynamicFieldInput';

export interface ChildDraftRow {
  localKey: string;
  id?: string;
  version?: number;
  data: Record<string, unknown>;
  status: 'existing' | 'new' | 'edited' | 'deleted';
  approvalStatus?: string;
}

interface RelatedChildTabPanelProps {
  group: RelatedTableGroup;
  rows: ChildDraftRow[];
  onChangeRows: (next: ChildDraftRow[]) => void;
}

function formatCellPreview(col: SysColumn, rawVal: unknown): React.ReactNode {
  if (rawVal === undefined || rawVal === null || rawVal === '') {
    return <span style={{ color: 'var(--text-muted)' }}>—</span>;
  }
  const dt = (col.dataType || 'STRING').toUpperCase();
  if (dt === 'BOOLEAN') {
    const b = rawVal === true || String(rawVal).toLowerCase() === 'true';
    return b ? 'Có' : 'Không';
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
  if (dt === 'SUB_TABLE' && Array.isArray(rawVal)) {
    return `${rawVal.length} dòng`;
  }
  const text = String(rawVal);
  return text.length > 48 ? `${text.slice(0, 48)}…` : text;
}

function StatusChip({ status }: { status: ChildDraftRow['status'] }) {
  if (status === 'new') {
    return (
      <span className="badge badge-success" style={{ fontSize: 10.5 }}>
        Mới
      </span>
    );
  }
  if (status === 'edited') {
    return (
      <span className="badge badge-warning" style={{ fontSize: 10.5 }}>
        Đã sửa
      </span>
    );
  }
  if (status === 'deleted') {
    return (
      <span className="badge badge-danger" style={{ fontSize: 10.5 }}>
        Chờ xóa
      </span>
    );
  }
  return null;
}

/**
 * Tab nhập liệu nháp cho một bảng phụ liên kết (ref_table_id) bên trong Modal Thêm/Sửa bản ghi cha.
 */
export default function RelatedChildTabPanel({
  group,
  rows,
  onChangeRows,
}: RelatedChildTabPanelProps) {
  const { user } = useAuth();
  const userRoles = useMemo(
    () => (user?.roleCodes?.length ? user.roleCodes : (user?.roleCode ? [user.roleCode] : [])),
    [user]
  );
  const isUserAdmin = Boolean(user?.isAdmin || userRoles.some((r) => r.toUpperCase() === 'ADMIN'));

  const [editingKey, setEditingKey] = useState<string | null>(null);
  const [draftData, setDraftData] = useState<Record<string, unknown>>({});
  const [rowError, setRowError] = useState('');

  const editableColumns = useMemo(() => {
    return group.columns.filter((c) => {
      if (group.viaColumnCode && c.columnCode === group.viaColumnCode) return false;
      if (!isUserAdmin && (c.hiddenForRoles || []).some((r) => userRoles.some((ur) => ur.toUpperCase() === r.toUpperCase()))) {
        return false;
      }
      return true;
    });
  }, [group.columns, group.viaColumnCode, isUserAdmin, userRoles]);

  const previewColumns = useMemo(() => {
    const simple = editableColumns.filter((c) => !['SUB_TABLE', 'FILE', 'MULTI_FILE', 'IMAGE', 'SIGNATURE'].includes((c.dataType || '').toUpperCase()));
    return (simple.length > 0 ? simple : editableColumns).slice(0, 5);
  }, [editableColumns]);

  const startAddRow = () => {
    const init = formDefaultValues(group.columns);
    if (group.viaColumnCode) delete init[group.viaColumnCode];
    setEditingKey('__NEW__');
    setDraftData(init);
    setRowError('');
  };

  const startEditRow = (row: ChildDraftRow) => {
    setEditingKey(row.localKey);
    setDraftData({ ...(row.data || {}) });
    setRowError('');
  };

  const cancelRowEdit = () => {
    setEditingKey(null);
    setDraftData({});
    setRowError('');
  };

  const commitRowEdit = () => {
    for (const col of editableColumns) {
      const dt = (col.dataType || '').toUpperCase();
      if (!col.isRequired || col.formula || dt === 'AUTO_NUMBER') continue;
      const val = draftData[col.columnCode];
      const empty = val === undefined || val === null || (typeof val === 'string' && val.trim() === '');
      if (empty) {
        setRowError(`Vui lòng nhập "${col.columnName}".`);
        return;
      }
    }

    if (editingKey === '__NEW__') {
      const nextRow: ChildDraftRow = {
        localKey: `new_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`,
        data: { ...draftData },
        status: 'new',
      };
      onChangeRows([nextRow, ...rows]);
    } else if (editingKey) {
      onChangeRows(
        rows.map((r) => {
          if (r.localKey !== editingKey) return r;
          return {
            ...r,
            data: { ...draftData },
            status: r.status === 'new' ? 'new' : 'edited',
          };
        }),
      );
    }

    cancelRowEdit();
  };

  const toggleDeleteRow = (row: ChildDraftRow) => {
    if (row.status === 'new') {
      onChangeRows(rows.filter((r) => r.localKey !== row.localKey));
      if (editingKey === row.localKey) cancelRowEdit();
      return;
    }
    onChangeRows(
      rows.map((r) => {
        if (r.localKey !== row.localKey) return r;
        if (r.status === 'deleted') {
          return { ...r, status: 'existing' };
        }
        return { ...r, status: 'deleted' };
      }),
    );
    if (editingKey === row.localKey) cancelRowEdit();
  };

  const activeCount = rows.filter((r) => r.status !== 'deleted').length;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
        <div style={{ fontSize: 12.5, color: 'var(--text-secondary)' }}>
          Liên kết tự động qua trường <strong>{group.viaColumnName}</strong> · Đang có <strong>{activeCount}</strong> dòng
          <span style={{ color: 'var(--text-muted)', marginLeft: 6 }}>
            (Mọi thay đổi được lưu cùng lúc khi bấm &ldquo;Lưu bản ghi&rdquo;)
          </span>
        </div>
        {group.canCreate && editingKey !== '__NEW__' && (
          <button type="button" className="btn btn-primary btn-xs" onClick={startAddRow}>
            <AddRoundedIcon sx={{ fontSize: 15 }} /> Thêm dòng {group.tableDisplayName}
          </button>
        )}
      </div>

      {editingKey && (
        <div
          style={{
            border: '1px solid var(--primary)',
            borderRadius: 10,
            padding: 14,
            background: 'var(--bg-subtle, #f8fafc)',
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
            <strong style={{ fontSize: 13 }}>
              {editingKey === '__NEW__' ? `Thêm dòng mới — ${group.tableDisplayName}` : `Chỉnh sửa dòng — ${group.tableDisplayName}`}
            </strong>
            <button type="button" className="btn btn-outline btn-xs" onClick={cancelRowEdit}>
              <CloseRoundedIcon sx={{ fontSize: 14 }} />
            </button>
          </div>

          {rowError && (
            <div style={{ marginBottom: 10, padding: '6px 10px', borderRadius: 6, background: '#fef2f2', color: '#b91c1c', fontSize: 12 }}>
              {rowError}
            </div>
          )}

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12 }}>
            {editableColumns.map((col) => {
              const isRoleReadOnly = !isUserAdmin && (col.readOnlyForRoles || []).some((rr) =>
                userRoles.some((ur) => ur.toUpperCase() === rr.toUpperCase()),
              );
              return (
                <div
                  key={col.id}
                  className="form-group"
                  style={{
                    marginBottom: 0,
                    gridColumn: col.dataType === 'TEXTAREA' || col.dataType === 'SUB_TABLE' ? '1 / -1' : undefined,
                  }}
                >
                  <label className="form-label" style={{ fontSize: 12 }}>
                    {col.columnName} {col.isRequired && !isRoleReadOnly && <span style={{ color: '#d93025' }}>*</span>}
                  </label>
                  <DynamicFieldInput
                    column={col}
                    value={draftData[col.columnCode]}
                    formData={draftData}
                    disabled={isRoleReadOnly}
                    onChange={(val) => {
                      setRowError('');
                      setDraftData((prev) => ({ ...prev, [col.columnCode]: val }));
                    }}
                  />
                </div>
              );
            })}
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 12 }}>
            <button type="button" className="btn btn-outline btn-xs" onClick={cancelRowEdit}>
              Hủy
            </button>
            <button type="button" className="btn btn-primary btn-xs" onClick={commitRowEdit}>
              <CheckRoundedIcon sx={{ fontSize: 14 }} /> Xác nhận dòng
            </button>
          </div>
        </div>
      )}

      {rows.length === 0 ? (
        <div
          style={{
            padding: '24px 16px',
            textAlign: 'center',
            border: '1px dashed var(--border-light)',
            borderRadius: 10,
            color: 'var(--text-muted)',
            fontSize: 13,
          }}
        >
          Chưa có bản ghi liên kết nào trong <strong>{group.tableDisplayName}</strong>.
        </div>
      ) : (
        <div style={{ overflowX: 'auto', border: '1px solid var(--border-light)', borderRadius: 10 }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12.5 }}>
            <thead>
              <tr style={{ background: 'var(--bg-subtle, #f8fafc)', borderBottom: '1px solid var(--border-light)' }}>
                <th style={{ padding: '8px 10px', textAlign: 'left', width: 76 }}>Trạng thái</th>
                {previewColumns.map((c) => (
                  <th key={c.id} style={{ padding: '8px 10px', textAlign: 'left', fontWeight: 600 }}>
                    {c.columnName}
                  </th>
                ))}
                <th style={{ padding: '8px 10px', textAlign: 'right', width: 92 }}>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const isDeleted = row.status === 'deleted';
                const isApprovedLocked = !isUserAdmin && row.approvalStatus === 'APPROVED';
                const canEditRow = !isDeleted && !isApprovedLocked && (row.status === 'new' || Boolean(group.canEdit));
                const canDeleteRow = !isApprovedLocked && (row.status === 'new' || Boolean(group.canDelete));

                return (
                  <tr
                    key={row.localKey}
                    style={{
                      borderBottom: '1px solid var(--border-light)',
                      opacity: isDeleted ? 0.55 : 1,
                      textDecoration: isDeleted ? 'line-through' : 'none',
                      background: editingKey === row.localKey ? 'rgba(26, 115, 232, 0.04)' : undefined,
                    }}
                  >
                    <td style={{ padding: '7px 10px' }}>
                      <StatusChip status={row.status} />
                    </td>
                    {previewColumns.map((c) => (
                      <td key={c.id} style={{ padding: '7px 10px' }}>
                        {formatCellPreview(c, row.data?.[c.columnCode])}
                      </td>
                    ))}
                    <td style={{ padding: '7px 10px', textAlign: 'right', whiteSpace: 'nowrap' }}>
                      {canEditRow && (
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          style={{ marginRight: 4 }}
                          title="Chỉnh sửa dòng"
                          onClick={() => startEditRow(row)}
                        >
                          <EditRoundedIcon sx={{ fontSize: 13 }} />
                        </button>
                      )}
                      {canDeleteRow && (
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          title={isDeleted ? 'Hoàn tác xóa' : 'Đánh dấu xóa'}
                          onClick={() => toggleDeleteRow(row)}
                        >
                          {isDeleted ? <UndoRoundedIcon sx={{ fontSize: 13 }} /> : <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} />}
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
