import { LookupOption, SysColumn } from './types';

const lookupCache = new Map<string, LookupOption[]>();
const inflight = new Map<string, Promise<LookupOption[]>>();

function makeKey(
  tableId: string,
  valueField?: string | null,
  displayField?: string | null,
  filterField?: string | null,
  filterValue?: string | null
) {
  return `${tableId}::${valueField || ''}::${displayField || ''}::${filterField || ''}::${filterValue || ''}`;
}

export async function fetchLookupOptions(
  tableId: string,
  valueField?: string | null,
  displayField?: string | null,
  forceRefresh = false,
  filterField?: string | null,
  filterValue?: string | null
): Promise<LookupOption[]> {
  if (!tableId) return [];
  const key = makeKey(tableId, valueField, displayField, filterField, filterValue);

  if (!forceRefresh && lookupCache.has(key)) {
    return lookupCache.get(key) || [];
  }
  if (!forceRefresh && inflight.has(key)) {
    return inflight.get(key)!;
  }

  const qs = new URLSearchParams({ tableId });
  if (valueField) qs.set('valueField', valueField);
  if (displayField) qs.set('displayField', displayField);
  if (filterField && filterValue) {
    qs.set('filterField', filterField);
    qs.set('filterValue', filterValue);
  }

  const req = fetch(`/api/records/lookup-options?${qs.toString()}`, { credentials: 'include' })
    .then((r) => r.json())
    .then((res) => {
      const items: LookupOption[] = res.success && Array.isArray(res.data) ? res.data : [];
      lookupCache.set(key, items);
      return items;
    })
    .catch(() => [])
    .finally(() => inflight.delete(key));

  inflight.set(key, req);
  return req;
}

export async function searchLookupOptions(
  tableId: string,
  valueField?: string | null,
  displayField?: string | null,
  query?: string,
  filterField?: string | null,
  filterValue?: string | null
): Promise<LookupOption[]> {
  if (!tableId) return [];
  const qs = new URLSearchParams({ tableId });
  if (valueField) qs.set('valueField', valueField);
  if (displayField) qs.set('displayField', displayField);
  if (query && query.trim()) qs.set('q', query.trim());
  if (filterField && filterValue) {
    qs.set('filterField', filterField);
    qs.set('filterValue', filterValue);
  }

  const res = await fetch(`/api/records/lookup-options?${qs.toString()}`, { credentials: 'include' })
    .then((r) => r.json())
    .catch(() => ({ success: false, data: [] }));
  const items: LookupOption[] = res.success && Array.isArray(res.data) ? res.data : [];
  if (items.length > 0) {
    const key = makeKey(tableId, valueField, displayField, filterField, filterValue);
    const prev = lookupCache.get(key) || [];
    const merged = new Map(prev.map((x) => [x.value, x]));
    items.forEach((it) => merged.set(it.value, it));
    lookupCache.set(key, Array.from(merged.values()));
  }
  return items;
}

export async function batchFetchLookups(columns: SysColumn[]): Promise<void> {
  const lookupCols = columns.filter((c) => c.refTableId);
  await Promise.all(
    lookupCols.map((c) => fetchLookupOptions(c.refTableId!, c.refValueField, c.refDisplayField))
  );
}

export function getLookupOptions(col: SysColumn): LookupOption[] {
  if (!col.refTableId) return [];
  return lookupCache.get(makeKey(col.refTableId, col.refValueField, col.refDisplayField)) || [];
}

export function resolveLookupLabel(col: SysColumn, rawVal: unknown): string | null {
  if (rawVal === null || rawVal === undefined || rawVal === '') return null;
  const opts = getLookupOptions(col);
  const str = String(rawVal).trim();
  const found = opts.find((o) => o.value === str);
  return found ? found.label : null;
}

export function clearLookupCache() {
  lookupCache.clear();
}
