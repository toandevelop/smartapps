'use client';

import React, { useEffect, useState } from 'react';
import { AppRecord, SysCategoryItem, SysColumn } from '@/lib/types';
import { fetchCategoryItems } from '@/lib/category-service';
import StatusBadge from './StatusBadge';
import EditRoundedIcon from '@mui/icons-material/EditRounded';

export interface KanbanBoardViewProps {
  groupColumn: SysColumn;
  columns: SysColumn[];
  records: AppRecord[];
  onMoveStatus: (recordId: string, field: string, nextValue: string) => Promise<void>;
  onEditRecord: (record: AppRecord) => void;
}

export default function KanbanBoardView({
  groupColumn,
  columns,
  records,
  onMoveStatus,
  onEditRecord,
}: KanbanBoardViewProps) {
  const [lanes, setLanes] = useState<SysCategoryItem[]>([]);
  const [draggingId, setDraggingId] = useState<string | null>(null);

  useEffect(() => {
    if (!groupColumn.refCategoryCode) {
      setLanes([]);
      return;
    }
    fetchCategoryItems(groupColumn.refCategoryCode).then(setLanes);
  }, [groupColumn.refCategoryCode]);

  const previewCols = columns
    .filter((c) => c.columnCode !== groupColumn.columnCode && c.showInTable !== false)
    .slice(0, 4);

  return (
    <div style={{ display: 'grid', gridTemplateColumns: `repeat(${Math.max(lanes.length, 1)}, minmax(260px, 1fr))`, gap: 14, overflowX: 'auto', paddingBottom: 8 }}>
      {lanes.map((lane) => {
        const laneRecords = records.filter(
          (r) => String(r.data?.[groupColumn.columnCode] || '').toUpperCase() === lane.itemCode.toUpperCase()
        );
        return (
          <div
            key={lane.itemCode}
            onDragOver={(e) => e.preventDefault()}
            onDrop={async () => {
              if (draggingId) {
                await onMoveStatus(draggingId, groupColumn.columnCode, lane.itemCode);
                setDraggingId(null);
              }
            }}
            style={{
              backgroundColor: 'var(--bg-secondary)',
              borderRadius: 14,
              padding: 12,
              minHeight: 360,
              display: 'flex',
              flexDirection: 'column',
              gap: 10,
              border: '1px solid var(--border-light)',
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '2px 4px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
                <span style={{ width: 9, height: 9, borderRadius: '50%', backgroundColor: lane.color || '#1a73e8' }} />
                <strong style={{ fontSize: 13.5, color: 'var(--text-primary)' }}>{lane.itemName}</strong>
              </div>
              <span className="badge badge-info">{laneRecords.length}</span>
            </div>

            {laneRecords.map((rec) => (
              <div
                key={rec.id}
                draggable
                onDragStart={() => setDraggingId(rec.id)}
                style={{
                  backgroundColor: 'var(--bg-surface)',
                  border: '1px solid var(--border-subtle)',
                  borderRadius: 12,
                  padding: 12,
                  boxShadow: 'var(--shadow-sm)',
                  cursor: 'grab',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 6,
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <code style={{ fontSize: 11.5 }}>{rec.recordKey || rec.id}</code>
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
              </div>
            ))}
          </div>
        );
      })}
    </div>
  );
}
