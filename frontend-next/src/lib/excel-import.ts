import type { SysColumn, ValidationErrorItem } from './types';

export const MAX_IMPORT_ROWS = 2000;
export interface ImportSourceRow { sourceRow: number; values: unknown[] }
export interface ImportPreview {
  totalRows: number; validRows: number; errorRows: number; inserted: number; updated: number;
  canImport: boolean; fingerprint: string;
  rows: { sourceRow: number; operation: string; recordKey?: string; data: Record<string, unknown>; errors: ValidationErrorItem[] }[];
}

function headerKey(value: unknown): string {
  return String(value ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/[đĐ]/g, 'd').toLowerCase().replace(/[^a-z0-9]/g, '');
}

export function suggestImportMapping(headers: unknown[], columns: SysColumn[]): string[] {
  const used = new Set<string>();
  return headers.map(header => {
    const key = headerKey(header);
    if (!key) return '';
    const column = columns.find(c => !used.has(c.columnCode) && [c.columnCode, c.excelHeader, c.columnName].some(v => headerKey(v) === key));
    if (column) used.add(column.columnCode);
    return column?.columnCode || '';
  });
}

export function getImportSourceRows(grid: unknown[][], headerRow: number): ImportSourceRow[] {
  return grid.slice(headerRow).map((values, index) => ({ sourceRow: headerRow + index + 1, values }))
    .filter(row => row.values.some(value => value != null && String(value).trim() !== ''));
}

export function mapImportRows(rows: ImportSourceRow[], mapping: string[], columns: SysColumn[],
  transform: (value: unknown, column: SysColumn) => unknown = value => value) {
  const targets = mapping.filter(Boolean);
  if (!targets.length) throw new Error('Cần ánh xạ ít nhất một cột dữ liệu.');
  if (new Set(targets).size !== targets.length) throw new Error('Mỗi trường đích chỉ được ánh xạ một lần.');
  if (!rows.length || rows.length > MAX_IMPORT_ROWS) throw new Error(`Cần từ 1 đến ${MAX_IMPORT_ROWS} dòng dữ liệu mỗi lần nhập.`);
  const byCode = new Map(columns.map(column => [column.columnCode, column]));
  return {
    rowNumbers: rows.map(row => row.sourceRow),
    rows: rows.map(row => {
      const result: Record<string, unknown> = Object.create(null);
      mapping.forEach((target, index) => {
        if (!target) return;
        const column = byCode.get(target);
        if (!column) throw new Error(`Trường đích không hợp lệ: ${target}`);
        result[target] = transform(row.values[index] ?? null, column);
      });
      return result;
    }),
  };
}
