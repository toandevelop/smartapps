export function cleanColumnDisplayName(name: string | null | undefined): string {
  if (!name) return '';
  let s = String(name).trim();
  s = s.replace(/\s*\(\s*\*\s*\)/g, '');
  s = s.replace(/\s*\*+$/g, '');
  s = s.replace(/\s{2,}/g, ' ');
  return s.trim() || String(name);
}

export function formatCurrencyVnd(val: unknown): string {
  if (val === null || val === undefined || val === '') return '—';
  const num = Number(val);
  if (Number.isNaN(num)) return String(val);
  return new Intl.NumberFormat('vi-VN').format(num) + ' ₫';
}
