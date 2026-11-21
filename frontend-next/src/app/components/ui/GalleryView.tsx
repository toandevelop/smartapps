'use client';

import React, { useMemo } from 'react';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import PhotoLibraryRoundedIcon from '@mui/icons-material/PhotoLibraryRounded';
import { AppRecord, SysColumn } from '@/lib/types';
import { formatCurrencyVnd } from '@/lib/column-utils';
import { getCategoryItemName } from '@/lib/category-service';
import { resolveLookupLabel } from '@/lib/lookup-service';
import StatusBadge from './StatusBadge';
import EmptyState from './EmptyState';

interface GalleryViewProps {
  records: AppRecord[];
  columns: SysColumn[];
  accentColor?: string;
  canEdit?: boolean;
  onInspectRelations: (rec: AppRecord) => void;
  onEditRecord?: (rec: AppRecord) => void;
}

function formatCellText(col: SysColumn, raw: unknown): string {
  if (raw === undefined || raw === null || raw === '') return '—';
  const dt = (col.dataType || 'STRING').toUpperCase();
  if (dt === 'CURRENCY') return formatCurrencyVnd(raw);
  if (dt === 'NUMBER') {
    const n = Number(raw);
    return Number.isFinite(n) ? n.toLocaleString('vi-VN') : String(raw);
  }
  if (dt === 'BOOLEAN') return raw === true || String(raw).toLowerCase() === 'true' ? 'Có' : 'Không';
  if (col.refCategoryCode) return getCategoryItemName(col.refCategoryCode, String(raw))?.name || String(raw);
  if (col.refTableId || dt === 'LOOKUP') return resolveLookupLabel(col, raw) || String(raw);
  if (dt === 'DATE') {
    const s = String(raw).slice(0, 10);
    if (/^\d{4}-\d{2}-\d{2}$/.test(s)) {
      const [y, m, d] = s.split('-');
      return `${d}/${m}/${y}`;
    }
    return s;
  }
  return String(raw);
}

export default function GalleryView({
  records,
  columns,
  accentColor = 'var(--accent-primary)',
  canEdit = false,
  onInspectRelations,
  onEditRecord,
}: GalleryViewProps) {
  const imageCol = useMemo(
    () => columns.find((c) => (c.dataType || '').toUpperCase() === 'IMAGE'),
    [columns],
  );
  const titleCol = useMemo(
    () =>
      columns.find((c) => c.showInTable !== false && !c.isIdentityKey && (c.dataType === 'STRING' || c.dataType === 'TEXTAREA'))
      || columns.find((c) => c.showInTable !== false),
    [columns],
  );
  const highlightCols = useMemo(
    () =>
      columns
        .filter(
          (c) =>
            c.showInTable !== false &&
            c.columnCode !== imageCol?.columnCode &&
            c.columnCode !== titleCol?.columnCode &&
            !['SUB_TABLE', 'FILE', 'IMAGE'].includes((c.dataType || '').toUpperCase()),
        )
        .slice(0, 4),
    [columns, imageCol, titleCol],
  );

  if (records.length === 0) {
    return (
      <div className="card" style={{ padding: 28 }}>
        <EmptyState
          title="Chưa có bản ghi nào trong chế độ Thư viện"
          description="Thêm bản ghi mới hoặc nới lỏng bộ lọc để hiển thị dạng thẻ trực quan."
          icon={<PhotoLibraryRoundedIcon sx={{ fontSize: 28, color: 'var(--text-muted)' }} />}
        />
      </div>
    );
  }

  return (
    <div className="runtime-gallery-grid">
      {records.map((rec) => {
        const imgUrl = imageCol ? String(rec.data?.[imageCol.columnCode] || '').trim() : '';
        const hasValidImage =
          imgUrl.startsWith('/') || imgUrl.startsWith('http://') || imgUrl.startsWith('https://') || imgUrl.startsWith('data:image/');
        const headline = titleCol ? formatCellText(titleCol, rec.data?.[titleCol.columnCode]) : rec.recordKey || rec.id;

        return (
          <article
            key={rec.id}
            className="card runtime-gallery-card"
            onClick={() => onInspectRelations(rec)}
            style={{ cursor: 'pointer', padding: 0, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}
          >
            <div
              style={{
                height: 148,
                position: 'relative',
                background: hasValidImage
                  ? 'var(--bg-secondary)'
                  : `linear-gradient(135deg, ${accentColor}22 0%, var(--accent-primary-container) 100%)`,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                overflow: 'hidden',
                borderBottom: '1px solid var(--border-light)',
              }}
            >
              {hasValidImage ? (
                // eslint-disable-next-line @next/next/no-img-element
                <img
                  src={imgUrl}
                  alt={headline}
                  style={{ width: '100%', height: '100%', objectFit: 'cover' }}
                />
              ) : (
                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6, color: 'var(--text-muted)' }}>
                  <PhotoLibraryRoundedIcon sx={{ fontSize: 32, opacity: 0.45 }} />
                  <code style={{ fontSize: 11 }}>{rec.recordKey || rec.id.slice(0, 10)}</code>
                </div>
              )}
              <div style={{ position: 'absolute', top: 10, right: 10 }}>
                <StatusBadge status={rec.approvalStatus || rec.status} size="sm" />
              </div>
            </div>

            <div style={{ padding: '14px 16px', display: 'flex', flexDirection: 'column', gap: 8, flex: 1 }}>
              <div style={{ fontSize: 14.5, fontWeight: 700, color: 'var(--text-primary)', lineHeight: 1.35 }}>
                {headline}
              </div>
              {rec.recordKey && (
                <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
                  <code>{rec.recordKey}</code>
                </div>
              )}

              <div style={{ display: 'flex', flexDirection: 'column', gap: 5, marginTop: 2 }}>
                {highlightCols.map((col) => (
                  <div
                    key={col.id}
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'baseline',
                      gap: 10,
                      fontSize: 12,
                    }}
                  >
                    <span style={{ color: 'var(--text-muted)', flexShrink: 0 }}>{col.columnName}</span>
                    <span
                      style={{
                        color: 'var(--text-primary)',
                        fontWeight: col.dataType === 'CURRENCY' || col.dataType === 'NUMBER' ? 700 : 500,
                        textAlign: 'right',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                      }}
                    >
                      {formatCellText(col, rec.data?.[col.columnCode])}
                    </span>
                  </div>
                ))}
              </div>

              <div
                style={{
                  marginTop: 'auto',
                  paddingTop: 10,
                  borderTop: '1px solid var(--border-light)',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  gap: 6,
                }}
                onClick={(e) => e.stopPropagation()}
              >
                <button type="button" className="btn btn-outline btn-xs" onClick={() => onInspectRelations(rec)}>
                  <AccountTreeRoundedIcon sx={{ fontSize: 14 }} /> Hồ sơ 360°
                </button>
                {canEdit && onEditRecord && (
                  <button type="button" className="btn btn-outline btn-xs" onClick={() => onEditRecord(rec)}>
                    <EditRoundedIcon sx={{ fontSize: 14 }} /> Sửa
                  </button>
                )}
              </div>
            </div>
          </article>
        );
      })}
    </div>
  );
}
