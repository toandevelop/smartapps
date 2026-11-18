'use client';

import React, { useState, useEffect, useRef } from 'react';
import SearchableSelect, { SearchableSelectOption } from './SearchableSelect';
import BooleanSwitch, { normalizeBoolVal, resolveDefaultBoolVal } from './BooleanSwitch';
import { createCategoryItemQuick, fetchCategoryItems } from '@/lib/category-service';
import { fetchLookupOptions, searchLookupOptions } from '@/lib/lookup-service';
import { cleanColumnDisplayName } from '@/lib/column-utils';
import {
  evaluateSubTableRowFormulas,
  parseSubTableSchema,
  validateSubTableRows,
} from '@/lib/sub-table';
import { LookupOption, SysColumn } from '@/lib/types';

export interface DynamicFieldInputProps {
  column: SysColumn;
  value: any;
  onChange: (val: any) => void;
  onSelectDetail?: (opt: LookupOption) => void;
  disabled?: boolean;
  placeholder?: string;
  size?: 'sm' | 'md';
  formData?: Record<string, any>;
}

export default function DynamicFieldInput({
  column,
  value,
  onChange,
  onSelectDetail,
  disabled = false,
  placeholder,
  size = 'md',
  formData,
}: DynamicFieldInputProps) {
  const [options, setOptions] = useState<SearchableSelectOption[]>([]);
  const [lookupDetails, setLookupDetails] = useState<LookupOption[]>([]);
  const [loadingOpts, setLoadingOpts] = useState(false);
  const [creatingOpt, setCreatingOpt] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState('');
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const lookupFilterVal =
    column.lookupFilterField && column.lookupSourceField && formData
      ? String(formData[column.lookupSourceField] ?? '').trim() || null
      : null;

  useEffect(() => {
    let mounted = true;
    if (column.refTableId) {
      setLoadingOpts(true);
      fetchLookupOptions(
        column.refTableId,
        column.refValueField,
        column.refDisplayField,
        true,
        column.lookupFilterField,
        lookupFilterVal
      )
        .then((items) => {
          if (!mounted) return;
          setLookupDetails(items);
          setOptions(items.map((it) => ({
            value: it.value,
            label: it.label,
            subLabel: it.subLabel,
            searchText: it.searchText,
          })));
        })
        .finally(() => {
          if (mounted) setLoadingOpts(false);
        });
    } else if (column.refCategoryCode) {
      setLookupDetails([]);
      setLoadingOpts(true);
      fetchCategoryItems(column.refCategoryCode)
        .then((items) => {
          if (!mounted) return;
          setOptions(
            items.map((it) => ({
              value: it.itemCode,
              label: `${it.itemCode} - ${it.itemName}`,
              subLabel: it.itemName,
            }))
          );
        })
        .finally(() => {
          if (mounted) setLoadingOpts(false);
        });
    } else if ((column.dataType || '').toUpperCase() === 'USER') {
      setLookupDetails([]);
      setLoadingOpts(true);
      fetch('/api/users', { credentials: 'include' })
        .then((r) => (r.ok ? r.json() : null))
        .then((res) => {
          if (!mounted || !res?.success || !Array.isArray(res.data)) return;
          setOptions(
            res.data.map((u: any) => ({
              value: u.username,
              label: u.fullName ? `${u.fullName} (@${u.username})` : `@${u.username}`,
              subLabel: u.department || u.roleCode || '',
            })),
          );
        })
        .catch(() => {})
        .finally(() => {
          if (mounted) setLoadingOpts(false);
        });
    } else {
      setLookupDetails([]);
      setOptions([]);
    }
    return () => {
      mounted = false;
      if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
    };
  }, [column.dataType, column.refCategoryCode, column.refTableId, column.refValueField, column.refDisplayField, column.lookupFilterField, lookupFilterVal]);

  const handleLookupSearch = (term: string) => {
    if (!column.refTableId) return;
    if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
    const trimmed = term.trim();
    if (trimmed.length < 2) return;
    searchTimerRef.current = setTimeout(async () => {
      const remote = await searchLookupOptions(
        column.refTableId!,
        column.refValueField,
        column.refDisplayField,
        trimmed,
        column.lookupFilterField,
        lookupFilterVal
      );
      if (remote.length === 0) return;
      setLookupDetails((prev) => {
        const map = new Map(prev.map((x) => [x.value, x]));
        remote.forEach((it) => map.set(it.value, it));
        const merged = Array.from(map.values());
        setOptions(merged.map((it) => ({
          value: it.value,
          label: it.label,
          subLabel: it.subLabel,
          searchText: it.searchText,
        })));
        return merged;
      });
    }, 240);
  };

  const cleanName = cleanColumnDisplayName(column.columnName);
  const dt = (column.dataType || 'STRING').toUpperCase();

  if (column.formula) {
    return (
      <div
        style={{
          height: size === 'sm' ? 30 : 38,
          padding: '0 12px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          borderRadius: 8,
          backgroundColor: '#f8fafc',
          border: '1px dashed #cbd5e1',
          fontSize: 12.5,
          color: '#475569',
        }}
      >
        <span>{value !== null && value !== undefined ? String(value) : 'Tự động tính khi lưu'}</span>
        <code style={{ fontSize: 11, color: '#1a73e8' }}>ƒ({column.formula})</code>
      </div>
    );
  }

  if (column.refTableId || dt === 'LOOKUP' || column.refCategoryCode || dt === 'SELECT') {
    const currentStr = value !== null && value !== undefined ? String(value) : '';
    const selectedDetail = column.refTableId
      ? lookupDetails.find((it) => String(it.value) === currentStr)
      : undefined;

    return (
      <div>
        <SearchableSelect
          options={options}
          value={currentStr}
          onChange={(v) => {
            onChange(v);
            if (column.refTableId && onSelectDetail) {
              const matched = lookupDetails.find((it) => String(it.value) === String(v));
              if (matched) onSelectDetail(matched);
            }
          }}
          onSearchChange={column.refTableId ? handleLookupSearch : undefined}
          placeholder={loadingOpts ? 'Đang tải dữ liệu liên kết...' : placeholder || `Chọn ${cleanName}...`}
          searchPlaceholder={column.refTableId ? 'Tìm theo tên, mã, SĐT, CCCD...' : 'Tìm nhanh...'}
          disabled={disabled || loadingOpts}
          size={size}
          isClearable={!column.isRequired}
          creatingOption={creatingOpt}
          onCreateOption={
            column.refCategoryCode && !column.refTableId && column.allowQuickCreate
              ? async (inputText) => {
                  if (!column.refCategoryCode) return;
                  setCreatingOpt(true);
                  try {
                    const created = await createCategoryItemQuick(column.refCategoryCode, inputText);
                    if (created) {
                      const refreshed = await fetchCategoryItems(column.refCategoryCode, true);
                      setOptions(refreshed.map((it) => ({ value: it.itemCode, label: it.itemName })));
                      onChange(created.itemCode);
                    }
                  } finally {
                    setCreatingOpt(false);
                  }
                }
              : undefined
          }
        />
        {size !== 'sm' && selectedDetail?.summaryFields && selectedDetail.summaryFields.length > 0 && (
          <div
            style={{
              marginTop: 6,
              padding: '8px 10px',
              borderRadius: 8,
              backgroundColor: '#f0f6ff',
              border: '1px solid #bfdbfe',
              fontSize: 11.5,
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fit, minmax(145px, 1fr))',
              gap: '4px 10px',
            }}
          >
            {selectedDetail.summaryFields.map((sf) => (
              <div key={sf.code} style={{ minWidth: 0 }}>
                <span style={{ color: '#64748b' }}>{sf.label}: </span>
                <strong style={{ color: '#0f172a', wordBreak: 'break-word' }}>{sf.value}</strong>
              </div>
            ))}
          </div>
        )}
      </div>
    );
  }

  if (dt === 'BOOLEAN') {
    const parsed = normalizeBoolVal(value);
    const checked = parsed !== null ? parsed : resolveDefaultBoolVal(column.defaultValue);
    return <BooleanSwitch checked={checked} onChange={(b) => onChange(b)} disabled={disabled} size={size} />;
  }

  if (dt === 'DATE') {
    const str = value ? String(value).substring(0, 10) : '';
    return (
      <input
        type="date"
        className="form-input"
        value={str}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
      />
    );
  }

  if (dt === 'NUMBER' || dt === 'CURRENCY') {
    return (
      <input
        type="number"
        className="form-input"
        style={{ textAlign: value !== null && value !== undefined && value !== '' ? 'right' : 'left' }}
        value={value !== null && value !== undefined ? value : ''}
        onChange={(e) => onChange(e.target.value === '' ? null : Number(e.target.value))}
        placeholder={placeholder || `Nhập ${cleanName.toLowerCase()}...`}
        disabled={disabled}
      />
    );
  }

  if (dt === 'TEXTAREA') {
    return (
      <textarea
        className="form-textarea"
        rows={3}
        value={value !== null && value !== undefined ? String(value) : ''}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder || `Nhập ${cleanName.toLowerCase()}...`}
        disabled={disabled}
      />
    );
  }

  if (dt === 'MULTI_FILE') {
    const fileList: Array<{ id: string; fileName: string; url: string; sizeBytes?: number }> = Array.isArray(value) ? value : [];

    const handleMultiUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
      const files = Array.from(e.target.files || []);
      if (files.length === 0) return;
      setUploadError('');
      setUploading(true);
      try {
        const added: Array<{ id: string; fileName: string; url: string; sizeBytes?: number }> = [];
        for (const file of files) {
          const fd = new FormData();
          fd.append('file', file);
          if (column.tableId) fd.append('tableId', column.tableId);
          const res = await fetch('/api/attachments', { method: 'POST', credentials: 'include', body: fd });
          const json = await res.json();
          if (!res.ok || !json.success) {
            setUploadError(json.message || `Lỗi tải tệp ${file.name}`);
            break;
          }
          const att = json.data;
          added.push({
            id: att.id,
            fileName: att.fileName || file.name,
            url: `/api/attachments/${att.id}?name=${encodeURIComponent(att.fileName || file.name)}`,
            sizeBytes: att.sizeBytes,
          });
        }
        if (added.length > 0) onChange([...fileList, ...added]);
      } catch {
        setUploadError('Không thể kết nối máy chủ upload');
      } finally {
        setUploading(false);
        e.target.value = '';
      }
    };

    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <label className="btn btn-outline btn-sm" style={{ alignSelf: 'flex-start', cursor: disabled || uploading ? 'not-allowed' : 'pointer', margin: 0 }}>
          {uploading ? 'Đang tải lên...' : 'Thêm nhiều tệp...'}
          <input type="file" multiple disabled={disabled || uploading} onChange={handleMultiUpload} style={{ display: 'none' }} />
        </label>
        {fileList.length > 0 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {fileList.map((f, idx) => (
              <div key={`${f.id}_${idx}`} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, padding: '6px 10px', borderRadius: 8, backgroundColor: '#f8fafc', border: '1px solid #e2e8f0', fontSize: 12.5 }}>
                <a href={f.url} target="_blank" rel="noreferrer" style={{ color: '#1a73e8', fontWeight: 600, textDecoration: 'underline', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {f.fileName}
                </a>
                <div style={{ display: 'flex', gap: 6, flexShrink: 0 }}>
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    onClick={async () => {
                      const res = await fetch(`/api/attachments/${f.id}/share`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        credentials: 'include',
                        body: JSON.stringify({ expiresInHours: 168, allowDownload: true }),
                      }).then((r) => r.json());
                      if (res.success && res.data?.shareUrl) {
                        const fullUrl = `${window.location.origin}${res.data.shareUrl}`;
                        await navigator.clipboard?.writeText(fullUrl).catch(() => {});
                        setUploadError(`Link chia sẻ (7 ngày): ${fullUrl}`);
                      }
                    }}
                  >
                    Chia sẻ
                  </button>
                  {!disabled && (
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      style={{ color: '#d93025' }}
                      onClick={() => onChange(fileList.filter((_, i) => i !== idx))}
                    >
                      ×
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
        {uploadError && <span style={{ fontSize: 12, color: uploadError.startsWith('Link chia sẻ') ? '#137333' : '#d93025' }}>{uploadError}</span>}
      </div>
    );
  }

  if (dt === 'MULTI_SELECT') {
    const selectedArr: string[] = Array.isArray(value)
      ? value.map(String)
      : (typeof value === 'string' && value ? value.split(',').map((s) => s.trim()).filter(Boolean) : []);

    if (options.length > 0) {
      return (
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
          {options.map((opt) => {
            const active = selectedArr.includes(opt.value);
            return (
              <button
                key={opt.value}
                type="button"
                disabled={disabled}
                className={`btn btn-xs ${active ? 'btn-primary' : 'btn-outline'}`}
                style={{ borderRadius: 999 }}
                onClick={() => {
                  const next = active ? selectedArr.filter((v) => v !== opt.value) : [...selectedArr, opt.value];
                  onChange(next);
                }}
              >
                {opt.subLabel || opt.label}
              </button>
            );
          })}
        </div>
      );
    }

    return (
      <input
        className="form-input"
        disabled={disabled}
        placeholder="Nhập các giá trị, cách nhau bởi dấu phẩy..."
        value={selectedArr.join(', ')}
        onChange={(e) => onChange(e.target.value.split(',').map((s) => s.trim()).filter(Boolean))}
      />
    );
  }

  if (dt === 'USER') {
    return (
      <SearchableSelect
        value={value === null || value === undefined ? '' : String(value)}
        options={options}
        disabled={disabled}
        placeholder={loadingOpts ? 'Đang tải danh sách nhân sự...' : (placeholder || '-- Chọn nhân sự --')}
        size={size}
        onChange={(val) => onChange(val)}
      />
    );
  }

  if (dt === 'SIGNATURE') {
    const sigUrl = typeof value === 'string' && value.startsWith('data:image/') ? value : '';
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        {sigUrl ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <img src={sigUrl} alt="Chữ ký" style={{ height: 72, borderRadius: 8, border: '1px solid #cbd5e1', backgroundColor: '#fff' }} />
            {!disabled && (
              <button type="button" className="btn btn-outline btn-xs" style={{ color: '#d93025' }} onClick={() => onChange('')}>
                Ký lại
              </button>
            )}
          </div>
        ) : !disabled ? (
          <SignatureCanvasPad onSave={(dataUrl) => onChange(dataUrl)} />
        ) : (
          <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>Chưa có chữ ký</span>
        )}
      </div>
    );
  }

  if (dt === 'FILE' || dt === 'IMAGE') {
    const urlStr = typeof value === 'string' ? value : value?.url || '';
    let fileLabel = '';
    if (urlStr) {
      const match = urlStr.match(/[?&]name=([^&]+)/);
      fileLabel = match ? decodeURIComponent(match[1]) : urlStr.split('/').pop()?.split('?')[0] || 'Tệp đính kèm';
    }

    const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
      const file = e.target.files?.[0];
      if (!file) return;
      setUploadError('');
      setUploading(true);
      try {
        const fd = new FormData();
        fd.append('file', file);
        if (column.tableId) fd.append('tableId', column.tableId);
        const res = await fetch('/api/attachments', {
          method: 'POST',
          credentials: 'include',
          body: fd,
        });
        const json = await res.json();
        if (!res.ok || !json.success) {
          setUploadError(json.message || 'Lỗi tải tệp lên');
        } else {
          const att = json.data;
          onChange(`/api/attachments/${att.id}?name=${encodeURIComponent(att.fileName || file.name)}`);
        }
      } catch {
        setUploadError('Không thể kết nối máy chủ upload');
      } finally {
        setUploading(false);
        e.target.value = '';
      }
    };

    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <label
            className="btn btn-outline btn-sm"
            style={{ cursor: disabled || uploading ? 'not-allowed' : 'pointer', margin: 0 }}
          >
            {uploading ? 'Đang tải lên...' : dt === 'IMAGE' ? 'Chọn ảnh...' : 'Tải tệp lên...'}
            <input
              type="file"
              accept={dt === 'IMAGE' ? 'image/*' : undefined}
              disabled={disabled || uploading}
              onChange={handleFileUpload}
              style={{ display: 'none' }}
            />
          </label>

          {urlStr && (
            <>
              <a
                href={urlStr}
                target="_blank"
                rel="noreferrer"
                style={{ fontSize: 12.5, fontWeight: 600, color: '#1a73e8', textDecoration: 'underline' }}
              >
                {fileLabel}
              </a>
              {!disabled && (
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  onClick={() => onChange('')}
                  style={{ color: '#d93025' }}
                >
                  Xóa
                </button>
              )}
            </>
          )}
        </div>

        {dt === 'IMAGE' && urlStr && (
          <img
            src={urlStr}
            alt={fileLabel || 'Preview'}
            style={{
              maxWidth: 180,
              maxHeight: 110,
              borderRadius: 10,
              border: '1px solid var(--border-subtle)',
              objectFit: 'cover',
            }}
          />
        )}

        {uploadError && (
          <span style={{ fontSize: 11.5, color: '#d93025' }}>{uploadError}</span>
        )}
      </div>
    );
  }

  if (dt === 'AUTO_NUMBER') {
    return (
      <div
        style={{
          height: size === 'sm' ? 30 : 38,
          padding: '0 12px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          borderRadius: 8,
          backgroundColor: '#f8fafc',
          border: '1px dashed #cbd5e1',
          fontSize: 12.5,
          color: '#334155',
        }}
      >
        <code>{value ? String(value) : `Tự sinh (${column.defaultValue || 'REC-{YYYY}-'}0001)`}</code>
        <span className="badge badge-info">AUTO</span>
      </div>
    );
  }

  if (dt === 'SUB_TABLE') {
    const subCols = parseSubTableSchema(column.defaultValue);
    const rows: Array<Record<string, any>> = Array.isArray(value)
      ? value
      : typeof value === 'string' && value.trim().startsWith('[')
      ? (() => {
          try {
            return JSON.parse(value);
          } catch {
            return [];
          }
        })()
      : [];

    const cellErrors = validateSubTableRows(column.columnCode, subCols, rows);
    const updateRows = (next: Array<Record<string, any>>) => onChange(next);

    return (
      <div
        style={{
          border: '1px solid var(--border-subtle)',
          borderRadius: 12,
          overflow: 'hidden',
          backgroundColor: 'var(--bg-surface)',
        }}
      >
        <div
          style={{
            padding: '8px 12px',
            backgroundColor: 'var(--bg-primary)',
            borderBottom: '1px solid var(--border-light)',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
          }}
        >
          <span style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-secondary)' }}>
            Bảng chi tiết ({rows.length} dòng)
          </span>
          {!disabled && (
            <button
              type="button"
              className="btn btn-outline btn-xs"
              onClick={() => {
                const emptyRow: Record<string, any> = {
                  _rowId: `row_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 6)}`,
                };
                subCols.forEach((sc) => {
                  emptyRow[sc.code] = sc.dataType === 'BOOLEAN' ? false : sc.defaultValue ?? '';
                });
                updateRows([...rows, evaluateSubTableRowFormulas(subCols, emptyRow)]);
              }}
            >
              + Thêm dòng
            </button>
          )}
        </div>
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12.5 }}>
            <thead>
              <tr style={{ backgroundColor: '#fbfcfd', borderBottom: '1px solid var(--border-light)' }}>
                <th style={{ padding: '6px 8px', width: 34, textAlign: 'center', color: 'var(--text-muted)' }}>#</th>
                {subCols.map((sc) => (
                  <th key={sc.code} style={{ padding: '6px 8px', textAlign: 'left', fontWeight: 600 }}>
                    {sc.name}
                    {sc.required && <span style={{ color: '#d93025', marginLeft: 3 }}>*</span>}
                  </th>
                ))}
                {!disabled && <th style={{ width: 44 }} />}
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr>
                  <td colSpan={subCols.length + 2} style={{ padding: 12, textAlign: 'center', color: 'var(--text-muted)' }}>
                    Chưa có dòng chi tiết. Nhấn &ldquo;+ Thêm dòng&rdquo; để nhập.
                  </td>
                </tr>
              ) : (
                rows.map((r, rIdx) => (
                  <tr key={r?._rowId || rIdx} style={{ borderBottom: '1px solid var(--border-light)' }}>
                    <td style={{ padding: '4px 8px', textAlign: 'center', color: 'var(--text-muted)' }}>{rIdx + 1}</td>
                    {subCols.map((sc) => {
                      const errKey = `${column.columnCode}[${rIdx}].${sc.code}`;
                      const cellErr = cellErrors[errKey];
                      const isFormula = Boolean(sc.formula?.trim());
                      const cellDisabled = disabled || isFormula;
                      const cellVal = r?.[sc.code];

                      const handleCellChange = (nextVal: any) => {
                        const copy = [...rows];
                        const updatedRow = evaluateSubTableRowFormulas(subCols, {
                          ...(copy[rIdx] || {}),
                          [sc.code]: nextVal,
                        });
                        copy[rIdx] = updatedRow;
                        updateRows(copy);
                      };

                      return (
                        <td key={sc.code} style={{ padding: '4px 6px', verticalAlign: 'top' }}>
                          {sc.dataType === 'BOOLEAN' ? (
                            <input
                              type="checkbox"
                              checked={Boolean(cellVal)}
                              disabled={cellDisabled}
                              onChange={(e) => handleCellChange(e.target.checked)}
                            />
                          ) : (
                            <input
                              type={
                                sc.dataType === 'NUMBER' || sc.dataType === 'CURRENCY'
                                  ? 'number'
                                  : sc.dataType === 'DATE'
                                  ? 'date'
                                  : sc.dataType === 'EMAIL'
                                  ? 'email'
                                  : 'text'
                              }
                              step={sc.dataType === 'NUMBER' || sc.dataType === 'CURRENCY' ? 'any' : undefined}
                              className="form-input"
                              style={{
                                height: 30,
                                fontSize: 12.5,
                                padding: '2px 8px',
                                backgroundColor: isFormula ? 'var(--bg-primary)' : undefined,
                                borderColor: cellErr ? '#d93025' : undefined,
                              }}
                              disabled={cellDisabled}
                              value={cellVal ?? ''}
                              onChange={(e) => handleCellChange(e.target.value)}
                            />
                          )}
                          {cellErr && (
                            <div style={{ fontSize: 11, color: '#d93025', marginTop: 2 }}>{cellErr}</div>
                          )}
                        </td>
                      );
                    })}
                    {!disabled && (
                      <td style={{ padding: '4px 6px', textAlign: 'center' }}>
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          style={{ color: '#d93025' }}
                          onClick={() => updateRows(rows.filter((_, idx) => idx !== rIdx))}
                        >
                          ×
                        </button>
                      </td>
                    )}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    );
  }

  const inputType = dt === 'EMAIL' ? 'email' : dt === 'TEL' ? 'tel' : 'text';
  return (
    <input
      type={inputType}
      className="form-input"
      value={value !== null && value !== undefined ? String(value) : ''}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder || `Nhập ${cleanName.toLowerCase()}...`}
      disabled={disabled}
    />
  );
}

function SignatureCanvasPad({ onSave }: { onSave: (dataUrl: string) => void }) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const drawingRef = useRef(false);

  const getPos = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const rect = e.currentTarget.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  };

  const getTouchPos = (e: React.TouchEvent<HTMLCanvasElement>) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const t = e.touches[0] || e.changedTouches[0];
    return { x: t.clientX - rect.left, y: t.clientY - rect.top };
  };

  const beginStroke = (x: number, y: number) => {
    const ctx = canvasRef.current?.getContext('2d');
    if (!ctx) return;
    drawingRef.current = true;
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.lineWidth = 2;
    ctx.lineCap = 'round';
    ctx.strokeStyle = '#0f172a';
  };

  const moveStroke = (x: number, y: number) => {
    if (!drawingRef.current) return;
    const ctx = canvasRef.current?.getContext('2d');
    if (!ctx) return;
    ctx.lineTo(x, y);
    ctx.stroke();
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6, alignItems: 'flex-start' }}>
      <canvas
        ref={canvasRef}
        width={340}
        height={110}
        style={{ border: '1px dashed #94a3b8', borderRadius: 8, backgroundColor: '#fff', cursor: 'crosshair', touchAction: 'none', maxWidth: '100%' }}
        onMouseDown={(e) => {
          const p = getPos(e);
          beginStroke(p.x, p.y);
        }}
        onMouseMove={(e) => {
          const p = getPos(e);
          moveStroke(p.x, p.y);
        }}
        onMouseUp={() => {
          drawingRef.current = false;
        }}
        onMouseLeave={() => {
          drawingRef.current = false;
        }}
        onTouchStart={(e) => {
          e.preventDefault();
          const p = getTouchPos(e);
          beginStroke(p.x, p.y);
        }}
        onTouchMove={(e) => {
          e.preventDefault();
          const p = getTouchPos(e);
          moveStroke(p.x, p.y);
        }}
        onTouchEnd={() => {
          drawingRef.current = false;
        }}
      />
      <div style={{ display: 'flex', gap: 6 }}>
        <button
          type="button"
          className="btn btn-primary btn-xs"
          onClick={() => {
            if (canvasRef.current) onSave(canvasRef.current.toDataURL('image/png'));
          }}
        >
          Xác nhận chữ ký
        </button>
        <button
          type="button"
          className="btn btn-outline btn-xs"
          onClick={() => {
            const c = canvasRef.current;
            c?.getContext('2d')?.clearRect(0, 0, c.width, c.height);
          }}
        >
          Xóa nét
        </button>
      </div>
    </div>
  );
}
