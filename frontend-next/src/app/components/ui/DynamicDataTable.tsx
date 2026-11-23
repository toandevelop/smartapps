'use client';

import React, { useEffect, useState, useMemo } from 'react';
import DataTable, { ColumnDef } from './DataTable';
import DynamicInlineCell from './DynamicInlineCell';
import StatusBadge from './StatusBadge';
import { batchFetchCategories, getCategoryItemName, getCategoryItems } from '@/lib/category-service';
import { batchFetchLookups, getLookupOptions, resolveLookupLabel } from '@/lib/lookup-service';
import { cleanColumnDisplayName, formatCurrencyVnd } from '@/lib/column-utils';
import {
  MONDAY_PRIORITY_PRESETS,
  MONDAY_STATUS_PRESETS,
  clampProgressValue,
  clampRatingValue,
  computeMondayColumnSummary,
  formatDateTimeDisplay,
  getAvatarInitialsAndColor,
  getColumnTypeMeta,
  getMondayProgressColor,
  getTagColor,
  normalizeSafeLinkUrl,
  parseTagsValue,
  resolveMondayPriorityStyle,
  resolveMondayStatusStyle,
} from '@/lib/monday-fields';
import { AppRecord, SysColumn } from '@/lib/types';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import ErrorOutlineRoundedIcon from '@mui/icons-material/ErrorOutlineRounded';
import FunctionsRoundedIcon from '@mui/icons-material/FunctionsRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import LinkRoundedIcon from '@mui/icons-material/LinkRounded';
import PrintRoundedIcon from '@mui/icons-material/PrintRounded';
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded';
import StarRoundedIcon from '@mui/icons-material/StarRounded';
import StarBorderRoundedIcon from '@mui/icons-material/StarBorderRounded';

export interface DynamicDataTableProps {
  title?: React.ReactNode;
  subtitle?: React.ReactNode;
  badge?: React.ReactNode;
  headerActions?: React.ReactNode;

  searchPlaceholder?: string;
  searchValue?: string;
  onSearchChange?: (val: string) => void;
  statusFilter?: {
    value: string;
    onChange: (val: string) => void;
    options: Array<{ value: string; label: string }>;
  };

  columns: SysColumn[];
  records: AppRecord[];
  loading?: boolean;
  enableSelection?: boolean;
  selectedIds?: string[];
  onSelectionChange?: (ids: string[]) => void;
  onBatchDelete?: (ids: string[]) => void;
  batchActions?: React.ReactNode;
  onEditRecord?: (record: AppRecord) => void;
  onDeleteRecord?: (recordId: string) => void;
  onInspectRelations?: (record: AppRecord) => void;
  onPrintRecord?: (record: AppRecord) => void;
  approvalEnabled?: boolean;
  canApprove?: boolean;
  currentUserRoles?: string[];
  onSubmitApproval?: (record: AppRecord) => void;
  onApproveRecord?: (record: AppRecord, approve: boolean) => void;
  onRecallApproval?: (record: AppRecord) => void;
  onCancelApproval?: (record: AppRecord) => void;
  onDirectSave?: (rowId: string, field: string, newVal: unknown) => Promise<boolean>;
  inlineEditEnabled?: boolean;
  enableFiltering?: boolean;
  serverSort?: {
    sortKey: string;
    direction: 'asc' | 'desc';
    onSortChange: (key: string, dir: 'asc' | 'desc') => void;
  };
  storageKey?: string;
  pagination?: {
    page: number;
    pageSize: number;
    total: number;
    totalPages: number;
    onPageChange: (page: number) => void;
    onPageSizeChange: (pageSize: number) => void;
  };
  emptyText?: string;
  emptyAction?: React.ReactNode;
  toolbarExtra?: React.ReactNode;
}

export default function DynamicDataTable({
  title,
  subtitle,
  badge,
  headerActions,
  searchPlaceholder,
  searchValue,
  onSearchChange,
  statusFilter,
  columns,
  records,
  loading = false,
  enableSelection = true,
  selectedIds = [],
  onSelectionChange,
  onBatchDelete,
  batchActions,
  onEditRecord,
  onDeleteRecord,
  onInspectRelations,
  onPrintRecord,
  approvalEnabled = false,
  canApprove = false,
  currentUserRoles = [],
  onSubmitApproval,
  onApproveRecord,
  onRecallApproval,
  onCancelApproval,
  onDirectSave,
  inlineEditEnabled = true,
  enableFiltering = false,
  serverSort,
  storageKey = 'smartapps_dynamic',
  pagination,
  emptyText,
  emptyAction,
  toolbarExtra,
}: DynamicDataTableProps) {
  const [refVersion, setRefVersion] = useState(0);

  const visibleDynamicColumns = useMemo(
    () => columns.filter((c) => c.showInTable !== false),
    [columns],
  );

  useEffect(() => {
    const codes = columns.map((c) => c.refCategoryCode).filter((x): x is string => Boolean(x));
    const hasLookups = columns.some((c) => Boolean(c.refTableId));
    Promise.all([
      codes.length > 0 ? batchFetchCategories(codes) : Promise.resolve(),
      hasLookups ? batchFetchLookups(columns) : Promise.resolve(),
    ]).then(() => setRefVersion((v) => v + 1));
  }, [columns]);

  const tableColumns = useMemo<ColumnDef<AppRecord>[]>(() => {
    if (refVersion < 0) return [];
    const result: ColumnDef<AppRecord>[] = [
      {
        key: 'stt',
        header: '#',
        width: 48,
        align: 'center',
        sortable: false,
        filterable: false,
        pinned: 'left',
        render: (_, idx) => {
          const offset = pagination ? (pagination.page - 1) * pagination.pageSize : 0;
          return <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>{offset + idx + 1}</span>;
        },
      },
    ];

    for (const col of visibleDynamicColumns) {
      const cleanLabel = cleanColumnDisplayName(col.columnName);
      const dt = (col.dataType || 'STRING').toUpperCase();
      const colMeta = getColumnTypeMeta(dt);

      const selectOptions = col.refTableId
        ? getLookupOptions(col).map((o) => ({ value: o.value, label: o.label, subLabel: o.subLabel, searchText: o.searchText }))
        : col.refCategoryCode
        ? getCategoryItems(col.refCategoryCode).map((it) => ({
            value: it.itemCode,
            label: `${it.itemCode} - ${it.itemName}`,
            subLabel: it.itemName,
          }))
        : dt === 'STATUS'
        ? MONDAY_STATUS_PRESETS.map((p) => ({ value: p.code, label: p.label, subLabel: p.label }))
        : dt === 'PRIORITY'
        ? MONDAY_PRIORITY_PRESETS.map((p) => ({ value: p.code, label: p.label, subLabel: p.label }))
        : [];

      const filterType: 'text' | 'select' | 'date' =
        selectOptions.length > 0 || dt === 'BOOLEAN'
          ? 'select'
          : dt === 'DATE' || dt === 'DATETIME'
          ? 'date'
          : 'text';

      const filterOptions =
        selectOptions.length > 0
          ? selectOptions.map((o) => ({ value: o.value, label: o.label, subLabel: o.subLabel }))
          : dt === 'BOOLEAN'
          ? [
              { value: 'true', label: 'Có / Đúng' },
              { value: 'false', label: 'Không / Sai' },
            ]
          : undefined;

      result.push({
        key: col.columnCode,
        label: cleanLabel,
        width: col.width || 160,
        align:
          dt === 'NUMBER' || dt === 'CURRENCY'
            ? 'right'
            : dt === 'STATUS' || dt === 'PRIORITY' || dt === 'RATING' || dt === 'BOOLEAN'
            ? 'center'
            : 'left',
        filterable: true,
        filterType,
        filterOptions,
        refCategoryCode: col.refCategoryCode,
        header: (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
            <span
              aria-hidden="true"
              className="monday-col-type-badge"
              style={{
                backgroundColor: `color-mix(in srgb, ${colMeta.accent} 14%, var(--bg-surface))`,
                color: colMeta.accent,
              }}
            >
              {colMeta.shortBadge}
            </span>
            <span>{cleanLabel}</span>
            {col.isRequired && <span style={{ color: '#d93025' }}>*</span>}
            {col.refTableId && (
              <span title="Liên kết dữ liệu từ App/Bảng khác" style={{ color: '#0284c7', display: 'inline-flex' }}>
                <LinkRoundedIcon sx={{ fontSize: 14 }} />
              </span>
            )}
            {col.formula && (
              <span title={`Công thức: ${col.formula}`} style={{ color: '#1a73e8', display: 'inline-flex' }}>
                <FunctionsRoundedIcon sx={{ fontSize: 14 }} />
              </span>
            )}
          </span>
        ),
        render: (row) => {
          const rawVal = row.data?.[col.columnCode];
          let displayNode: React.ReactNode = <span style={{ color: '#9aa0a6' }}>—</span>;

          if (col.refTableId && rawVal) {
            const linkedLabel = resolveLookupLabel(col, rawVal);
            displayNode = (
              <span
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 5,
                  padding: '3px 10px',
                  borderRadius: 999,
                  fontSize: 12,
                  fontWeight: 600,
                  backgroundColor: '#e0f2fe',
                  color: '#0369a1',
                  border: '1px solid #bae6fd',
                }}
              >
                <LinkRoundedIcon sx={{ fontSize: 13 }} />
                {linkedLabel || String(rawVal)}
              </span>
            );
          } else if (dt === 'STATUS' && rawVal) {
            const cat = col.refCategoryCode ? getCategoryItemName(col.refCategoryCode, rawVal) : null;
            const stStyle = resolveMondayStatusStyle(rawVal, cat?.color, cat?.name);
            displayNode = (
              <span
                className="monday-status-pill"
                style={{ backgroundColor: stStyle.bg, color: stStyle.color }}
              >
                {stStyle.label}
              </span>
            );
          } else if (dt === 'PRIORITY' && rawVal) {
            const cat = col.refCategoryCode ? getCategoryItemName(col.refCategoryCode, rawVal) : null;
            const prStyle = resolveMondayPriorityStyle(rawVal, cat?.name);
            displayNode = (
              <span
                className="monday-status-pill"
                style={{ backgroundColor: prStyle.bg, color: prStyle.color }}
              >
                {prStyle.label}
              </span>
            );
          } else if (col.refCategoryCode && rawVal) {
            const cat = getCategoryItemName(col.refCategoryCode, rawVal);
            const stStyle = resolveMondayStatusStyle(rawVal, cat?.color, cat?.name);
            displayNode = cat ? (
              <span
                className="monday-status-pill"
                style={{
                  backgroundColor: stStyle.bg,
                  color: stStyle.color,
                }}
              >
                {cat.name}
              </span>
            ) : (
              <code>{String(rawVal)}</code>
            );
          } else if (dt === 'RATING') {
            const stars = clampRatingValue(rawVal);
            displayNode = (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 1 }}>
                {[1, 2, 3, 4, 5].map((s) =>
                  s <= stars ? (
                    <StarRoundedIcon key={s} sx={{ fontSize: 17, color: '#ffcb00' }} />
                  ) : (
                    <StarBorderRoundedIcon key={s} sx={{ fontSize: 17, color: '#cbd5e1' }} />
                  )
                )}
              </span>
            );
          } else if (
            dt === 'PROGRESS' ||
            (dt === 'NUMBER' &&
              rawVal !== null &&
              rawVal !== undefined &&
              rawVal !== '' &&
              /(tien_do|phan_tram|percent|progress|%)/i.test(`${col.columnCode} ${col.columnName}`))
          ) {
            const pct = clampProgressValue(rawVal);
            const barColor = getMondayProgressColor(pct);
            displayNode = (
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 110 }}>
                <div
                  style={{
                    flex: 1,
                    height: 8,
                    borderRadius: 999,
                    backgroundColor: 'var(--bg-secondary)',
                    border: '1px solid var(--border-subtle)',
                    overflow: 'hidden',
                  }}
                >
                  <div
                    style={{
                      width: `${pct}%`,
                      height: '100%',
                      backgroundColor: barColor,
                      transition: 'width 0.25s ease',
                    }}
                  />
                </div>
                <span style={{ fontSize: 11.5, fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: barColor }}>
                  {pct}%
                </span>
              </div>
            );
          } else if (dt === 'TAGS' && rawVal) {
            const tags = parseTagsValue(rawVal);
            displayNode =
              tags.length > 0 ? (
                <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
                  {tags.slice(0, 3).map((t) => {
                    const tc = getTagColor(t);
                    return (
                      <span
                        key={t}
                        style={{
                          padding: '2px 7px',
                          borderRadius: 999,
                          fontSize: 11,
                          fontWeight: 600,
                          backgroundColor: tc.bg,
                          color: tc.color,
                        }}
                      >
                        #{t}
                      </span>
                    );
                  })}
                  {tags.length > 3 && (
                    <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>+{tags.length - 3}</span>
                  )}
                </div>
              ) : (
                <span style={{ color: '#9aa0a6' }}>—</span>
              );
          } else if (dt === 'COLOR' && rawVal) {
            const colorHex = String(rawVal).trim();
            displayNode = (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                <span
                  style={{
                    width: 18,
                    height: 18,
                    borderRadius: 5,
                    backgroundColor: colorHex,
                    border: '1px solid rgba(0,0,0,0.18)',
                    display: 'inline-block',
                  }}
                />
                <code style={{ fontSize: 11.5 }}>{colorHex.toUpperCase()}</code>
              </span>
            );
          } else if (dt === 'LINK' && rawVal) {
            const safeLink = normalizeSafeLinkUrl(rawVal);
            displayNode = safeLink ? (
              <a
                href={safeLink.href}
                target="_blank"
                rel="noreferrer"
                onClick={(e) => e.stopPropagation()}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 4,
                  color: '#0086c0',
                  fontWeight: 600,
                  fontSize: 12.5,
                  textDecoration: 'underline',
                  maxWidth: 190,
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                }}
              >
                <span>{safeLink.label}</span>
                <OpenInNewRoundedIcon sx={{ fontSize: 13, flexShrink: 0 }} />
              </a>
            ) : (
              <span>{String(rawVal)}</span>
            );
          } else if (dt === 'USER' && rawVal) {
            const av = getAvatarInitialsAndColor(rawVal);
            displayNode = (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 7 }}>
                <span
                  style={{
                    width: 24,
                    height: 24,
                    borderRadius: '50%',
                    backgroundColor: av.bg,
                    color: '#ffffff',
                    fontSize: 10.5,
                    fontWeight: 700,
                    display: 'inline-flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    flexShrink: 0,
                  }}
                >
                  {av.initials}
                </span>
                <span style={{ fontWeight: 600, fontSize: 12.5 }}>{av.label}</span>
              </span>
            );
          } else if ((dt === 'DATETIME' || dt === 'TIME') && rawVal) {
            displayNode = (
              <span style={{ fontVariantNumeric: 'tabular-nums', fontWeight: 500 }}>
                {formatDateTimeDisplay(rawVal, dt)}
              </span>
            );
          } else if (dt === 'IMAGE' && rawVal) {
            const urlStr = typeof rawVal === 'string'
              ? rawVal
              : typeof rawVal === 'object' && rawVal !== null && 'url' in rawVal
              ? String((rawVal as { url?: unknown }).url ?? '')
              : '';
            displayNode = (
              <a href={urlStr} target="_blank" rel="noreferrer" onClick={(e) => e.stopPropagation()}>
                <img
                  src={urlStr}
                  alt="Ảnh"
                  style={{
                    width: 44,
                    height: 32,
                    borderRadius: 6,
                    objectFit: 'cover',
                    border: '1px solid var(--border-light)',
                    display: 'block',
                  }}
                />
              </a>
            );
          } else if (dt === 'FILE' && rawVal) {
            const urlStr = typeof rawVal === 'string'
              ? rawVal
              : typeof rawVal === 'object' && rawVal !== null && 'url' in rawVal
              ? String((rawVal as { url?: unknown }).url ?? '')
              : '';
            const match = urlStr.match(/[?&]name=([^&]+)/);
            const label = match ? decodeURIComponent(match[1]) : 'Tải tệp';
            displayNode = (
              <a
                href={urlStr}
                target="_blank"
                rel="noreferrer"
                onClick={(e) => e.stopPropagation()}
                className="badge badge-info"
                style={{ textDecoration: 'none', maxWidth: 160, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
              >
                {label}
              </a>
            );
          } else if (dt === 'SUB_TABLE' && Array.isArray(rawVal)) {
            displayNode = <span className="badge badge-info">{rawVal.length} dòng chi tiết</span>;
          } else if (dt === 'MULTI_FILE' && Array.isArray(rawVal)) {
            displayNode = <span className="badge badge-info">{rawVal.length} tệp đính kèm</span>;
          } else if (dt === 'MULTI_SELECT' && Array.isArray(rawVal)) {
            displayNode = (
              <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
                {rawVal.slice(0, 3).map((v, idx) => (
                  <span key={idx} className="badge badge-info" style={{ fontSize: 10.5 }}>{String(v)}</span>
                ))}
                {rawVal.length > 3 && <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>+{rawVal.length - 3}</span>}
              </div>
            );
          } else if (dt === 'SIGNATURE' && typeof rawVal === 'string' && rawVal.startsWith('data:image/')) {
            displayNode = <img src={rawVal} alt="Chữ ký" style={{ height: 26, borderRadius: 4, border: '1px solid var(--border-light)', backgroundColor: '#fff' }} />;
          } else if (dt === 'AUTO_NUMBER' && rawVal) {
            displayNode = <code>{String(rawVal)}</code>;
          } else if (dt === 'CURRENCY' && rawVal !== null && rawVal !== undefined && rawVal !== '') {
            displayNode = <span style={{ fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>{formatCurrencyVnd(rawVal)}</span>;
          } else if (
            dt === 'NUMBER' &&
            rawVal !== null &&
            rawVal !== undefined &&
            rawVal !== '' &&
            /(tien_do|phan_tram|percent|progress|%)/i.test(`${col.columnCode} ${col.columnName}`)
          ) {
            const pct = Math.max(0, Math.min(100, Number(rawVal) || 0));
            displayNode = (
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 100 }}>
                <div style={{ flex: 1, height: 6, borderRadius: 999, backgroundColor: 'var(--border-subtle)', overflow: 'hidden' }}>
                  <div
                    style={{
                      width: `${pct}%`,
                      height: '100%',
                      backgroundColor: pct >= 100 ? '#16a34a' : pct >= 50 ? '#1a73e8' : '#f59e0b',
                    }}
                  />
                </div>
                <span style={{ fontSize: 11.5, fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>{pct}%</span>
              </div>
            );
          } else if (dt === 'BOOLEAN') {
            displayNode = (
              <span className={`badge ${rawVal ? 'badge-success' : 'badge-warning'}`}>
                {rawVal ? 'Có' : 'Không'}
              </span>
            );
          } else if (rawVal !== null && rawVal !== undefined && rawVal !== '') {
            const strVal = String(rawVal);
            const upperVal = strVal.trim().toUpperCase();
            if (col.isIdentityKey) {
              displayNode = <code>{strVal}</code>;
            } else if (/^(payment_status|trang_thai|status)/i.test(col.columnCode) && /^(PAID|UNPAID|DA_THANH_TOAN|CHUA_THANH_TOAN|HOAN_THANH|CANCELLED|HUY|FAILED)$/i.test(upperVal)) {
              const badgeCls = /^(PAID|DA_THANH_TOAN|HOAN_THANH)$/.test(upperVal)
                ? 'badge-success'
                : /^(CANCELLED|HUY|FAILED)$/.test(upperVal)
                  ? 'badge-danger'
                  : 'badge-warning';
              displayNode = <span className={`badge ${badgeCls}`}>{strVal}</span>;
            } else {
              displayNode = <span>{strVal}</span>;
            }
          }

          const isAdminRole = currentUserRoles.some((ur) => ur.toUpperCase() === 'ADMIN');
          const isRoleReadOnly = !isAdminRole && (col.readOnlyForRoles || []).some((rr) =>
            currentUserRoles.some((ur) => ur.toUpperCase() === rr.toUpperCase()),
          );

          if (inlineEditEnabled && !isRoleReadOnly && onDirectSave && dt !== 'FILE' && dt !== 'IMAGE' && dt !== 'SUB_TABLE' && dt !== 'AUTO_NUMBER') {
            return (
              <DynamicInlineCell
                rowId={row.id}
                column={col}
                value={rawVal}
                displayNode={displayNode}
                selectOptions={selectOptions}
                onDirectSave={onDirectSave}
              />
            );
          }
          return displayNode;
        },
      });
    }

    result.push({
      key: 'status',
      header: 'Kiểm tra',
      label: 'Trạng thái',
      width: 125,
      sortable: false,
      render: (row) => {
        const errs = row.validationErrors || [];
        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <StatusBadge status={row.status} size="sm" />
            {errs.length > 0 && (
              <span title={errs.map((e) => e.message).join('\n')} style={{ color: '#d93025', display: 'inline-flex', cursor: 'help' }}>
                <ErrorOutlineRoundedIcon sx={{ fontSize: 16 }} />
              </span>
            )}
          </div>
        );
      },
    });

    if (approvalEnabled) {
      result.push({
        key: 'approvalStatus',
        header: 'Phê duyệt',
        width: 195,
        sortable: false,
        render: (row) => {
          const st = (row.approvalStatus || 'DRAFT').toUpperCase();
          const badgeClass =
            st === 'APPROVED'
              ? 'badge-success'
              : st === 'PENDING'
              ? 'badge-warning'
              : st === 'REJECTED' || st === 'CANCELLED'
              ? 'badge-danger'
              : 'badge-info';
          const stepSuffix =
            st === 'PENDING' && row.totalWorkflowSteps && row.totalWorkflowSteps > 1 && row.currentStepOrder
              ? ` • B${row.currentStepOrder}/${row.totalWorkflowSteps}`
              : '';
          const label =
            st === 'APPROVED'
              ? 'Đã duyệt'
              : st === 'PENDING'
              ? `Chờ duyệt${stepSuffix}`
              : st === 'REJECTED'
              ? 'Từ chối'
              : st === 'RECALLED'
              ? 'Đã thu hồi'
              : st === 'CANCELLED'
              ? 'Đã hủy'
              : 'Nháp';
          return (
            <div style={{ display: 'flex', alignItems: 'center', gap: 4, flexWrap: 'wrap' }}>
              <span
                className={`badge ${badgeClass}`}
                title={
                  st === 'PENDING' && row.currentStepName
                    ? `Đang chờ: ${row.currentStepName}${row.currentStepApproverRoles?.length ? ` (${row.currentStepApproverRoles.join(', ')})` : ''}`
                    : row.rejectionReason || undefined
                }
              >
                {label}
              </span>
              {(st === 'DRAFT' || st === 'REJECTED' || st === 'RECALLED') && onSubmitApproval && (
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  onClick={() => onSubmitApproval(row)}
                >
                  {st === 'DRAFT' ? 'Trình duyệt' : 'Nộp lại'}
                </button>
              )}
              {st === 'PENDING' && onRecallApproval && (
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  onClick={() => onRecallApproval(row)}
                >
                  Thu hồi
                </button>
              )}
              {(st === 'PENDING' || st === 'REJECTED' || st === 'RECALLED') && onCancelApproval && (
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  style={{ color: '#64748b' }}
                  onClick={() => onCancelApproval(row)}
                >
                  Hủy
                </button>
              )}
              {st === 'PENDING' &&
                onApproveRecord &&
                (() => {
                  if (canApprove) return true;
                  const stepRoles = row.currentStepApproverRoles || [];
                  if (stepRoles.length === 0 || currentUserRoles.length === 0) return false;
                  return currentUserRoles.some((ur) =>
                    ur.toUpperCase() === 'ADMIN' || stepRoles.some((sr) => sr.toUpperCase() === ur.toUpperCase())
                  );
                })() && (
                <>
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    style={{ color: '#1e8e3e' }}
                    onClick={() => onApproveRecord(row, true)}
                  >
                    Duyệt
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    style={{ color: '#d93025' }}
                    onClick={() => onApproveRecord(row, false)}
                  >
                    Từ chối
                  </button>
                </>
              )}
            </div>
          );
        },
      });
    }

    if (onEditRecord || onDeleteRecord || onInspectRelations || onPrintRecord) {
      result.push({
        key: 'actions',
        header: 'Thao tác',
        width: 165,
        align: 'center',
        sortable: false,
        filterable: false,
        pinned: 'right',
        render: (row) => (
          <div style={{ display: 'inline-flex', gap: 4 }}>
            {onInspectRelations && (
              <button
                type="button"
                className="btn btn-outline btn-xs"
                style={{ color: '#1a73e8' }}
                onClick={() => onInspectRelations(row)}
                title="Xem Hồ sơ Chi tiết Đa Tab (360°)"
              >
                <AccountTreeRoundedIcon sx={{ fontSize: 14 }} />
              </button>
            )}
            {onPrintRecord && (
              <button
                type="button"
                className="btn btn-outline btn-xs"
                onClick={() => onPrintRecord(row)}
                title="In phiếu / Biên bản"
              >
                <PrintRoundedIcon sx={{ fontSize: 14 }} />
              </button>
            )}
            {onEditRecord && (
              <button
                type="button"
                className="btn btn-outline btn-xs"
                onClick={() => onEditRecord(row)}
                title="Mở form chi tiết"
              >
                <EditRoundedIcon sx={{ fontSize: 14 }} />
              </button>
            )}
            {onDeleteRecord && (
              <button
                type="button"
                className="btn btn-outline btn-xs"
                style={{ color: '#d93025' }}
                onClick={() => onDeleteRecord(row.id)}
                title="Xóa bản ghi"
              >
                <DeleteOutlineRoundedIcon sx={{ fontSize: 14 }} />
              </button>
            )}
          </div>
        ),
      });
    }

    return result;
  }, [refVersion, visibleDynamicColumns, pagination, inlineEditEnabled, approvalEnabled, canApprove, currentUserRoles, onSubmitApproval, onApproveRecord, onRecallApproval, onCancelApproval, onDirectSave, onEditRecord, onDeleteRecord, onInspectRelations, onPrintRecord]);

  const summaryItems = useMemo(() => {
    if (!records || records.length === 0) return [];
    const items: Array<{ col: SysColumn; summary: ReturnType<typeof computeMondayColumnSummary> }> = [];
    for (const col of visibleDynamicColumns) {
      const summary = computeMondayColumnSummary(records, col);
      if (summary.kind !== 'EMPTY') {
        items.push({ col, summary });
      }
    }
    return items.slice(0, 6);
  }, [records, visibleDynamicColumns]);

  const footerNode =
    summaryItems.length > 0 ? (
      <div
        className="monday-board-summary-bar"
        role="region"
        aria-label="Tổng hợp nhanh chân bảng monday.com"
      >
        <span style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: 0.4 }}>
          Tổng hợp ({records.length} dòng)
        </span>
        {summaryItems.map(({ col, summary }) => {
          const cleanLabel = cleanColumnDisplayName(col.columnName);
          if (summary.kind === 'STATUS_BAR' && summary.segments) {
            return (
              <div
                key={col.columnCode}
                style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}
                title={summary.segments.map((s) => `${s.label}: ${s.count} (${s.pct}%)`).join(' • ')}
              >
                <span style={{ fontSize: 11.5, color: 'var(--text-secondary)', fontWeight: 600 }}>{cleanLabel}:</span>
                <div
                  style={{
                    width: 96,
                    height: 12,
                    borderRadius: 4,
                    overflow: 'hidden',
                    display: 'flex',
                    border: '1px solid rgba(0,0,0,0.08)',
                  }}
                >
                  {summary.segments.map((seg, i) => (
                    <div
                      key={`${seg.label}_${i}`}
                      style={{
                        width: `${Math.max(8, seg.pct)}%`,
                        backgroundColor: seg.color,
                        height: '100%',
                      }}
                    />
                  ))}
                </div>
              </div>
            );
          }

          return (
            <div
              key={col.columnCode}
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 6,
                padding: '2px 10px',
                borderRadius: 999,
                backgroundColor: 'var(--bg-surface)',
                border: '1px solid var(--border-subtle)',
                fontSize: 11.5,
              }}
            >
              <span style={{ color: 'var(--text-muted)' }}>{cleanLabel}:</span>
              <strong style={{ color: 'var(--text-primary)', fontVariantNumeric: 'tabular-nums' }}>
                {summary.text}
              </strong>
            </div>
          );
        })}
      </div>
    ) : undefined;

  return (
    <DataTable<AppRecord>
      title={title}
      subtitle={subtitle}
      badge={badge}
      headerActions={headerActions}
      searchPlaceholder={searchPlaceholder}
      searchValue={searchValue}
      onSearchChange={onSearchChange}
      statusFilter={statusFilter}
      columns={tableColumns}
      data={records}
      loading={loading}
      enableSelection={enableSelection}
      enableFiltering={enableFiltering}
      serverSort={serverSort}
      onRowClick={onInspectRelations}
      selectedIds={selectedIds}
      onSelectionChange={onSelectionChange}
      onBatchDelete={onBatchDelete}
      batchActions={batchActions}
      storageKey={storageKey}
      pagination={pagination}
      emptyText={emptyText}
      emptyAction={emptyAction}
      toolbarExtra={toolbarExtra}
      footerContent={footerNode}
    />
  );
}
