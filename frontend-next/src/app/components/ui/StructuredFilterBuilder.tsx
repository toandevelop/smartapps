'use client';

import React, { useState } from 'react';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import FilterAltRoundedIcon from '@mui/icons-material/FilterAltRounded';
import ClearAllRoundedIcon from '@mui/icons-material/ClearAllRounded';
import type { QueryCondition, StructuredQueryFilter, SysCategoryItem, SysColumn } from '@/lib/types';
import {
  APPROVAL_STATUS_OPTIONS,
  DATE_PRESET_OPTIONS,
  filterValidConditions,
  getOperatorsForColumn,
} from '@/lib/query-filter';
import { fetchCategoryItems, getCategoryItems } from '@/lib/category-service';
import { getLookupOptions } from '@/lib/lookup-service';

interface StructuredFilterBuilderProps {
  columns: SysColumn[];
  approvalEnabled?: boolean;
  value: StructuredQueryFilter;
  onApply: (next: StructuredQueryFilter) => void;
  onClear: () => void;
}

function makeDefaultCondition(
  columns: SysColumn[],
  approvalEnabled?: boolean,
): QueryCondition {
  const firstCol = columns.find((c) => c.dataType !== 'SUB_TABLE');
  if (firstCol) {
    const ops = getOperatorsForColumn(firstCol.columnCode, firstCol.dataType);
    const defaultOp = ops[0]?.value || 'contains';
    return {
      field: firstCol.columnCode,
      op: defaultOp,
      value: defaultOp === 'date_preset' ? 'TODAY' : '',
    };
  }
  if (approvalEnabled) {
    return { field: 'approvalStatus', op: 'eq', value: 'PENDING' };
  }
  return { field: '', op: 'contains', value: '' };
}

/** Visual multi-condition AND/OR query builder mapped directly to RecordService SQL JSON filters (FUN-06). */
export default function StructuredFilterBuilder({
  columns,
  approvalEnabled = false,
  value,
  onApply,
  onClear,
}: StructuredFilterBuilderProps) {
  const filterableColumns = columns.filter((c) => c.dataType !== 'SUB_TABLE');

  const [logic, setLogic] = useState<'AND' | 'OR'>(value.logic === 'OR' ? 'OR' : 'AND');
  const [conditions, setConditions] = useState<QueryCondition[]>(() =>
    value.conditions.length > 0
      ? value.conditions
      : [makeDefaultCondition(filterableColumns, approvalEnabled)],
  );
  const [extraCategoryCache, setExtraCategoryCache] = useState<Record<string, SysCategoryItem[]>>({});

  const ensureCategoryLoaded = (catCode?: string | null) => {
    if (!catCode) return;
    const key = catCode.trim().toUpperCase();
    if (getCategoryItems(key).length > 0 || extraCategoryCache[key]) return;
    void fetchCategoryItems(key).then((items) => {
      setExtraCategoryCache((prev) => ({ ...prev, [key]: items }));
    });
  };

  const handleAddCondition = () => {
    setConditions((prev) => [...prev, makeDefaultCondition(filterableColumns, approvalEnabled)]);
  };

  const handleRemoveCondition = (idx: number) => {
    setConditions((prev) => prev.filter((_, i) => i !== idx));
  };

  const handleFieldChange = (idx: number, nextField: string) => {
    const targetCol = filterableColumns.find((c) => c.columnCode === nextField);
    if (targetCol?.refCategoryCode) {
      ensureCategoryLoaded(targetCol.refCategoryCode);
    }
    const ops = getOperatorsForColumn(nextField, targetCol?.dataType);
    const nextOp = ops[0]?.value || 'contains';
    setConditions((prev) =>
      prev.map((item, i) =>
        i === idx
          ? {
              field: nextField,
              op: nextOp,
              value: nextOp === 'date_preset' ? 'TODAY' : '',
            }
          : item,
      ),
    );
  };

  const handleOpChange = (idx: number, nextOp: QueryCondition['op']) => {
    setConditions((prev) =>
      prev.map((item, i) =>
        i === idx
          ? {
              field: item.field,
              op: nextOp,
              value: nextOp === 'date_preset' ? 'TODAY' : nextOp === 'is_empty' || nextOp === 'not_empty' ? '' : item.value || '',
              value2: nextOp === 'between' ? item.value2 || '' : undefined,
            }
          : item,
      ),
    );
  };

  const handleValueChange = (idx: number, val: string, isSecond = false) => {
    setConditions((prev) =>
      prev.map((item, i) =>
        i === idx ? (isSecond ? { ...item, value2: val } : { ...item, value: val }) : item,
      ),
    );
  };

  const validPreview = filterValidConditions(conditions);

  return (
    <div
      style={{
        padding: '12px 14px',
        borderRadius: 10,
        backgroundColor: 'var(--bg-secondary)',
        border: '1px solid var(--border-subtle)',
        display: 'flex',
        flexDirection: 'column',
        gap: 10,
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: 12.5, fontWeight: 700, color: 'var(--text-primary)' }}>
            <FilterAltRoundedIcon sx={{ fontSize: 16, color: '#1a73e8' }} />
            Truy vấn nâng cao (AND / OR)
          </span>
          <div className="segmented-tabs" style={{ padding: 2 }}>
            <button
              type="button"
              className={`segmented-tab ${logic === 'AND' ? 'active' : ''}`}
              style={{ height: 24, fontSize: 11.5, padding: '0 10px' }}
              onClick={() => setLogic('AND')}
            >
              Khớp TẤT CẢ (AND)
            </button>
            <button
              type="button"
              className={`segmented-tab ${logic === 'OR' ? 'active' : ''}`}
              style={{ height: 24, fontSize: 11.5, padding: '0 10px' }}
              onClick={() => setLogic('OR')}
            >
              Khớp BẤT KỲ (OR)
            </button>
          </div>
        </div>

        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
          <button type="button" className="btn btn-outline btn-xs" onClick={handleAddCondition}>
            <AddRoundedIcon sx={{ fontSize: 14 }} /> Thêm điều kiện
          </button>
          <button
            type="button"
            className="btn btn-outline btn-xs"
            style={{ color: 'var(--text-muted)' }}
            onClick={() => {
              setConditions([makeDefaultCondition(filterableColumns, approvalEnabled)]);
              onClear();
            }}
          >
            <ClearAllRoundedIcon sx={{ fontSize: 14 }} /> Xóa truy vấn
          </button>
          <button
            type="button"
            className="btn btn-primary btn-xs"
            onClick={() => onApply({ logic, conditions: validPreview })}
          >
            Áp dụng ({validPreview.length})
          </button>
        </div>
      </div>

      {conditions.length === 0 ? (
        <div style={{ fontSize: 12.5, color: 'var(--text-muted)', padding: '6px 0' }}>
          Chưa có điều kiện nào. Nhấn &quot;+ Thêm điều kiện&quot; để bắt đầu lọc kết hợp AND / OR.
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          {conditions.map((cond, idx) => {
            const col = filterableColumns.find((c) => c.columnCode === cond.field);
            const operators = getOperatorsForColumn(cond.field, col?.dataType);
            const dt = (col?.dataType || '').toUpperCase();
            const catKey = col?.refCategoryCode?.trim().toUpperCase() || '';
            const cachedCatItems = catKey
              ? (getCategoryItems(catKey).length > 0 ? getCategoryItems(catKey) : extraCategoryCache[catKey] || [])
              : [];
            const lookupOpts = col?.refTableId ? getLookupOptions(col) : [];
            const needsNoValue = cond.op === 'is_empty' || cond.op === 'not_empty';
            const canPickChoice = cond.op === 'eq' || cond.op === 'neq';

            return (
              <div
                key={`cond_${idx}`}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  flexWrap: 'wrap',
                  padding: '6px 8px',
                  borderRadius: 8,
                  backgroundColor: 'var(--bg-surface)',
                  border: '1px solid var(--border-subtle)',
                }}
              >
                <span
                  style={{
                    fontSize: 11,
                    fontWeight: 700,
                    minWidth: 42,
                    textAlign: 'center',
                    padding: '2px 6px',
                    borderRadius: 6,
                    backgroundColor: 'var(--info-bg)',
                    color: 'var(--info-text)',
                  }}
                >
                  {idx === 0 ? 'NẾU' : logic}
                </span>

                <select
                  className="form-select"
                  style={{ width: 175, height: 30, fontSize: 12.5, padding: '2px 8px' }}
                  value={cond.field}
                  onChange={(e) => handleFieldChange(idx, e.target.value)}
                >
                  <option value="">-- Chọn trường --</option>
                  {filterableColumns.map((c) => (
                    <option key={c.id} value={c.columnCode}>
                      {c.columnName}
                    </option>
                  ))}
                  {approvalEnabled && <option value="approvalStatus">Trạng thái duyệt</option>}
                </select>

                <select
                  className="form-select"
                  style={{ width: 170, height: 30, fontSize: 12.5, padding: '2px 8px' }}
                  value={cond.op}
                  onChange={(e) => handleOpChange(idx, e.target.value as QueryCondition['op'])}
                >
                  {operators.map((op) => (
                    <option key={op.value} value={op.value}>
                      {op.label}
                    </option>
                  ))}
                </select>

                {needsNoValue ? (
                  <span style={{ fontSize: 12, color: 'var(--text-muted)', fontStyle: 'italic', flex: 1, minWidth: 160 }}>
                    (Kiểm tra trực tiếp không cần nhập giá trị)
                  </span>
                ) : cond.field === 'approvalStatus' ? (
                  <select
                    className="form-select"
                    style={{ flex: 1, minWidth: 180, height: 30, fontSize: 12.5, padding: '2px 8px' }}
                    value={cond.value || ''}
                    onChange={(e) => handleValueChange(idx, e.target.value)}
                  >
                    <option value="">-- Chọn trạng thái --</option>
                    {APPROVAL_STATUS_OPTIONS.map((opt) => (
                      <option key={opt.value} value={opt.value}>
                        {opt.label} ({opt.value})
                      </option>
                    ))}
                  </select>
                ) : cond.op === 'date_preset' ? (
                  <select
                    className="form-select"
                    style={{ flex: 1, minWidth: 180, height: 30, fontSize: 12.5, padding: '2px 8px' }}
                    value={cond.value || 'TODAY'}
                    onChange={(e) => handleValueChange(idx, e.target.value)}
                  >
                    {DATE_PRESET_OPTIONS.map((opt) => (
                      <option key={opt.value} value={opt.value}>
                        {opt.label}
                      </option>
                    ))}
                  </select>
                ) : cond.op === 'between' ? (
                  <div style={{ display: 'flex', alignItems: 'center', gap: 6, flex: 1, minWidth: 210 }}>
                    <input
                      type="number"
                      step="any"
                      className="form-input"
                      style={{ height: 30, fontSize: 12.5, padding: '2px 8px' }}
                      placeholder="Từ giá trị..."
                      value={cond.value || ''}
                      onChange={(e) => handleValueChange(idx, e.target.value)}
                    />
                    <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>→</span>
                    <input
                      type="number"
                      step="any"
                      className="form-input"
                      style={{ height: 30, fontSize: 12.5, padding: '2px 8px' }}
                      placeholder="Đến giá trị..."
                      value={cond.value2 || ''}
                      onChange={(e) => handleValueChange(idx, e.target.value, true)}
                    />
                  </div>
                ) : canPickChoice && cachedCatItems.length > 0 ? (
                  <select
                    className="form-select"
                    style={{ flex: 1, minWidth: 180, height: 30, fontSize: 12.5, padding: '2px 8px' }}
                    value={cond.value || ''}
                    onChange={(e) => handleValueChange(idx, e.target.value)}
                  >
                    <option value="">-- Chọn danh mục --</option>
                    {cachedCatItems.map((it) => (
                      <option key={it.id} value={it.itemCode}>
                        {it.itemName}
                      </option>
                    ))}
                  </select>
                ) : canPickChoice && lookupOpts.length > 0 ? (
                  <select
                    className="form-select"
                    style={{ flex: 1, minWidth: 180, height: 30, fontSize: 12.5, padding: '2px 8px' }}
                    value={cond.value || ''}
                    onChange={(e) => handleValueChange(idx, e.target.value)}
                  >
                    <option value="">-- Chọn bản ghi liên kết --</option>
                    {lookupOpts.map((opt) => (
                      <option key={opt.value} value={opt.value}>
                        {opt.label}
                      </option>
                    ))}
                  </select>
                ) : dt === 'BOOLEAN' ? (
                  <select
                    className="form-select"
                    style={{ flex: 1, minWidth: 150, height: 30, fontSize: 12.5, padding: '2px 8px' }}
                    value={cond.value || ''}
                    onChange={(e) => handleValueChange(idx, e.target.value)}
                  >
                    <option value="">-- Chọn Có / Không --</option>
                    <option value="true">Có (true)</option>
                    <option value="false">Không (false)</option>
                  </select>
                ) : (
                  <input
                    type={
                      dt === 'DATE' && (cond.op === 'eq' || cond.op === 'neq')
                        ? 'date'
                        : (dt === 'NUMBER' || dt === 'CURRENCY') && cond.op !== 'in'
                        ? 'number'
                        : 'text'
                    }
                    step={dt === 'NUMBER' || dt === 'CURRENCY' ? 'any' : undefined}
                    className="form-input"
                    style={{ flex: 1, minWidth: 180, height: 30, fontSize: 12.5, padding: '2px 8px' }}
                    placeholder={cond.op === 'in' ? 'VD: HN, HCM, DN (ngăn bởi dấu phẩy)' : 'Nhập giá trị so sánh...'}
                    value={cond.value || ''}
                    onChange={(e) => handleValueChange(idx, e.target.value)}
                  />
                )}

                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  style={{ color: '#d93025', borderColor: 'var(--border-subtle)', padding: '2px 6px' }}
                  title="Xóa điều kiện này"
                  onClick={() => handleRemoveCondition(idx)}
                >
                  <DeleteOutlineRoundedIcon sx={{ fontSize: 15 }} />
                </button>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
