'use client';

import React, { useState } from 'react';
import { ColumnDef } from './DataTable';
import KeyboardArrowDownRoundedIcon from '@mui/icons-material/KeyboardArrowDownRounded';
import KeyboardArrowUpRoundedIcon from '@mui/icons-material/KeyboardArrowUpRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import EmptyState from './EmptyState';

export interface MobileCardViewProps<T extends { id: string }> {
  columns: ColumnDef<T>[];
  data: T[];
  selectedIds?: string[];
  onSelectRow?: (id: string) => void;
  enableSelection?: boolean;
  emptyText?: string;
  loading?: boolean;
  onBatchDelete?: (ids: string[]) => void;
  batchActions?: React.ReactNode;
  onRowClick?: (row: T) => void;
  pagination?: {
    page: number;
    pageSize: number;
    total: number;
    totalPages: number;
    onPageChange: (page: number) => void;
    onPageSizeChange?: (pageSize: number) => void;
  };
}

function getCardFieldValue<T>(row: T, key: string): any {
  const r = row as Record<string, any>;
  if (r.data && typeof r.data === 'object' && key in r.data) {
    return r.data[key];
  }
  return r[key];
}

export default function MobileCardView<T extends { id: string }>({
  columns,
  data,
  selectedIds = [],
  onSelectRow,
  enableSelection = false,
  emptyText = 'Chưa có bản ghi nào.',
  loading = false,
  batchActions,
  onRowClick,
  pagination,
}: MobileCardViewProps<T>) {
  // Track which cards are expanded for detailed fields
  const [expandedCardIds, setExpandedCardIds] = useState<Set<string>>(new Set());

  const toggleExpand = (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    setExpandedCardIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  // Filter out selection or action columns for the body display
  const contentColumns = columns.filter(
    (c) => c.key !== 'selection' && c.key !== 'actions' && c.key !== '_actions' && c.key !== 'stt'
  );
  const actionColumn = columns.find((c) => c.key === 'actions' || c.key === '_actions');

  if (loading) {
    return (
      <div
        role="status"
        aria-live="polite"
        style={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          padding: '48px 16px',
          gap: '12px',
          backgroundColor: 'var(--bg-surface)',
          borderRadius: 'var(--radius-lg)',
          border: '1px solid var(--border-subtle)',
        }}
      >
        <div
          style={{
            width: '32px',
            height: '32px',
            border: '3px solid var(--border-subtle)',
            borderTopColor: 'var(--accent-primary)',
            borderRadius: '50%',
            animation: 'spin 0.8s linear infinite',
          }}
        />
        <span style={{ fontSize: '13px', color: 'var(--text-secondary)' }}>
          Đang tải dữ liệu dạng thẻ...
        </span>
      </div>
    );
  }

  if (data.length === 0) {
    return (
      <EmptyState
        title="Không tìm thấy bản ghi"
        description={emptyText}
      />
    );
  }

  return (
    <div
      role="region"
      aria-label="Danh sách thẻ dữ liệu di động"
      style={{ display: 'flex', flexDirection: 'column', gap: '12px', width: '100%' }}
    >
      {/* Batch Actions Bar on Mobile if items selected */}
      {enableSelection && selectedIds.length > 0 && (
        <div
          role="toolbar"
          aria-label="Thao tác hàng loạt trên thiết bị di động"
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '10px 14px',
            backgroundColor: '#e8f0fe',
            border: '1px solid #1a73e8',
            borderRadius: 'var(--radius-md)',
            marginBottom: '4px',
          }}
        >
          <span style={{ fontSize: '13px', fontWeight: 600, color: '#1a73e8' }}>
            Đã chọn {selectedIds.length} bản ghi
          </span>
          <div>{batchActions}</div>
        </div>
      )}

      {/* Cards List */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
        {data.map((row, idx) => {
          const isSelected = selectedIds.includes(row.id);
          const isExpanded = expandedCardIds.has(row.id);

          // Pick primary column (first column or column with label 'Tên', 'Họ tên', 'Mã')
          const primaryCol =
            contentColumns.find(
              (c) =>
                c.key.toLowerCase().includes('name') ||
                c.key.toLowerCase().includes('ten') ||
                c.key.toLowerCase().includes('code') ||
                c.key.toLowerCase().includes('ma')
            ) || contentColumns[0];

          // Secondary columns (first 4 for preview, rest in expansion)
          const remainingCols = contentColumns.filter((c) => c.key !== primaryCol?.key);
          const previewCols = remainingCols.slice(0, 4);
          const extraCols = remainingCols.slice(4);

          return (
            <article
              key={row.id}
              role="article"
              aria-label={`Bản ghi số ${idx + 1}`}
              tabIndex={0}
              onClick={() => onRowClick?.(row)}
              style={{
                backgroundColor: isSelected ? '#f8fafd' : 'var(--bg-surface)',
                border: isSelected ? '1.5px solid #1a73e8' : '1px solid var(--border-subtle)',
                borderRadius: 'var(--radius-md)',
                padding: '14px 16px',
                boxShadow: isSelected ? '0 0 0 2px rgba(26,115,232,0.15)' : 'var(--shadow-sm)',
                transition: 'all 0.18s ease',
                cursor: onRowClick ? 'pointer' : 'default',
              }}
            >
              {/* Header: Checkbox, Primary Field & Expand Toggle */}
              <div
                style={{
                  display: 'flex',
                  alignItems: 'flex-start',
                  justifyContent: 'space-between',
                  gap: '10px',
                  marginBottom: '10px',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flex: 1, minWidth: 0 }}>
                  {enableSelection && (
                    <input
                      type="checkbox"
                      checked={isSelected}
                      onChange={() => onSelectRow?.(row.id)}
                      onClick={(e) => e.stopPropagation()}
                      aria-label={`Chọn bản ghi ${row.id}`}
                      style={{
                        width: '18px',
                        height: '18px',
                        minWidth: '18px',
                        cursor: 'pointer',
                        accentColor: '#1a73e8',
                      }}
                    />
                  )}
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ fontSize: '11px', color: 'var(--text-muted)', fontWeight: 600 }}>
                      #{idx + 1 + (pagination ? (pagination.page - 1) * pagination.pageSize : 0)} &bull;{' '}
                      {primaryCol?.label || (typeof primaryCol?.header === 'string' ? primaryCol.header : primaryCol?.key || 'Bản ghi')}
                    </div>
                    <div
                      style={{
                        fontSize: '14px',
                        fontWeight: 700,
                        color: 'var(--text-primary)',
                        marginTop: '2px',
                        wordBreak: 'break-word',
                      }}
                    >
                      {primaryCol?.render ? primaryCol.render(row, idx) : getCardFieldValue(row, primaryCol?.key || 'id') ?? '---'}
                    </div>
                  </div>
                </div>

                {extraCols.length > 0 && (
                  <button
                    type="button"
                    onClick={(e) => toggleExpand(row.id, e)}
                    aria-expanded={isExpanded}
                    aria-label={isExpanded ? 'Thu gọn chi tiết' : 'Mở rộng chi tiết'}
                    style={{
                      border: '1px solid var(--border-subtle)',
                      backgroundColor: 'var(--bg-secondary)',
                      borderRadius: '16px',
                      padding: '4px 8px',
                      fontSize: '11px',
                      fontWeight: 600,
                      color: 'var(--text-secondary)',
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: '2px',
                      cursor: 'pointer',
                    }}
                  >
                    <span>{isExpanded ? 'Thu gọn' : `+${extraCols.length} trường`}</span>
                    {isExpanded ? (
                      <KeyboardArrowUpRoundedIcon sx={{ fontSize: 16 }} />
                    ) : (
                      <KeyboardArrowDownRoundedIcon sx={{ fontSize: 16 }} />
                    )}
                  </button>
                )}
              </div>

              {/* Grid of Attributes */}
              <div
                style={{
                  display: 'grid',
                  gridTemplateColumns: 'repeat(auto-fit, minmax(135px, 1fr))',
                  gap: '8px 12px',
                  paddingTop: '8px',
                  borderTop: '1px dashed var(--border-subtle)',
                }}
              >
                {previewCols.map((col) => {
                  const label =
                    col.label || (typeof col.header === 'string' ? col.header : col.key);
                  const renderedVal = col.render ? col.render(row, idx) : getCardFieldValue(row, col.key);

                  return (
                    <div key={col.key} style={{ minWidth: 0 }}>
                      <div
                        style={{
                          fontSize: '10.5px',
                          color: 'var(--text-muted)',
                          fontWeight: 600,
                          textTransform: 'uppercase',
                          letterSpacing: '0.02em',
                        }}
                      >
                        {label}
                      </div>
                      <div
                        style={{
                          fontSize: '12.5px',
                          color: 'var(--text-primary)',
                          marginTop: '2px',
                          wordBreak: 'break-word',
                        }}
                      >
                        {renderedVal ?? '---'}
                      </div>
                    </div>
                  );
                })}

                {/* Additional columns shown when expanded */}
                {isExpanded &&
                  extraCols.map((col) => {
                    const label =
                      col.label || (typeof col.header === 'string' ? col.header : col.key);
                    const renderedVal = col.render ? col.render(row, idx) : getCardFieldValue(row, col.key);

                    return (
                      <div
                        key={col.key}
                        style={{
                          minWidth: 0,
                          backgroundColor: 'rgba(0,0,0,0.02)',
                          padding: '4px 6px',
                          borderRadius: '4px',
                        }}
                      >
                        <div
                          style={{
                            fontSize: '10.5px',
                            color: 'var(--text-muted)',
                            fontWeight: 600,
                            textTransform: 'uppercase',
                          }}
                        >
                          {label}
                        </div>
                        <div
                          style={{
                            fontSize: '12.5px',
                            color: 'var(--text-primary)',
                            marginTop: '2px',
                            wordBreak: 'break-word',
                          }}
                        >
                          {renderedVal ?? '---'}
                        </div>
                      </div>
                    );
                  })}
              </div>

              {/* Action column footer if available */}
              {actionColumn && (
                <div
                  style={{
                    marginTop: '12px',
                    paddingTop: '10px',
                    borderTop: '1px solid var(--border-subtle)',
                    display: 'flex',
                    justifyContent: 'flex-end',
                    gap: '8px',
                    flexWrap: 'wrap',
                  }}
                  onClick={(e) => e.stopPropagation()}
                >
                  {actionColumn.render ? actionColumn.render(row, idx) : null}
                </div>
              )}
            </article>
          );
        })}
      </div>

      {/* Pagination Footer */}
      {pagination && pagination.totalPages > 1 && (
        <nav
          role="navigation"
          aria-label="Phân trang danh sách thẻ di động"
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '12px 14px',
            backgroundColor: 'var(--bg-surface)',
            border: '1px solid var(--border-subtle)',
            borderRadius: 'var(--radius-md)',
            marginTop: '4px',
          }}
        >
          <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
            Trang {pagination.page} / {pagination.totalPages} ({pagination.total} bản ghi)
          </span>
          <div style={{ display: 'flex', gap: '6px' }}>
            <button
              type="button"
              className="btn btn-outline"
              disabled={pagination.page <= 1}
              onClick={() => pagination.onPageChange(pagination.page - 1)}
              style={{ padding: '4px 10px', fontSize: '12px', minHeight: '34px' }}
              aria-label="Trang trước"
            >
              &larr; Trước
            </button>
            <button
              type="button"
              className="btn btn-outline"
              disabled={pagination.page >= pagination.totalPages}
              onClick={() => pagination.onPageChange(pagination.page + 1)}
              style={{ padding: '4px 10px', fontSize: '12px', minHeight: '34px' }}
              aria-label="Trang sau"
            >
              Sau &rarr;
            </button>
          </div>
        </nav>
      )}
    </div>
  );
}
