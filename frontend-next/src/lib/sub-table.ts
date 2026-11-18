export type SubTableDataType =
  | 'STRING'
  | 'NUMBER'
  | 'CURRENCY'
  | 'DATE'
  | 'BOOLEAN'
  | 'EMAIL';

export interface SubTableColumnDef {
  code: string;
  name: string;
  dataType: SubTableDataType;
  required?: boolean;
  formula?: string;
  defaultValue?: string;
  validationRegex?: string;
}

export function parseSubTableSchema(defaultValue?: string | null): SubTableColumnDef[] {
  const raw = (defaultValue || '').trim();
  if (raw.startsWith('[')) {
    try {
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed) && parsed.length > 0) {
        const cleaned = parsed
          .filter((item): item is Record<string, unknown> => Boolean(item && typeof item === 'object' && item.code))
          .map((item) => {
            const code = String(item.code).trim();
            const dt = String(item.dataType || 'STRING').trim().toUpperCase() as SubTableDataType;
            return {
              code,
              name: item.name ? String(item.name).trim() : code,
              dataType: (['STRING', 'NUMBER', 'CURRENCY', 'DATE', 'BOOLEAN', 'EMAIL'].includes(dt)
                ? dt
                : 'STRING') as SubTableDataType,
              required: Boolean(item.required),
              formula: item.formula ? String(item.formula).trim() : undefined,
              defaultValue: item.defaultValue != null ? String(item.defaultValue) : undefined,
              validationRegex: item.validationRegex ? String(item.validationRegex).trim() : undefined,
            };
          });
        if (cleaned.length > 0) return cleaned;
      }
    } catch {
      // Fallback to comma-separated format
    }
  }
  const fallback = raw || 'hang_muc,so_luong,ghi_chu';
  return fallback
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean)
    .map((code) => ({
      code,
      name: code,
      dataType: 'STRING' as SubTableDataType,
    }));
}

export function serializeSubTableSchema(cols: SubTableColumnDef[]): string {
  return JSON.stringify(
    cols
      .filter((c) => c.code.trim())
      .map((c) => ({
        code: c.code.trim(),
        name: c.name.trim() || c.code.trim(),
        dataType: c.dataType || 'STRING',
        ...(c.required ? { required: true } : {}),
        ...(c.formula?.trim() ? { formula: c.formula.trim() } : {}),
        ...(c.defaultValue ? { defaultValue: c.defaultValue } : {}),
        ...(c.validationRegex?.trim() ? { validationRegex: c.validationRegex.trim() } : {}),
      }))
  );
}

function evalSimpleArithmetic(expr: string, row: Record<string, unknown>): number | null {
  let trimmed = expr.trim();
  let roundDigits: number | null = null;
  const roundMatch = /^ROUND\((.+),\s*(\d+)\)$/i.exec(trimmed);
  if (roundMatch) {
    trimmed = roundMatch[1].trim();
    roundDigits = Math.min(6, Math.max(0, Number(roundMatch[2])));
  }
  const replaced = trimmed.replace(/\b([a-zA-Z_][a-zA-Z0-9_]*)\b/g, (match) => {
    const val = row[match];
    if (val === null || val === undefined || val === '') return '0';
    const num = Number(val);
    return Number.isFinite(num) ? String(num) : 'NaN';
  });
  if (!/^[0-9+\-*/().\s]+$/.test(replaced)) return null;
  try {
    const result = Function(`"use strict"; return (${replaced});`)();
    if (typeof result !== 'number' || !Number.isFinite(result)) return null;
    if (roundDigits !== null) {
      const factor = 10 ** roundDigits;
      return Math.round(result * factor) / factor;
    }
    return result;
  } catch {
    return null;
  }
}

export function evaluateSubTableRowFormulas(
  subCols: SubTableColumnDef[],
  row: Record<string, unknown>
): Record<string, unknown> {
  const next: Record<string, unknown> = { ...row };
  const formulaCols = subCols.filter((c) => Boolean(c.formula?.trim()));
  for (let pass = 0; pass < 2; pass++) {
    for (const col of formulaCols) {
      const val = evalSimpleArithmetic(col.formula!, next);
      if (val !== null) {
        next[col.code] = val;
      }
    }
  }
  return next;
}

export function validateSubTableRows(
  fieldCode: string,
  subCols: SubTableColumnDef[],
  rows: Array<Record<string, unknown>>
): Record<string, string> {
  const errors: Record<string, string> = {};
  rows.forEach((row, idx) => {
    for (const col of subCols) {
      const raw = row?.[col.code];
      const str = raw === null || raw === undefined ? '' : String(raw).trim();
      const key = `${fieldCode}[${idx}].${col.code}`;

      if (col.required && !str) {
        errors[key] = `${col.name} là bắt buộc`;
        continue;
      }
      if (!str) continue;

      if ((col.dataType === 'NUMBER' || col.dataType === 'CURRENCY') && !Number.isFinite(Number(str))) {
        errors[key] = `${col.name} phải là số`;
      } else if (col.dataType === 'DATE' && Number.isNaN(Date.parse(str))) {
        errors[key] = `${col.name} phải là ngày hợp lệ`;
      } else if (col.dataType === 'EMAIL' && !/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(str)) {
        errors[key] = `${col.name} không đúng email`;
      } else if (col.validationRegex) {
        try {
          if (!new RegExp(col.validationRegex).test(str)) {
            errors[key] = `${col.name} không khớp mẫu`;
          }
        } catch {
          // ignore invalid regex
        }
      }
    }
  });
  return errors;
}
