'use client';

import React, { useState, useRef, useEffect } from 'react';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import CheckRoundedIcon from '@mui/icons-material/CheckRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import StarRoundedIcon from '@mui/icons-material/StarRounded';
import StarBorderRoundedIcon from '@mui/icons-material/StarBorderRounded';
import SearchableSelect, { SearchableSelectOption } from './SearchableSelect';
import BooleanSwitch, { normalizeBoolVal } from './BooleanSwitch';
import {
  MONDAY_COLOR_SWATCHES,
  MONDAY_PRIORITY_PRESETS,
  MONDAY_STATUS_PRESETS,
  clampProgressValue,
  clampRatingValue,
  getMondayProgressColor,
  parseTagsValue,
  resolveMondayPriorityStyle,
  resolveMondayStatusStyle,
} from '@/lib/monday-fields';
import { SysColumn } from '@/lib/types';

interface DynamicInlineCellProps {
  rowId: string;
  column: SysColumn;
  value: unknown;
  displayNode: React.ReactNode;
  selectOptions?: SearchableSelectOption[];
  disabled?: boolean;
  onDirectSave?: (rowId: string, field: string, newVal: unknown) => Promise<boolean>;
}

export default function DynamicInlineCell({
  rowId,
  column,
  value,
  displayNode,
  selectOptions = [],
  disabled = false,
  onDirectSave,
}: DynamicInlineCellProps) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState<unknown>(value ?? '');
  const [saving, setSaving] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const dt = (column.dataType || 'STRING').toUpperCase();
  const isLocked = disabled || !onDirectSave || Boolean(column.formula);

  useEffect(() => {
    if (!editing) return;
    const timer = setTimeout(() => inputRef.current?.focus(), 20);
    return () => clearTimeout(timer);
  }, [editing]);

  const startEditing = () => {
    if (isLocked) return;
    if (dt === 'DATE' && value) setDraft(String(value).substring(0, 10));
    else if (dt === 'DATETIME' && value) setDraft(String(value).replace(' ', 'T').substring(0, 16));
    else if (dt === 'TIME' && value) setDraft(String(value).substring(0, 5));
    else if (dt === 'TAGS') setDraft(parseTagsValue(value).join(', '));
    else if (dt === 'PROGRESS') setDraft(clampProgressValue(value));
    else setDraft(value ?? '');
    setEditing(true);
  };

  const commit = async (nextVal: unknown) => {
    if (!onDirectSave) return;
    const normalizedVal = dt === 'TAGS' ? parseTagsValue(nextVal) : nextVal;
    if (JSON.stringify(normalizedVal ?? '') === JSON.stringify(value ?? '')) {
      setEditing(false);
      return;
    }
    setSaving(true);
    try {
      const ok = await onDirectSave(rowId, column.columnCode, normalizedVal);
      if (ok) setEditing(false);
    } finally {
      setSaving(false);
    }
  };

  if (dt === 'BOOLEAN' && !isLocked) {
    const boolVal = Boolean(normalizeBoolVal(value));
    return (
      <BooleanSwitch
        size="sm"
        checked={boolVal}
        disabled={saving}
        onChange={(next) => {
          void onDirectSave?.(rowId, column.columnCode, next);
        }}
      />
    );
  }

  // 1-Click Direct Star Rating in Table Cell (monday.com Rating column behavior)
  if (dt === 'RATING' && !isLocked) {
    const curStars = clampRatingValue(value);
    return (
      <div
        role="group"
        aria-label={`Đánh giá ${column.columnName}`}
        onClick={(e) => e.stopPropagation()}
        style={{ display: 'inline-flex', alignItems: 'center', gap: 1 }}
      >
        {[1, 2, 3, 4, 5].map((star) => {
          const filled = star <= curStars;
          return (
            <button
              key={star}
              type="button"
              disabled={saving}
              aria-label={`Chấm ${star} sao cho ${column.columnName}`}
              onClick={() => void commit(curStars === star && !column.isRequired ? 0 : star)}
              style={{
                border: 'none',
                background: 'transparent',
                padding: 1,
                cursor: saving ? 'wait' : 'pointer',
                color: filled ? '#ffcb00' : '#cbd5e1',
                display: 'inline-flex',
              }}
            >
              {filled ? <StarRoundedIcon sx={{ fontSize: 18 }} /> : <StarBorderRoundedIcon sx={{ fontSize: 18 }} />}
            </button>
          );
        })}
      </div>
    );
  }

  if (!editing) {
    return (
      <div
        className={`staging-inline-cell-view ${isLocked ? 'is-locked' : ''}`}
        tabIndex={isLocked ? undefined : 0}
        role={isLocked ? undefined : 'button'}
        aria-label={isLocked ? undefined : `Sửa ${column.columnName}`}
        onClick={startEditing}
        onKeyDown={(e) => {
          if (!isLocked && (e.key === 'Enter' || e.key === 'F2')) {
            e.preventDefault();
            startEditing();
          }
        }}
        title={column.formula ? `Công thức tự động: ${column.formula}` : isLocked ? undefined : 'Nhấp hoặc nhấn Enter để sửa nhanh'}
      >
        <div style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {displayNode}
        </div>
        {!isLocked && (
          <span className="cell-edit-hint">
            <EditRoundedIcon sx={{ fontSize: 13 }} />
          </span>
        )}
      </div>
    );
  }

  // Inline monday.com Status / Priority 1-click color pill selector
  if (dt === 'STATUS' || dt === 'PRIORITY') {
    const presets =
      selectOptions.length > 0
        ? selectOptions.map((o) =>
            dt === 'PRIORITY'
              ? resolveMondayPriorityStyle(o.value, o.subLabel || o.label)
              : resolveMondayStatusStyle(o.value, undefined, o.subLabel || o.label)
          )
        : dt === 'PRIORITY'
        ? MONDAY_PRIORITY_PRESETS
        : MONDAY_STATUS_PRESETS;

    return (
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          gap: 4,
          padding: 4,
          borderRadius: 8,
          backgroundColor: 'var(--bg-surface)',
          border: '1px solid var(--border-subtle)',
          boxShadow: 'var(--shadow-sm)',
          minWidth: 190,
        }}
      >
        {presets.map((p) => (
          <button
            key={p.code}
            type="button"
            disabled={saving}
            onClick={() => void commit(p.code)}
            className="monday-status-pill"
            style={{
              backgroundColor: p.bg,
              color: p.color,
              height: 24,
              minWidth: 74,
              fontSize: 11,
              padding: '0 8px',
            }}
          >
            {p.label}
          </button>
        ))}
        <button
          type="button"
          className="btn btn-outline btn-xs"
          onClick={() => setEditing(false)}
          title="Đóng"
        >
          <CloseRoundedIcon sx={{ fontSize: 12 }} />
        </button>
      </div>
    );
  }

  // Inline monday.com Progress slider
  if (dt === 'PROGRESS') {
    const pct = clampProgressValue(draft);
    const barColor = getMondayProgressColor(pct);
    return (
      <div
        onClick={(e) => e.stopPropagation()}
        style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 155 }}
      >
        <input
          type="range"
          min={0}
          max={100}
          step={5}
          value={pct}
          disabled={saving}
          onChange={(e) => setDraft(Number(e.target.value))}
          style={{ flex: 1, accentColor: barColor }}
        />
        <span style={{ fontSize: 11.5, fontWeight: 700, minWidth: 34 }}>{pct}%</span>
        <button type="button" className="btn btn-primary btn-xs" disabled={saving} onClick={() => void commit(pct)} title="Lưu">
          <CheckRoundedIcon sx={{ fontSize: 13 }} />
        </button>
        <button type="button" className="btn btn-outline btn-xs" disabled={saving} onClick={() => setEditing(false)} title="Hủy">
          <CloseRoundedIcon sx={{ fontSize: 13 }} />
        </button>
      </div>
    );
  }

  // Inline monday.com Color Swatch Picker
  if (dt === 'COLOR') {
    return (
      <div
        onClick={(e) => e.stopPropagation()}
        style={{ display: 'flex', alignItems: 'center', gap: 4, flexWrap: 'wrap', minWidth: 160 }}
      >
        {MONDAY_COLOR_SWATCHES.slice(0, 10).map((sw) => (
          <button
            key={sw}
            type="button"
            disabled={saving}
            aria-label={`Chọn màu ${sw}`}
            onClick={() => void commit(sw)}
            style={{
              width: 18,
              height: 18,
              borderRadius: 4,
              backgroundColor: sw,
              border: '1px solid rgba(0,0,0,0.15)',
              cursor: 'pointer',
            }}
          />
        ))}
        <button type="button" className="btn btn-outline btn-xs" onClick={() => setEditing(false)} title="Đóng">
          <CloseRoundedIcon sx={{ fontSize: 12 }} />
        </button>
      </div>
    );
  }

  if (column.refTableId || dt === 'LOOKUP' || column.refCategoryCode || dt === 'SELECT') {
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 4, minWidth: 175 }}>
        <SearchableSelect
          size="sm"
          options={selectOptions}
          value={String(draft ?? '')}
          onChange={(v) => void commit(v)}
          disabled={saving}
        />
        <button
          type="button"
          className="btn btn-outline btn-xs"
          onClick={() => setEditing(false)}
          title="Đóng"
        >
          <CloseRoundedIcon sx={{ fontSize: 13 }} />
        </button>
      </div>
    );
  }

  const inputType =
    dt === 'DATE'
      ? 'date'
      : dt === 'DATETIME'
      ? 'datetime-local'
      : dt === 'TIME'
      ? 'time'
      : dt === 'NUMBER' || dt === 'CURRENCY'
      ? 'number'
      : 'text';

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
      <input
        ref={inputRef}
        className="staging-inline-input"
        type={inputType}
        value={typeof draft === 'number' || typeof draft === 'string' ? draft : ''}
        disabled={saving}
        onChange={(e) =>
          setDraft(
            dt === 'NUMBER' || dt === 'CURRENCY'
              ? e.target.value === ''
                ? null
                : Number(e.target.value)
              : e.target.value
          )
        }
        onKeyDown={(e) => {
          if (e.nativeEvent.isComposing || e.keyCode === 229) return;
          if (e.key === 'Enter') {
            e.preventDefault();
            void commit(draft);
          } else if (e.key === 'Escape') {
            e.preventDefault();
            setEditing(false);
          }
        }}
      />
      <button
        type="button"
        className="btn btn-primary btn-xs"
        disabled={saving}
        onClick={() => void commit(draft)}
        title="Lưu"
      >
        <CheckRoundedIcon sx={{ fontSize: 13 }} />
      </button>
      <button
        type="button"
        className="btn btn-outline btn-xs"
        disabled={saving}
        onClick={() => setEditing(false)}
        title="Hủy"
      >
        <CloseRoundedIcon sx={{ fontSize: 13 }} />
      </button>
    </div>
  );
}
