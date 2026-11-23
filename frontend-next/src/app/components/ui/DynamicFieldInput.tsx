'use client';

import React, { useState, useEffect, useRef } from 'react';
import QrCodeScannerRoundedIcon from '@mui/icons-material/QrCodeScannerRounded';
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded';
import StarRoundedIcon from '@mui/icons-material/StarRounded';
import StarBorderRoundedIcon from '@mui/icons-material/StarBorderRounded';
import SearchableSelect, { SearchableSelectOption } from './SearchableSelect';
import QrScannerModal from './QrScannerModal';
import BooleanSwitch, { normalizeBoolVal, resolveDefaultBoolVal } from './BooleanSwitch';
import { createCategoryItemQuick, fetchCategoryItems } from '@/lib/category-service';
import { fetchLookupOptions, searchLookupOptions } from '@/lib/lookup-service';
import { cleanColumnDisplayName } from '@/lib/column-utils';
import {
  MONDAY_COLOR_SWATCHES,
  MONDAY_PRIORITY_PRESETS,
  MONDAY_STATUS_PRESETS,
  clampProgressValue,
  clampRatingValue,
  getMondayProgressColor,
  getTagColor,
  normalizeSafeLinkUrl,
  parseTagsValue,
  resolveMondayPriorityStyle,
  resolveMondayStatusStyle,
} from '@/lib/monday-fields';
import {
  evaluateSubTableRowFormulas,
  parseSubTableSchema,
  validateSubTableRows,
} from '@/lib/sub-table';
import { LookupOption, SysColumn } from '@/lib/types';

export interface DynamicFieldInputProps {
  column: SysColumn;
  value: unknown;
  onChange: (val: unknown) => void;
  onSelectDetail?: (opt: LookupOption) => void;
  disabled?: boolean;
  required?: boolean;
  inputId?: string;
  placeholder?: string;
  size?: 'sm' | 'md';
  formData?: Record<string, unknown>;
  publicFormTableId?: string;
  publicCategories?: Array<{ id: string; itemCode: string; itemName: string }>;
}

export default function DynamicFieldInput({
  column,
  value,
  onChange,
  onSelectDetail,
  disabled = false,
  required = false,
  inputId,
  placeholder,
  size = 'md',
  formData,
  publicFormTableId,
  publicCategories,
}: DynamicFieldInputProps) {
  const [options, setOptions] = useState<SearchableSelectOption[]>([]);
  const [lookupDetails, setLookupDetails] = useState<LookupOption[]>([]);
  const [loadingOpts, setLoadingOpts] = useState(false);
  const [creatingOpt, setCreatingOpt] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState('');
  const [localImagePreview, setLocalImagePreview] = useState<string>('');
  const [qrOpen, setQrOpen] = useState(false);
  const [tagDraft, setTagDraft] = useState('');
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const lookupFilterVal =
    column.lookupFilterField && column.lookupSourceField && formData
      ? String(formData[column.lookupSourceField] ?? '').trim() || null
      : null;

  useEffect(() => {
    let mounted = true;
    void Promise.resolve().then(() => {
      if (!mounted) return;
      if (column.refTableId) {
        setLoadingOpts(true);
        const loadReq: Promise<LookupOption[]> = publicFormTableId
          ? fetch(
              `/api/public/forms/${encodeURIComponent(publicFormTableId)}/lookup-options?columnCode=${encodeURIComponent(column.columnCode)}${lookupFilterVal ? `&filterValue=${encodeURIComponent(lookupFilterVal)}` : ''}`,
            )
              .then((r) => r.json())
              .then((res) => (res.success && Array.isArray(res.data) ? (res.data as LookupOption[]) : []))
              .catch(() => [])
          : fetchLookupOptions(
              column.refTableId,
              column.refValueField,
              column.refDisplayField,
              true,
              column.lookupFilterField,
              lookupFilterVal,
            );
        loadReq
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
        if (publicCategories && publicCategories.length > 0) {
          setOptions(
            publicCategories.map((it) => ({
              value: it.itemCode,
              label: `${it.itemCode} - ${it.itemName}`,
              subLabel: it.itemName,
            })),
          );
        } else if (!publicFormTableId) {
          setLoadingOpts(true);
          fetchCategoryItems(column.refCategoryCode)
            .then((items) => {
              if (!mounted) return;
              setOptions(
                items.map((it) => ({
                  value: it.itemCode,
                  label: `${it.itemCode} - ${it.itemName}`,
                  subLabel: it.itemName,
                })),
              );
            })
            .finally(() => {
              if (mounted) setLoadingOpts(false);
            });
        } else {
          setOptions([]);
        }
      } else if ((column.dataType || '').toUpperCase() === 'USER' && !publicFormTableId) {
        setLookupDetails([]);
        setLoadingOpts(true);
        fetch('/api/users', { credentials: 'include' })
          .then((r) => (r.ok ? r.json() : null))
          .then((res) => {
            if (!mounted || !res?.success || !Array.isArray(res.data)) return;
            const userRows = res.data as Array<{ username: string; fullName?: string | null; department?: string | null; roleCode?: string | null }>;
            setOptions(
              userRows.map((u) => ({
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
    });
    return () => {
      mounted = false;
      if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
    };
  }, [column.columnCode, column.dataType, column.refCategoryCode, column.refTableId, column.refValueField, column.refDisplayField, column.lookupFilterField, lookupFilterVal, publicFormTableId, publicCategories]);

  const handleLookupSearch = (term: string) => {
    if (!column.refTableId) return;
    if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
    const trimmed = term.trim();
    if (trimmed.length < 2) return;
    searchTimerRef.current = setTimeout(async () => {
      const remote: LookupOption[] = publicFormTableId
        ? await fetch(
            `/api/public/forms/${encodeURIComponent(publicFormTableId)}/lookup-options?columnCode=${encodeURIComponent(column.columnCode)}&q=${encodeURIComponent(trimmed)}${lookupFilterVal ? `&filterValue=${encodeURIComponent(lookupFilterVal)}` : ''}`,
          )
            .then((r) => r.json())
            .then((res) => (res.success && Array.isArray(res.data) ? (res.data as LookupOption[]) : []))
            .catch(() => [])
        : await searchLookupOptions(
            column.refTableId!,
            column.refValueField,
            column.refDisplayField,
            trimmed,
            column.lookupFilterField,
            lookupFilterVal,
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
          backgroundColor: 'var(--bg-secondary)',
          border: '1px dashed var(--border-subtle)',
          fontSize: 12.5,
          color: 'var(--text-secondary)',
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
            !publicFormTableId && column.refCategoryCode && !column.refTableId && column.allowQuickCreate
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
              backgroundColor: 'var(--info-bg)',
              border: '1px solid var(--info-border)',
              fontSize: 11.5,
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fit, minmax(145px, 1fr))',
              gap: '4px 10px',
            }}
          >
            {selectedDetail.summaryFields.map((sf) => (
              <div key={sf.code} style={{ minWidth: 0 }}>
                <span style={{ color: '#64748b' }}>{sf.label}: </span>
                <strong style={{ color: 'var(--text-primary)', wordBreak: 'break-word' }}>{sf.value}</strong>
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
    return <BooleanSwitch id={inputId} checked={checked} onChange={(b) => onChange(b)} disabled={disabled} size={size} />;
  }

  if (dt === 'STATUS' || dt === 'PRIORITY') {
    const currentStr = value !== null && value !== undefined ? String(value) : '';
    const presets =
      options.length > 0
        ? options.map((o) =>
            dt === 'PRIORITY'
              ? resolveMondayPriorityStyle(o.value, o.subLabel || o.label)
              : resolveMondayStatusStyle(o.value, undefined, o.subLabel || o.label)
          )
        : dt === 'PRIORITY'
        ? MONDAY_PRIORITY_PRESETS
        : MONDAY_STATUS_PRESETS;

    return (
      <div
        role="radiogroup"
        aria-label={cleanName}
        style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}
      >
        {presets.map((p) => {
          const active = currentStr.toUpperCase() === p.code.toUpperCase();
          return (
            <button
              key={p.code}
              type="button"
              role="radio"
              aria-checked={active}
              disabled={disabled}
              onClick={() => onChange(active && !required ? '' : p.code)}
              className="monday-status-pill"
              style={{
                backgroundColor: p.bg,
                color: p.color,
                opacity: currentStr && !active ? 0.55 : 1,
                transform: active ? 'scale(1.03)' : 'none',
                boxShadow: active ? `0 0 0 2px var(--bg-surface), 0 0 0 4px ${p.bg}` : 'none',
                minWidth: size === 'sm' ? 88 : 108,
                height: size === 'sm' ? 28 : 34,
                cursor: disabled ? 'not-allowed' : 'pointer',
              }}
            >
              {p.label}
            </button>
          );
        })}
      </div>
    );
  }

  if (dt === 'RATING') {
    const currentRating = clampRatingValue(value);
    return (
      <div
        role="group"
        aria-label={`Đánh giá sao cho ${cleanName}`}
        style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}
      >
        {[1, 2, 3, 4, 5].map((star) => {
          const filled = star <= currentRating;
          return (
            <button
              key={star}
              type="button"
              disabled={disabled}
              aria-label={`${star} sao`}
              aria-pressed={currentRating === star}
              onClick={() => onChange(currentRating === star && !required ? 0 : star)}
              style={{
                border: 'none',
                background: 'transparent',
                padding: 2,
                cursor: disabled ? 'not-allowed' : 'pointer',
                color: filled ? '#ffcb00' : '#cbd5e1',
                display: 'inline-flex',
                alignItems: 'center',
                transition: 'transform 0.12s ease',
              }}
            >
              {filled ? (
                <StarRoundedIcon sx={{ fontSize: size === 'sm' ? 22 : 26, filter: 'drop-shadow(0 1px 1px rgba(0,0,0,0.12))' }} />
              ) : (
                <StarBorderRoundedIcon sx={{ fontSize: size === 'sm' ? 22 : 26 }} />
              )}
            </button>
          );
        })}
        <span style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-secondary)', marginLeft: 4 }}>
          {currentRating}/5
        </span>
      </div>
    );
  }

  if (dt === 'PROGRESS') {
    const pct = clampProgressValue(value);
    const barColor = getMondayProgressColor(pct);
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6, width: '100%' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <input
            id={inputId}
            type="range"
            min={0}
            max={100}
            step={5}
            value={pct}
            disabled={disabled}
            aria-label={cleanName}
            onChange={(e) => onChange(Number(e.target.value))}
            style={{
              flex: 1,
              accentColor: barColor,
              cursor: disabled ? 'not-allowed' : 'pointer',
            }}
          />
          <span
            style={{
              minWidth: 52,
              textAlign: 'center',
              padding: '3px 8px',
              borderRadius: 999,
              fontSize: 12,
              fontWeight: 700,
              backgroundColor: barColor,
              color: '#ffffff',
              fontVariantNumeric: 'tabular-nums',
            }}
          >
            {pct}%
          </span>
        </div>
        {size !== 'sm' && !disabled && (
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {[0, 25, 50, 75, 100].map((presetPct) => (
              <button
                key={presetPct}
                type="button"
                className={`btn btn-xs ${pct === presetPct ? 'btn-primary' : 'btn-outline'}`}
                onClick={() => onChange(presetPct)}
                style={{ fontSize: 11, padding: '1px 8px', height: 22 }}
              >
                {presetPct}%
              </button>
            ))}
          </div>
        )}
      </div>
    );
  }

  if (dt === 'TAGS') {
    const tags = parseTagsValue(value);
    const commitTagDraft = (raw: string) => {
      const nextTags = parseTagsValue([...tags, ...raw.split(/[,;]+/)]);
      onChange(nextTags);
      setTagDraft('');
    };
    return (
      <div
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          gap: 6,
          padding: '6px 10px',
          borderRadius: 8,
          border: '1px solid var(--border-subtle)',
          backgroundColor: 'var(--bg-surface)',
          minHeight: size === 'sm' ? 32 : 38,
        }}
      >
        {tags.map((t) => {
          const style = getTagColor(t);
          return (
            <span
              key={t}
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 4,
                padding: '2px 8px',
                borderRadius: 999,
                fontSize: 12,
                fontWeight: 600,
                backgroundColor: style.bg,
                color: style.color,
              }}
            >
              #{t}
              {!disabled && (
                <button
                  type="button"
                  aria-label={`Xóa thẻ ${t}`}
                  onClick={() => onChange(tags.filter((x) => x !== t))}
                  style={{
                    border: 'none',
                    background: 'transparent',
                    color: style.color,
                    cursor: 'pointer',
                    fontWeight: 700,
                    padding: '0 2px',
                    lineHeight: 1,
                  }}
                >
                  ×
                </button>
              )}
            </span>
          );
        })}
        {!disabled && (
          <input
            id={inputId}
            type="text"
            value={tagDraft}
            placeholder={tags.length === 0 ? placeholder || 'Nhập #tag rồi nhấn Enter...' : '+ Thêm #tag...'}
            onChange={(e) => setTagDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.nativeEvent.isComposing || e.keyCode === 229) return;
              if ((e.key === 'Enter' || e.key === ',') && tagDraft.trim()) {
                e.preventDefault();
                commitTagDraft(tagDraft);
              } else if (e.key === 'Backspace' && !tagDraft && tags.length > 0) {
                onChange(tags.slice(0, -1));
              }
            }}
            onBlur={() => {
              if (tagDraft.trim()) commitTagDraft(tagDraft);
            }}
            style={{
              border: 'none',
              outline: 'none',
              background: 'transparent',
              color: 'var(--text-primary)',
              fontSize: 12.5,
              flex: '1 1 120px',
              minWidth: 90,
            }}
          />
        )}
      </div>
    );
  }

  if (dt === 'COLOR') {
    const curColor =
      typeof value === 'string' && /^#[0-9a-fA-F]{6}$/.test(value.trim())
        ? value.trim()
        : MONDAY_COLOR_SWATCHES[0];
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
          {MONDAY_COLOR_SWATCHES.map((sw) => {
            const selected = curColor.toLowerCase() === sw.toLowerCase();
            return (
              <button
                key={sw}
                type="button"
                disabled={disabled}
                aria-label={`Chọn màu ${sw}`}
                onClick={() => onChange(sw)}
                style={{
                  width: 24,
                  height: 24,
                  borderRadius: 6,
                  backgroundColor: sw,
                  border: selected ? '2px solid var(--text-primary)' : '2px solid transparent',
                  boxShadow: selected ? `0 0 0 2px ${sw}` : 'none',
                  cursor: disabled ? 'not-allowed' : 'pointer',
                }}
              />
            );
          })}
          <input
            id={inputId}
            type="color"
            value={curColor}
            disabled={disabled}
            aria-label={`Bảng màu tùy chỉnh ${cleanName}`}
            onChange={(e) => onChange(e.target.value)}
            style={{ width: 28, height: 26, padding: 0, border: 'none', background: 'transparent', cursor: 'pointer' }}
          />
          <code style={{ fontSize: 12 }}>{curColor.toUpperCase()}</code>
        </div>
      </div>
    );
  }

  if (dt === 'LINK') {
    const strVal = value !== null && value !== undefined ? String(value) : '';
    const safeLink = normalizeSafeLinkUrl(strVal);
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, width: '100%' }}>
        <input
          id={inputId}
          type="url"
          className="form-input"
          style={{ flex: 1, minWidth: 0 }}
          value={strVal}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder || 'https://...'}
          autoCorrect="off"
          autoCapitalize="none"
          spellCheck={false}
          disabled={disabled}
          required={required}
        />
        {safeLink && (
          <a
            href={safeLink.href}
            target="_blank"
            rel="noreferrer"
            className="btn btn-outline btn-xs"
            title={`Mở liên kết: ${safeLink.href}`}
            style={{ height: size === 'sm' ? 30 : 36, flexShrink: 0, textDecoration: 'none' }}
          >
            <OpenInNewRoundedIcon sx={{ fontSize: 15 }} />
          </a>
        )}
      </div>
    );
  }

  if (dt === 'DATETIME') {
    const str = value ? String(value).replace(' ', 'T').substring(0, 16) : '';
    return (
      <input
        id={inputId}
        type="datetime-local"
        className="form-input"
        value={str}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
        required={required}
      />
    );
  }

  if (dt === 'TIME') {
    const str = value ? String(value).substring(0, 5) : '';
    return (
      <input
        id={inputId}
        type="time"
        className="form-input"
        value={str}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
        required={required}
      />
    );
  }

  if (dt === 'DATE') {
    const str = value ? String(value).substring(0, 10) : '';
    return (
      <input
        id={inputId}
        type="date"
        className="form-input"
        value={str}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
        required={required}
      />
    );
  }

  if (dt === 'NUMBER' || dt === 'CURRENCY') {
    return (
      <input
        id={inputId}
        type="number"
        className="form-input"
        style={{ textAlign: value !== null && value !== undefined && value !== '' ? 'right' : 'left' }}
        value={typeof value === 'number' || typeof value === 'string' ? value : ''}
        onChange={(e) => onChange(e.target.value === '' ? null : Number(e.target.value))}
        placeholder={placeholder || `Nhập ${cleanName.toLowerCase()}...`}
        disabled={disabled}
        required={required}
      />
    );
  }

  if (dt === 'TEXTAREA') {
    return (
      <textarea
        id={inputId}
        className="form-textarea"
        rows={3}
        value={value !== null && value !== undefined ? String(value) : ''}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder || `Nhập ${cleanName.toLowerCase()}...`}
        disabled={disabled}
        required={required}
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
          const endpoint = publicFormTableId
            ? `/api/public/forms/${encodeURIComponent(publicFormTableId)}/upload`
            : '/api/attachments';
          const res = await fetch(endpoint, { method: 'POST', credentials: 'include', body: fd });
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
              <div key={`${f.id}_${idx}`} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, padding: '6px 10px', borderRadius: 8, backgroundColor: 'var(--bg-secondary)', border: '1px solid var(--border-subtle)', fontSize: 12.5 }}>
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
    const urlStr = typeof value === 'string'
      ? value
      : typeof value === 'object' && value !== null && 'url' in value
      ? String((value as { url?: unknown }).url ?? '')
      : '';
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
        const endpoint = publicFormTableId
          ? `/api/public/forms/${encodeURIComponent(publicFormTableId)}/upload`
          : '/api/attachments';
        const res = await fetch(endpoint, {
          method: 'POST',
          credentials: 'include',
          body: fd,
        });
        const json = await res.json();
        if (!res.ok || !json.success) {
          setUploadError(json.message || 'Lỗi tải tệp lên');
        } else {
          const att = json.data;
          if (publicFormTableId && dt === 'IMAGE') {
            setLocalImagePreview(URL.createObjectURL(file));
          }
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

        {dt === 'IMAGE' && (urlStr || localImagePreview) && (
          <img
            src={localImagePreview || urlStr}
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
          backgroundColor: 'var(--bg-secondary)',
          border: '1px dashed var(--border-subtle)',
          fontSize: 12.5,
          color: 'var(--text-secondary)',
        }}
      >
        <code>{value ? String(value) : `Tự sinh (${column.defaultValue || 'REC-{YYYY}-'}0001)`}</code>
        <span className="badge badge-info">AUTO</span>
      </div>
    );
  }

  if (dt === 'SUB_TABLE') {
    const subCols = parseSubTableSchema(column.defaultValue);
    const rows: Array<Record<string, unknown>> = Array.isArray(value)
      ? (value as Array<Record<string, unknown>>)
      : typeof value === 'string' && value.trim().startsWith('[')
      ? (() => {
          try {
            return JSON.parse(value) as Array<Record<string, unknown>>;
          } catch {
            return [];
          }
        })()
      : [];

    const cellErrors = validateSubTableRows(column.columnCode, subCols, rows);
    const updateRows = (next: Array<Record<string, unknown>>) => onChange(next);

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
                const emptyRow: Record<string, unknown> = {
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
              <tr style={{ backgroundColor: 'var(--bg-secondary)', borderBottom: '1px solid var(--border-light)' }}>
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
                  <tr key={String(r?._rowId || rIdx)} style={{ borderBottom: '1px solid var(--border-light)' }}>
                    <td style={{ padding: '4px 8px', textAlign: 'center', color: 'var(--text-muted)' }}>{rIdx + 1}</td>
                    {subCols.map((sc) => {
                      const errKey = `${column.columnCode}[${rIdx}].${sc.code}`;
                      const cellErr = cellErrors[errKey];
                      const isFormula = Boolean(sc.formula?.trim());
                      const cellDisabled = disabled || isFormula;
                      const cellVal = r?.[sc.code];

                      const handleCellChange = (nextVal: unknown) => {
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
                              value={typeof cellVal === 'number' || typeof cellVal === 'string' ? cellVal : ''}
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
  const isIdentifierOrEmail = Boolean(column.isIdentityKey) || dt === 'EMAIL';
  const showQrBtn = !disabled && inputType === 'text' && !column.formula;
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 6, width: '100%' }}>
      <input
        id={inputId}
        type={inputType}
        className="form-input"
        style={{ flex: 1, minWidth: 0 }}
        value={value !== null && value !== undefined ? String(value) : ''}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder || `Nhập ${cleanName.toLowerCase()}...`}
        autoCorrect={isIdentifierOrEmail ? 'off' : undefined}
        autoCapitalize={isIdentifierOrEmail ? 'none' : undefined}
        spellCheck={isIdentifierOrEmail ? false : undefined}
        disabled={disabled}
        required={required}
      />
      {showQrBtn && (
        <>
          <button
            type="button"
            className="btn btn-outline btn-xs"
            style={{ height: size === 'sm' ? 30 : 36, padding: '0 8px', flexShrink: 0 }}
            onClick={() => setQrOpen(true)}
            title={`Quét mã QR/Barcode vào trường ${cleanName}`}
            aria-label={`Quét mã QR vào ${cleanName}`}
          >
            <QrCodeScannerRoundedIcon sx={{ fontSize: 16 }} />
          </button>
          <QrScannerModal
            open={qrOpen}
            title={`Quét mã vào: ${cleanName}`}
            onClose={() => setQrOpen(false)}
            onDetected={(raw) => onChange(raw)}
          />
        </>
      )}
    </div>
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
