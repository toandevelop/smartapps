import type { QueryCondition, StructuredQueryFilter, SysColumn } from './types';

export const EMPTY_STRUCTURED_FILTER: StructuredQueryFilter = {
  logic: 'AND',
  conditions: [],
};

export const APPROVAL_STATUS_OPTIONS: Array<{ value: string; label: string }> = [
  { value: 'DRAFT', label: 'Nháp' },
  { value: 'PENDING', label: 'Chờ duyệt' },
  { value: 'APPROVED', label: 'Đã duyệt' },
  { value: 'REJECTED', label: 'Từ chối' },
  { value: 'RECALLED', label: 'Đã thu hồi' },
  { value: 'CANCELLED', label: 'Đã hủy' },
];

export const DATE_PRESET_OPTIONS: Array<{ value: string; label: string }> = [
  { value: 'TODAY', label: 'Hôm nay' },
  { value: 'THIS_WEEK', label: 'Tuần này' },
  { value: 'THIS_MONTH', label: 'Tháng này' },
];

/** Type guard for FUN-06 structured AND/OR filter payloads stored in Saved Views or query strings. */
export function isStructuredFilter(raw: unknown): raw is StructuredQueryFilter {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return false;
  const candidate = raw as Record<string, unknown>;
  return Array.isArray(candidate.conditions);
}

/** Checks whether a single condition has all inputs needed for backend SQL evaluation. */
export function isConditionValid(cond: QueryCondition): boolean {
  const field = (cond.field || '').trim();
  if (!field) return false;
  if (field === 'approvalStatus') {
    return Boolean(cond.value?.trim());
  }
  if (cond.op === 'is_empty' || cond.op === 'not_empty') {
    return true;
  }
  if (cond.op === 'between') {
    return Boolean(cond.value?.trim() && cond.value2?.trim());
  }
  return Boolean(cond.value?.trim());
}

/** Filters and normalizes query conditions by trimming fields/values and pinning approvalStatus to eq. */
export function filterValidConditions(conditions: QueryCondition[]): QueryCondition[] {
  const result: QueryCondition[] = [];
  for (const cond of conditions) {
    if (!isConditionValid(cond)) continue;
    const field = cond.field.trim();
    const op: QueryCondition['op'] = field === 'approvalStatus' ? 'eq' : cond.op;
    const cleaned: QueryCondition = { field, op };
    if (op !== 'is_empty' && op !== 'not_empty') {
      cleaned.value = (cond.value || '').trim();
    }
    if (op === 'between' && cond.value2 !== undefined) {
      cleaned.value2 = cond.value2.trim();
    }
    result.push(cleaned);
  }
  return result;
}

/** Normalizes legacy flat filter dictionaries and structured AND/OR objects from SysSavedView.filters. */
export function normalizeSavedViewFilters(raw: unknown): {
  flatFilters: Record<string, string>;
  structured: StructuredQueryFilter;
} {
  if (isStructuredFilter(raw)) {
    return {
      flatFilters: {},
      structured: {
        logic: raw.logic === 'OR' ? 'OR' : 'AND',
        conditions: filterValidConditions(raw.conditions),
      },
    };
  }
  const flatFilters: Record<string, string> = {};
  if (raw && typeof raw === 'object' && !Array.isArray(raw)) {
    for (const [key, val] of Object.entries(raw as Record<string, unknown>)) {
      if (typeof val === 'string' && val.trim()) {
        flatFilters[key] = val.trim();
      } else if (typeof val === 'number' || typeof val === 'boolean') {
        flatFilters[key] = String(val);
      }
    }
  }
  return {
    flatFilters,
    structured: { logic: 'AND', conditions: [] },
  };
}

/**
 * Produces the backend-compatible `filters` payload for `/api/records` and `/api/records/export`.
 * Merges active flat quick-filters when the structured group uses AND logic; uses structured group directly for OR.
 */
export function buildEffectiveFiltersPayload(
  flatFilters: Record<string, string>,
  structured?: StructuredQueryFilter | null,
): Record<string, string> | StructuredQueryFilter | null {
  const cleanFlat: Record<string, string> = {};
  for (const [k, v] of Object.entries(flatFilters || {})) {
    if (typeof v === 'string' && v.trim()) {
      cleanFlat[k] = v.trim();
    }
  }

  const validStructured = structured ? filterValidConditions(structured.conditions) : [];
  if (validStructured.length > 0) {
    const logic = structured?.logic === 'OR' ? 'OR' : 'AND';
    const flatAsConditions: QueryCondition[] =
      logic === 'AND'
        ? Object.entries(cleanFlat).map(([field, value]) => ({
            field,
            op: field === 'approvalStatus' ? 'eq' : 'contains',
            value,
          }))
        : [];
    return {
      logic,
      conditions: [...validStructured, ...flatAsConditions],
    };
  }

  return Object.keys(cleanFlat).length > 0 ? cleanFlat : null;
}

/** Returns operator choices matching RecordService.QueryRecordsAsync capabilities for each field type. */
export function getOperatorsForColumn(
  columnCode: string,
  dataType?: string,
): Array<{ value: QueryCondition['op']; label: string }> {
  if (columnCode === 'approvalStatus') {
    return [{ value: 'eq', label: 'Bằng (=)' }];
  }
  const dt = (dataType || '').toUpperCase();
  if (dt === 'NUMBER' || dt === 'CURRENCY') {
    return [
      { value: 'eq', label: 'Bằng (=)' },
      { value: 'neq', label: 'Khác (!=)' },
      { value: 'gt', label: 'Lớn hơn (>)' },
      { value: 'gte', label: 'Lớn hơn hoặc bằng (>=)' },
      { value: 'lt', label: 'Nhỏ hơn (<)' },
      { value: 'lte', label: 'Nhỏ hơn hoặc bằng (<=)' },
      { value: 'between', label: 'Trong khoảng (between)' },
      { value: 'is_empty', label: 'Để trống' },
      { value: 'not_empty', label: 'Có dữ liệu' },
    ];
  }
  if (dt === 'DATE') {
    return [
      { value: 'date_preset', label: 'Mốc thời gian nhanh' },
      { value: 'eq', label: 'Đúng ngày (=)' },
      { value: 'neq', label: 'Khác ngày (!=)' },
      { value: 'contains', label: 'Chứa chuỗi ngày' },
      { value: 'is_empty', label: 'Để trống' },
      { value: 'not_empty', label: 'Có dữ liệu' },
    ];
  }
  if (dt === 'BOOLEAN') {
    return [
      { value: 'eq', label: 'Là (=)' },
      { value: 'neq', label: 'Không phải (!=)' },
      { value: 'is_empty', label: 'Để trống' },
      { value: 'not_empty', label: 'Có dữ liệu' },
    ];
  }
  return [
    { value: 'contains', label: 'Chứa từ khóa' },
    { value: 'eq', label: 'Bằng chính xác (=)' },
    { value: 'neq', label: 'Khác (!=)' },
    { value: 'in', label: 'Thuộc danh sách (A, B, ...)' },
    { value: 'is_empty', label: 'Để trống' },
    { value: 'not_empty', label: 'Có dữ liệu' },
  ];
}

const OP_LABELS: Record<QueryCondition['op'], string> = {
  contains: 'chứa',
  eq: '=',
  neq: '!=',
  gt: '>',
  gte: '>=',
  lt: '<',
  lte: '<=',
  between: 'trong khoảng',
  in: 'thuộc',
  is_empty: 'trống',
  not_empty: 'có dữ liệu',
  date_preset: 'mốc',
};

/** Formats a QueryCondition into a human-readable Vietnamese badge label. */
export function describeCondition(cond: QueryCondition, columns: SysColumn[]): string {
  const fieldName =
    cond.field === 'approvalStatus'
      ? 'Trạng thái duyệt'
      : columns.find((c) => c.columnCode === cond.field)?.columnName || cond.field;
  const opLabel = OP_LABELS[cond.op] || cond.op;
  if (cond.op === 'is_empty' || cond.op === 'not_empty') {
    return `${fieldName} ${opLabel}`;
  }
  if (cond.op === 'between') {
    return `${fieldName}: ${cond.value || ''} → ${cond.value2 || ''}`;
  }
  if (cond.op === 'date_preset') {
    const presetLabel = DATE_PRESET_OPTIONS.find((d) => d.value === cond.value)?.label || cond.value || '';
    return `${fieldName}: ${presetLabel}`;
  }
  return `${fieldName} ${opLabel} "${cond.value || ''}"`;
}
