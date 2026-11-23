'use client';

import React, { useEffect, useMemo, useState } from 'react';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import KeyboardArrowUpRoundedIcon from '@mui/icons-material/KeyboardArrowUpRounded';
import KeyboardArrowDownRoundedIcon from '@mui/icons-material/KeyboardArrowDownRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import { AppRecord, SysColumn } from '@/lib/types';
import { formatCurrencyVnd } from '@/lib/column-utils';
import {
  clampProgressValue,
  clampRatingValue,
  formatDateTimeDisplay,
  parseTagsValue,
  resolveMondayPriorityStyle,
  resolveMondayStatusStyle,
} from '@/lib/monday-fields';
import StatusBadge from './StatusBadge';
import Record360Content from './Record360Content';
import EmptyState from './EmptyState';

interface RuntimeSplitViewProps {
  records: AppRecord[];
  columns: SysColumn[];
  tableDisplayName: string;
  appCode: string;
  loading?: boolean;
  canEdit?: boolean;
  canApprove?: boolean;
  searchValue: string;
  onSearchChange: (val: string) => void;
  onChanged: () => void;
}

function renderQuickVal(col: SysColumn, val: unknown): string {
  if (val === undefined || val === null || val === '') return '—';
  const dt = (col.dataType || 'STRING').toUpperCase();
  if (dt === 'STATUS') return resolveMondayStatusStyle(val).label;
  if (dt === 'PRIORITY') return resolveMondayPriorityStyle(val).label;
  if (dt === 'PROGRESS') return `${clampProgressValue(val)}%`;
  if (dt === 'RATING') return `★ ${clampRatingValue(val)}/5`;
  if (dt === 'TAGS') {
    const tags = parseTagsValue(val);
    return tags.length > 0 ? tags.map((t) => `#${t}`).join(' ') : '—';
  }
  if (dt === 'DATETIME' || dt === 'TIME') return formatDateTimeDisplay(val, dt);
  if (dt === 'CURRENCY') return formatCurrencyVnd(val);
  if (dt === 'BOOLEAN') return val === true || String(val).toLowerCase() === 'true' ? 'Có' : 'Không';
  if (Array.isArray(val)) return `${val.length} dòng`;
  return String(val);
}

export default function RuntimeSplitView({
  records,
  columns,
  tableDisplayName,
  appCode,
  loading = false,
  canEdit = false,
  canApprove = false,
  searchValue,
  onSearchChange,
  onChanged,
}: RuntimeSplitViewProps) {
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const activeRecord = useMemo(() => {
    if (records.length === 0) return null;
    if (selectedId) {
      const found = records.find((r) => r.id === selectedId);
      if (found) return found;
    }
    return records[0];
  }, [records, selectedId]);

  const activeIndex = useMemo(
    () => (activeRecord ? records.findIndex((r) => r.id === activeRecord.id) : -1),
    [records, activeRecord],
  );

  // Keyboard ↑ / ↓ quick navigation when not typing in an input
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement | null)?.tagName?.toLowerCase();
      if (tag === 'input' || tag === 'textarea' || tag === 'select') return;
      if (records.length === 0) return;
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        const nextIdx = Math.min(records.length - 1, Math.max(0, activeIndex) + 1);
        setSelectedId(records[nextIdx]?.id || null);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        const prevIdx = Math.max(0, (activeIndex < 0 ? 0 : activeIndex) - 1);
        setSelectedId(records[prevIdx]?.id || null);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [records, activeIndex]);

  const titleCol = columns.find((c) => c.showInTable !== false && !c.isIdentityKey && (c.dataType === 'STRING' || c.dataType === 'TEXTAREA'))
    || columns.find((c) => c.showInTable !== false);
  const metaCols = columns.filter((c) => c.showInTable !== false && c.columnCode !== titleCol?.columnCode && c.dataType !== 'SUB_TABLE').slice(0, 2);

  return (
    <div className="runtime-split-grid">
      {/* Left Master List */}
      <div className="card runtime-split-list">
        <div
          style={{
            padding: '10px 12px',
            borderBottom: '1px solid var(--border-light)',
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            backgroundColor: 'var(--bg-surface)',
          }}
        >
          <div style={{ position: 'relative', flex: 1 }}>
            <SearchRoundedIcon
              sx={{
                fontSize: 16,
                position: 'absolute',
                left: 10,
                top: '50%',
                transform: 'translateY(-50%)',
                color: 'var(--text-muted)',
              }}
            />
            <input
              type="text"
              className="form-input"
              style={{ height: 32, paddingLeft: 32, fontSize: 12.5 }}
              placeholder="Tìm nhanh trong danh sách..."
              value={searchValue}
              onChange={(e) => onSearchChange(e.target.value)}
            />
          </div>
          <div style={{ display: 'inline-flex', gap: 4 }}>
            <button
              type="button"
              className="btn btn-outline btn-xs"
              disabled={activeIndex <= 0}
              onClick={() => setSelectedId(records[Math.max(0, activeIndex - 1)]?.id || null)}
              title="Hồ sơ trước (↑)"
              aria-label="Hồ sơ trước"
            >
              <KeyboardArrowUpRoundedIcon sx={{ fontSize: 16 }} />
            </button>
            <button
              type="button"
              className="btn btn-outline btn-xs"
              disabled={activeIndex < 0 || activeIndex >= records.length - 1}
              onClick={() => setSelectedId(records[Math.min(records.length - 1, activeIndex + 1)]?.id || null)}
              title="Hồ sơ tiếp theo (↓)"
              aria-label="Hồ sơ tiếp theo"
            >
              <KeyboardArrowDownRoundedIcon sx={{ fontSize: 16 }} />
            </button>
          </div>
        </div>

        <div className="runtime-split-scroll">
          {loading ? (
            <div style={{ padding: 14, display: 'flex', flexDirection: 'column', gap: 10 }}>
              <div className="skeleton-box" style={{ height: 64, width: '100%' }} />
              <div className="skeleton-box" style={{ height: 64, width: '100%' }} />
              <div className="skeleton-box" style={{ height: 64, width: '100%' }} />
            </div>
          ) : records.length === 0 ? (
            <div style={{ padding: 24, textAlign: 'center', fontSize: 12.5, color: 'var(--text-muted)' }}>
              Không có bản ghi nào để hiển thị.
            </div>
          ) : (
            records.map((rec) => {
              const isSelected = activeRecord?.id === rec.id;
              const headline = titleCol ? renderQuickVal(titleCol, rec.data?.[titleCol.columnCode]) : rec.recordKey || rec.id;
              return (
                <button
                  key={rec.id}
                  type="button"
                  className={`runtime-split-item ${isSelected ? 'active' : ''}`}
                  onClick={() => setSelectedId(rec.id)}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
                    <span
                      style={{
                        fontSize: 13.5,
                        fontWeight: 700,
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                      }}
                    >
                      {headline}
                    </span>
                    <StatusBadge status={rec.approvalStatus || rec.status} size="sm" />
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, fontSize: 11.5, color: 'var(--text-muted)' }}>
                    <code>{rec.recordKey || rec.id.slice(0, 10)}</code>
                    <span>{rec.updatedAt ? new Date(rec.updatedAt).toLocaleDateString('vi-VN') : ''}</span>
                  </div>
                  {metaCols.length > 0 && (
                    <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', fontSize: 11.5, color: 'var(--text-secondary)' }}>
                      {metaCols.map((mc) => (
                        <span key={mc.id} style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '100%' }}>
                          <span style={{ color: 'var(--text-muted)' }}>{mc.columnName}:</span> {renderQuickVal(mc, rec.data?.[mc.columnCode])}
                        </span>
                      ))}
                    </div>
                  )}
                </button>
              );
            })
          )}
        </div>
      </div>

      {/* Right Embedded Record 360° Workspace */}
      <div className="card runtime-split-detail">
        {activeRecord ? (
          <Record360Content
            key={activeRecord.id}
            open={true}
            variant="embedded"
            record={activeRecord}
            columns={columns}
            tableDisplayName={tableDisplayName}
            appCode={appCode}
            canEdit={canEdit}
            canApprove={canApprove}
            onChanged={onChanged}
          />
        ) : (
          <div style={{ padding: 32 }}>
            <EmptyState
              title="Chọn một hồ sơ ở danh sách bên trái"
              description="Chế độ Chia đôi (Split View) giúp bạn duyệt, xem quan hệ 360° và xử lý hàng loạt cực nhanh bằng phím ↑ / ↓."
              icon={<AccountTreeRoundedIcon sx={{ fontSize: 28, color: 'var(--text-muted)' }} />}
            />
          </div>
        )}
      </div>
    </div>
  );
}
