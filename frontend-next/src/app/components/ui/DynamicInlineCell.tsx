'use client';

import React, { useState, useRef, useEffect } from 'react';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import CheckRoundedIcon from '@mui/icons-material/CheckRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import SearchableSelect, { SearchableSelectOption } from './SearchableSelect';
import BooleanSwitch, { normalizeBoolVal } from './BooleanSwitch';
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
    setDraft(dt === 'DATE' && value ? String(value).substring(0, 10) : value ?? '');
    setEditing(true);
  };

  const commit = async (nextVal: unknown) => {
    if (!onDirectSave) return;
    if (String(nextVal ?? '') === String(value ?? '')) {
      setEditing(false);
      return;
    }
    setSaving(true);
    try {
      const ok = await onDirectSave(rowId, column.columnCode, nextVal);
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
          onDirectSave?.(rowId, column.columnCode, next);
        }}
      />
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

  if (column.refTableId || dt === 'LOOKUP' || column.refCategoryCode || dt === 'SELECT') {
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 4, minWidth: 175 }}>
        <SearchableSelect
          size="sm"
          options={selectOptions}
          value={String(draft ?? '')}
          onChange={(v) => commit(v)}
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

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
      <input
        ref={inputRef}
        className="staging-inline-input"
        type={dt === 'DATE' ? 'date' : dt === 'NUMBER' || dt === 'CURRENCY' ? 'number' : 'text'}
        value={typeof draft === 'number' || typeof draft === 'string' ? draft : ''}
        disabled={saving}
        onChange={(e) =>
          setDraft(dt === 'NUMBER' || dt === 'CURRENCY' ? (e.target.value === '' ? null : Number(e.target.value)) : e.target.value)
        }
        onKeyDown={(e) => {
          if (e.nativeEvent.isComposing || e.keyCode === 229) return;
          if (e.key === 'Enter') {
            e.preventDefault();
            commit(draft);
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
        onClick={() => commit(draft)}
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
