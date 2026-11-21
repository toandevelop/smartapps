import type { FormLayoutConfig, FormLayoutSection, SysColumn } from './types';

export function buildDefaultFormLayout(columns: SysColumn[]): FormLayoutConfig {
  const formCols = columns.filter((c) => c.showInForm !== false);
  return {
    mode: 'sections',
    sections: [
      {
        id: 'sec_main',
        title: 'Thông tin chính',
        description: '',
        columns: 2,
        collapsible: false,
        calloutText: '',
        calloutTone: 'info',
        fields: formCols.map((c) => ({
          columnCode: c.columnCode,
          colSpan: c.dataType === 'SUB_TABLE' || c.dataType === 'TEXTAREA' ? 2 : 1,
        })),
      },
    ],
  };
}

/**
 * Normalizes a saved FormLayoutConfig against the current column list:
 * - Drops deleted/non-form columns
 * - Deduplicates field entries across sections
 * - Appends any newly added columns into the last section so no field is ever lost
 */
export function normalizeFormLayout(
  layout: FormLayoutConfig | null | undefined,
  columns: SysColumn[],
): FormLayoutConfig {
  const formCols = columns.filter((c) => c.showInForm !== false);
  const validByCode = new Map(formCols.map((c) => [c.columnCode, c]));

  if (!layout || !Array.isArray(layout.sections) || layout.sections.length === 0) {
    return buildDefaultFormLayout(formCols);
  }

  const mode = layout.mode === 'tabs' || layout.mode === 'wizard' ? layout.mode : 'sections';
  const placed = new Set<string>();

  const cleanedSections: FormLayoutSection[] = layout.sections.map((sec, idx) => {
    const colCount = Math.max(1, Math.min(3, Number(sec.columns) || 2));
    const fields = (sec.fields || [])
      .filter((f) => f && validByCode.has(f.columnCode) && !placed.has(f.columnCode))
      .map((f) => {
        placed.add(f.columnCode);
        return {
          columnCode: f.columnCode,
          colSpan: Math.max(1, Math.min(colCount, Number(f.colSpan) || 1)),
        };
      });

    return {
      id: (sec.id || '').trim() || `sec_${idx + 1}`,
      title: (sec.title || '').trim() || `Phần ${idx + 1}`,
      description: sec.description || '',
      columns: colCount,
      collapsible: Boolean(sec.collapsible),
      calloutText: sec.calloutText || '',
      calloutTone: sec.calloutTone === 'warning' || sec.calloutTone === 'success' ? sec.calloutTone : 'info',
      fields,
    };
  });

  // Append newly added columns not yet placed in any section
  const missingCols = formCols.filter((c) => !placed.has(c.columnCode));
  if (missingCols.length > 0) {
    const target = cleanedSections[cleanedSections.length - 1];
    const maxSpan = Math.max(1, Math.min(3, Number(target.columns) || 2));
    for (const c of missingCols) {
      target.fields.push({
        columnCode: c.columnCode,
        colSpan: c.dataType === 'SUB_TABLE' || c.dataType === 'TEXTAREA' ? maxSpan : 1,
      });
    }
  }

  return { mode, sections: cleanedSections };
}

export function validateFormLayout(
  layout: FormLayoutConfig | null | undefined,
  columns: SysColumn[],
): string[] {
  const errors: string[] = [];
  if (!layout) return errors;
  if (!Array.isArray(layout.sections) || layout.sections.length === 0) {
    errors.push('Bố cục form cần ít nhất 1 phần (Section).');
    return errors;
  }
  const seen = new Set<string>();
  const validCodes = new Set(columns.map((c) => c.columnCode));
  layout.sections.forEach((sec, idx) => {
    if (!sec.title?.trim()) errors.push(`Phần ${idx + 1} chưa có tiêu đề.`);
    (sec.fields || []).forEach((f) => {
      if (!validCodes.has(f.columnCode)) {
        errors.push(`Trường '${f.columnCode}' trong '${sec.title}' không tồn tại trong bảng.`);
      } else if (seen.has(f.columnCode)) {
        errors.push(`Trường '${f.columnCode}' bị lặp ở nhiều phần.`);
      }
      seen.add(f.columnCode);
    });
  });
  return errors;
}
