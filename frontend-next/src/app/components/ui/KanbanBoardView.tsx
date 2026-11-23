'use client';

import React, { useEffect, useMemo, useState } from 'react';
import { AppRecord, SysCategoryItem, SysColumn } from '@/lib/types';
import { fetchCategoryItems } from '@/lib/category-service';
import { MONDAY_PRIORITY_PRESETS, MONDAY_STATUS_PRESETS } from '@/lib/monday-fields';
import StatusBadge from './StatusBadge';
import EmptyState from './EmptyState';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import ViewKanbanRoundedIcon from '@mui/icons-material/ViewKanbanRounded';

export interface KanbanBoardViewProps {
  groupColumn?: SysColumn | null;
  groupableColumns?: SysColumn[];
  approvalEnabled?: boolean;
  columns: SysColumn[];
  records: AppRecord[];
  onMoveStatus: (recordId: string, field: string, nextValue: string) => Promise<void>;
  onEditRecord: (record: AppRecord) => void;
  onInspectRelations?: (record: AppRecord) => void;
}

interface LaneDef {
  code: string;
  label: string;
  color: string;
  isUnassigned?: boolean;
}

const APPROVAL_LANES: LaneDef[] = [
  { code: 'DRAFT', label: 'Nháp (Draft)', color: '#64748b' },
  { code: 'PENDING', label: 'Chờ duyệt', color: '#f59e0b' },
  { code: 'APPROVED', label: 'Đã duyệt', color: '#1e8e3e' },
  { code: 'REJECTED', label: 'Từ chối', color: '#d93025' },
  { code: 'RECALLED', label: 'Đã thu hồi', color: '#9333ea' },
];

const SELECT_PALETTE = ['#1a73e8', '#00897b', '#f59e0b', '#8e24aa', '#e53935', '#039be5'];

export default function KanbanBoardView({
  groupColumn,
  groupableColumns = [],
  approvalEnabled = false,
  columns,
  records,
  onMoveStatus,
  onEditRecord,
  onInspectRelations,
}: KanbanBoardViewProps) {
  const [categoryMap, setCategoryMap] = useState<Record<string, SysCategoryItem[]>>({});
  const [selectedGroupField, setSelectedGroupField] = useState<string>('');
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const [hoverLaneCode, setHoverLaneCode] = useState<string | null>(null);
  const [movingId, setMovingId] = useState<string | null>(null);

  const availableGroupCols = useMemo(() => {
    const merged: SysColumn[] = [];
    const seen = new Set<string>();
    for (const c of [groupColumn, ...groupableColumns]) {
      if (!c || seen.has(c.columnCode)) continue;
      if (c.refCategoryCode || ['SELECT', 'STATUS', 'PRIORITY'].includes(c.dataType.toUpperCase())) {
        seen.add(c.columnCode);
        merged.push(c);
      }
    }
    return merged;
  }, [groupColumn, groupableColumns]);

  const effectiveGroupField = useMemo(() => {
    if (selectedGroupField === '__approvalStatus__' && approvalEnabled) return '__approvalStatus__';
    if (selectedGroupField && availableGroupCols.some((c) => c.columnCode === selectedGroupField)) {
      return selectedGroupField;
    }
    if (groupColumn && availableGroupCols.some((c) => c.columnCode === groupColumn.columnCode)) {
      return groupColumn.columnCode;
    }
    if (availableGroupCols.length > 0) return availableGroupCols[0].columnCode;
    if (approvalEnabled) return '__approvalStatus__';
    return '';
  }, [selectedGroupField, availableGroupCols, groupColumn, approvalEnabled]);

  const activeGroupCol = useMemo(
    () => availableGroupCols.find((c) => c.columnCode === effectiveGroupField) || null,
    [availableGroupCols, effectiveGroupField],
  );

  const activeCatCode = activeGroupCol?.refCategoryCode?.trim().toUpperCase() || '';

  useEffect(() => {
    if (!activeCatCode) return;
    let cancelled = false;
    fetchCategoryItems(activeCatCode).then((items) => {
      if (cancelled) return;
      setCategoryMap((prev) => ({ ...prev, [activeCatCode]: items }));
    });
    return () => {
      cancelled = true;
    };
  }, [activeCatCode]);

  const isApprovalMode = effectiveGroupField === '__approvalStatus__';
  const canMoveCards = !isApprovalMode && Boolean(activeGroupCol);

  const lanes: LaneDef[] = useMemo(() => {
    if (isApprovalMode) {
      return APPROVAL_LANES;
    }
    if (activeCatCode) {
      return (categoryMap[activeCatCode] || []).map((it) => ({
        code: it.itemCode,
        label: it.itemName,
        color: it.color || '#1a73e8',
      }));
    }
    if (activeGroupCol) {
      const dt = activeGroupCol.dataType.toUpperCase();
      if (dt === 'STATUS') {
        return MONDAY_STATUS_PRESETS.map((p) => ({ code: p.code, label: p.label, color: p.bg }));
      }
      if (dt === 'PRIORITY') {
        return MONDAY_PRIORITY_PRESETS.map((p) => ({ code: p.code, label: p.label, color: p.bg }));
      }
      // Build lanes dynamically from SELECT default options and observed record values.
      const values = new Map<string, string>();
      if (activeGroupCol.defaultValue?.trim()) {
        for (const token of activeGroupCol.defaultValue.split(/[,;|]/)) {
          const t = token.trim();
          if (t && !values.has(t.toUpperCase())) values.set(t.toUpperCase(), t);
        }
      }
      for (const r of records) {
        const raw = String(r.data?.[activeGroupCol.columnCode] || '').trim();
        if (raw && !values.has(raw.toUpperCase())) {
          values.set(raw.toUpperCase(), raw);
        }
      }
      return Array.from(values.entries()).map(([upper, label], i) => ({
        code: label || upper,
        label,
        color: SELECT_PALETTE[i % SELECT_PALETTE.length],
      }));
    }
    return [];
  }, [isApprovalMode, activeCatCode, categoryMap, activeGroupCol, records]);

  const unassignedRecords = useMemo(() => {
    if (!effectiveGroupField) return [];
    const knownCodes = new Set(lanes.map((l) => l.code.toUpperCase()));
    return records.filter((r) => {
      const val = isApprovalMode
        ? String(r.approvalStatus || 'DRAFT').trim().toUpperCase()
        : String(r.data?.[effectiveGroupField] || '').trim().toUpperCase();
      return !val || !knownCodes.has(val);
    });
  }, [records, lanes, effectiveGroupField, isApprovalMode]);

  const displayedLanes: LaneDef[] = useMemo(() => {
    if (unassignedRecords.length === 0) return lanes;
    return [
      ...lanes,
      {
        code: '__UNASSIGNED__',
        label: 'Chưa phân loại',
        color: '#94a3b8',
        isUnassigned: true,
      },
    ];
  }, [lanes, unassignedRecords.length]);

  const previewCols = useMemo(
    () =>
      columns
        .filter((c) => c.columnCode !== effectiveGroupField && c.showInTable !== false)
        .slice(0, 4),
    [columns, effectiveGroupField],
  );

  const executeMove = async (recordId: string, nextCode: string) => {
    if (!canMoveCards || !activeGroupCol || movingId) return;
    setMovingId(recordId);
    try {
      await onMoveStatus(recordId, activeGroupCol.columnCode, nextCode === '__UNASSIGNED__' ? '' : nextCode);
    } finally {
      setMovingId(null);
    }
  };

  const hasMultipleGroupOptions = availableGroupCols.length + (approvalEnabled ? 1 : 0) > 1;

  if (displayedLanes.length === 0) {
    return (
      <div className="card" style={{ padding: 24 }}>
        <EmptyState
          icon={<ViewKanbanRoundedIcon sx={{ fontSize: 36 }} />}
          title="Chưa có cột phân nhóm Kanban"
          description="Bảng cần ít nhất 1 cột Danh mục / Select hoặc bật Quy trình phê duyệt để hiển thị Kanban."
        />
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      {(hasMultipleGroupOptions || isApprovalMode) && (
        <div
          className="card"
          style={{
            padding: '8px 14px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: 10,
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--text-secondary)' }}>
              Nhóm thẻ theo:
            </span>
            <select
              className="form-select"
              style={{ width: 220, height: 30, fontSize: 12.5, padding: '2px 8px' }}
              value={effectiveGroupField}
              onChange={(e) => setSelectedGroupField(e.target.value)}
            >
              {availableGroupCols.map((c) => (
                <option key={c.id} value={c.columnCode}>
                  {c.columnName} ({c.refCategoryCode ? 'Danh mục' : 'Select'})
                </option>
              ))}
              {approvalEnabled && <option value="__approvalStatus__">Trạng thái Phê duyệt</option>}
            </select>
          </div>
          {isApprovalMode && (
            <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              Chế độ theo dõi quy trình duyệt (mở Chi tiết bản ghi để Trình duyệt / Phê duyệt)
            </span>
          )}
        </div>
      )}

      <div
        style={{
          display: 'grid',
          gridTemplateColumns: `repeat(${Math.max(displayedLanes.length, 1)}, minmax(265px, 1fr))`,
          gap: 14,
          overflowX: 'auto',
          paddingBottom: 8,
        }}
      >
        {displayedLanes.map((lane) => {
          const laneRecords = lane.isUnassigned
            ? unassignedRecords
            : records.filter((r) => {
                const raw = isApprovalMode
                  ? String(r.approvalStatus || 'DRAFT').trim().toUpperCase()
                  : String(r.data?.[effectiveGroupField] || '').trim().toUpperCase();
                return raw === lane.code.toUpperCase();
              });
          const isDropTarget = hoverLaneCode === lane.code && canMoveCards;

          return (
            <div
              key={lane.code}
              onDragOver={(e) => {
                if (!canMoveCards) return;
                e.preventDefault();
                if (hoverLaneCode !== lane.code) setHoverLaneCode(lane.code);
              }}
              onDragLeave={() => {
                if (hoverLaneCode === lane.code) setHoverLaneCode(null);
              }}
              onDrop={async () => {
                setHoverLaneCode(null);
                if (draggingId && canMoveCards) {
                  const id = draggingId;
                  setDraggingId(null);
                  await executeMove(id, lane.code);
                }
              }}
              style={{
                backgroundColor: isDropTarget ? 'var(--info-bg)' : 'var(--bg-secondary)',
                borderRadius: 14,
                padding: 12,
                minHeight: 360,
                display: 'flex',
                flexDirection: 'column',
                gap: 10,
                border: isDropTarget ? '1.5px dashed #1a73e8' : '1px solid var(--border-light)',
                transition: 'background-color 0.15s ease, border-color 0.15s ease',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '2px 4px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
                  <span style={{ width: 9, height: 9, borderRadius: '50%', backgroundColor: lane.color || '#1a73e8' }} />
                  <strong style={{ fontSize: 13.5, color: 'var(--text-primary)' }}>{lane.label}</strong>
                </div>
                <span className="badge badge-info">{laneRecords.length}</span>
              </div>

              {laneRecords.map((rec) => {
                const isMoving = movingId === rec.id;
                const currentVal = activeGroupCol
                  ? String(rec.data?.[activeGroupCol.columnCode] || '').trim()
                  : '';

                return (
                  <div
                    key={rec.id}
                    draggable={canMoveCards && !isMoving}
                    onDragStart={() => {
                      if (canMoveCards) setDraggingId(rec.id);
                    }}
                    onDragEnd={() => {
                      setDraggingId(null);
                      setHoverLaneCode(null);
                    }}
                    style={{
                      backgroundColor: 'var(--bg-surface)',
                      border: '1px solid var(--border-subtle)',
                      borderRadius: 12,
                      padding: 12,
                      boxShadow: 'var(--shadow-sm)',
                      cursor: canMoveCards ? (isMoving ? 'wait' : 'grab') : 'default',
                      opacity: isMoving ? 0.65 : 1,
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 6,
                    }}
                  >
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6 }}>
                      {onInspectRelations ? (
                        <button
                          type="button"
                          onClick={() => onInspectRelations(rec)}
                          style={{
                            background: 'none',
                            border: 'none',
                            padding: 0,
                            cursor: 'pointer',
                            textAlign: 'left',
                            color: '#1a73e8',
                            fontFamily: 'monospace',
                            fontSize: 11.5,
                            fontWeight: 600,
                          }}
                          title="Mở Chi tiết 360° & Quy trình"
                        >
                          {rec.recordKey || rec.id}
                        </button>
                      ) : (
                        <code style={{ fontSize: 11.5 }}>{rec.recordKey || rec.id}</code>
                      )}
                      <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                        <StatusBadge status={rec.status} size="sm" />
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          onClick={() => onEditRecord(rec)}
                          title="Sửa chi tiết"
                        >
                          <EditRoundedIcon sx={{ fontSize: 13 }} />
                        </button>
                      </div>
                    </div>

                    {previewCols.map((c) => (
                      <div key={c.id} style={{ fontSize: 12.5, color: 'var(--text-secondary)' }}>
                        <span style={{ color: 'var(--text-muted)' }}>{c.columnName}: </span>
                        <strong style={{ color: 'var(--text-primary)' }}>{String(rec.data?.[c.columnCode] ?? '—')}</strong>
                      </div>
                    ))}

                    {canMoveCards && lanes.length > 0 && (
                      <div style={{ marginTop: 4, paddingTop: 6, borderTop: '1px solid var(--border-light)' }}>
                        <select
                          aria-label={`Chuyển cột cho ${rec.recordKey || rec.id}`}
                          className="form-select"
                          disabled={isMoving}
                          value={
                            lanes.find((l) => l.code.toUpperCase() === currentVal.toUpperCase())?.code ||
                            '__UNASSIGNED__'
                          }
                          onChange={(e) => {
                            const nextCode = e.target.value;
                            if (nextCode.toUpperCase() !== currentVal.toUpperCase()) {
                              void executeMove(rec.id, nextCode);
                            }
                          }}
                          style={{
                            width: '100%',
                            height: 28,
                            fontSize: 11.5,
                            padding: '2px 8px',
                            backgroundColor: 'var(--bg-secondary)',
                          }}
                        >
                          {lane.isUnassigned && <option value="__UNASSIGNED__">-- Chọn trạng thái --</option>}
                          {lanes.map((opt) => (
                            <option key={opt.code} value={opt.code}>
                              → {opt.label}
                            </option>
                          ))}
                        </select>
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          );
        })}
      </div>
    </div>
  );
}
