import { SysCategoryItem } from './types';

const categoryCache = new Map<string, SysCategoryItem[]>();
const inflightRequests = new Map<string, Promise<SysCategoryItem[]>>();

export async function fetchCategoryItems(categoryCode: string, forceRefresh = false): Promise<SysCategoryItem[]> {
  if (!categoryCode) return [];
  const key = categoryCode.trim().toUpperCase();

  if (!forceRefresh && categoryCache.has(key)) {
    return categoryCache.get(key) || [];
  }
  if (!forceRefresh && inflightRequests.has(key)) {
    return inflightRequests.get(key)!;
  }

  const req = fetch(`/api/categories/items?categoryCode=${encodeURIComponent(key)}`)
    .then((r) => r.json())
    .then((res) => {
      const items: SysCategoryItem[] = res.success && Array.isArray(res.data) ? res.data : [];
      categoryCache.set(key, items);
      return items;
    })
    .catch(() => [])
    .finally(() => {
      inflightRequests.delete(key);
    });

  inflightRequests.set(key, req);
  return req;
}

export async function batchFetchCategories(codes: string[]): Promise<void> {
  const unique = Array.from(new Set(codes.filter(Boolean).map((c) => c.trim().toUpperCase())));
  await Promise.all(unique.map((c) => fetchCategoryItems(c)));
}

export function getCategoryItems(categoryCode: string): SysCategoryItem[] {
  if (!categoryCode) return [];
  return categoryCache.get(categoryCode.trim().toUpperCase()) || [];
}

export function getCategoryItemName(categoryCode: string, itemCode: unknown): { name: string; color?: string | null } | null {
  if (itemCode === null || itemCode === undefined || itemCode === '') return null;
  const items = getCategoryItems(categoryCode);
  const target = String(itemCode).trim().toUpperCase();
  const found = items.find((i) => i.itemCode.toUpperCase() === target);
  if (!found) return null;
  return { name: found.itemName, color: found.color };
}

export function clearCategoryCache(categoryCode?: string) {
  if (categoryCode) {
    categoryCache.delete(categoryCode.trim().toUpperCase());
  } else {
    categoryCache.clear();
  }
}

export async function createCategoryItemQuick(categoryCode: string, itemName: string): Promise<SysCategoryItem | null> {
  const cat = categoryCode.trim().toUpperCase();
  const name = itemName.trim();
  if (!cat || !name) return null;

  const res = await fetch('/api/categories/items', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({
      categoryCode: cat,
      itemCode: '',
      itemName: name,
    }),
  }).then((r) => r.json());

  if (!res?.success || !res.data) return null;
  clearCategoryCache(cat);
  await fetchCategoryItems(cat, true);
  return res.data as SysCategoryItem;
}
