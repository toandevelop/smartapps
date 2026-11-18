import type { FormCondition, FormRule, SysColumn } from './types';

export const EMPTY_FORM_RULES: FormRule[] = [];
export const EMPTY_FORM_DEFAULTS: Record<string, unknown> = {};
export interface FormFieldState { visible: boolean; required: boolean; readOnly: boolean }

const text = (value: unknown): string => value == null ? '' : typeof value === 'object' ? JSON.stringify(value) : String(value);
const empty = (value: unknown) => text(value).trim() === '';

// Compare decimal strings without losing precision beyond JavaScript's safe integer range.
function decimal(value: unknown): { digits: bigint; scale: number } | null {
  const raw = text(value).trim();
  if (raw.length > 500 || typeof value === 'boolean') return null;
  const match = /^([+-]?)(?:(\d+)(?:\.(\d*))?|\.(\d+))(?:[eE]([+-]?\d+))?$/.exec(raw);
  if (!match) return null;
  const exponent = Number(match[5] || 0);
  if (!Number.isInteger(exponent) || Math.abs(exponent) > 100) return null;
  const fraction = match[3] ?? match[4] ?? '';
  return { digits: BigInt((match[1] === '-' ? '-' : '') + (match[2] || '0') + fraction), scale: fraction.length - exponent };
}
const boolean = (value: unknown) => {
  const raw = text(value).toLowerCase();
  return raw === 'true' || raw === '1' ? true : raw === 'false' || raw === '0' ? false : null;
};
function date(value: unknown) {
  const raw = text(value);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(raw) || Number(raw.slice(0, 4)) < 1) return null;
  const parsed = new Date(`${raw}T00:00:00Z`);
  return Number.isFinite(parsed.getTime()) && parsed.toISOString().slice(0, 10) === raw ? raw : null;
}

export function matchesFormCondition(condition: FormCondition, dataType: string, value: unknown): boolean {
  if (condition.operator === 'IS_EMPTY') return empty(value);
  if (condition.operator === 'NOT_EMPTY') return !empty(value);
  let comparison: number;
  const type = dataType.toUpperCase();
  if (type === 'NUMBER' || type === 'CURRENCY') {
    const a = decimal(value), b = decimal(condition.value);
    if (!a || !b) return false;
    const scale = Math.max(a.scale, b.scale);
    const left = a.digits * BigInt(10) ** BigInt(scale - a.scale), right = b.digits * BigInt(10) ** BigInt(scale - b.scale);
    comparison = left < right ? -1 : left > right ? 1 : 0;
  } else if (type === 'BOOLEAN') {
    const a = boolean(value), b = boolean(condition.value);
    if (a == null || b == null) return false;
    comparison = Number(a) - Number(b);
  } else if (type === 'DATE') {
    const a = date(value), b = date(condition.value);
    if (!a || !b) return false;
    comparison = a < b ? -1 : a > b ? 1 : 0;
  } else {
    const a = text(value).toUpperCase(), b = text(condition.value).toUpperCase();
    comparison = a < b ? -1 : a > b ? 1 : 0;
  }
  switch (condition.operator) {
    case 'EQUALS': return comparison === 0;
    case 'NOT_EQUALS': return comparison !== 0;
    case 'CONTAINS': return text(value).toUpperCase().includes(text(condition.value).toUpperCase());
    case 'GREATER_THAN': return comparison > 0;
    case 'GREATER_OR_EQUAL': return comparison >= 0;
    case 'LESS_THAN': return comparison < 0;
    case 'LESS_OR_EQUAL': return comparison <= 0;
    default: return false;
  }
}

export function evaluateFormRules(columns: SysColumn[], rules: FormRule[], data: Record<string, unknown>): Record<string, FormFieldState> {
  const states = Object.fromEntries(columns.map(c => [c.columnCode, { visible: c.showInForm !== false, required: c.isRequired, readOnly: false }]));
  const byCode = new Map(columns.map(c => [c.columnCode, c]));
  for (const rule of rules) {
    const state = states[rule.targetField];
    if (!state) continue;
    const check = (condition: FormCondition) => {
      const column = byCode.get(condition.field);
      return Boolean(column && matchesFormCondition(condition, column.dataType, data[condition.field]));
    };
    const matches = rule.match === 'ANY' ? rule.conditions.some(check) : rule.conditions.every(check);
    const value = matches ? rule.value : rule.elseValue;
    if (value == null) continue;
    state[rule.property] = rule.property === 'visible' ? byCode.get(rule.targetField)?.showInForm !== false && value : value;
  }
  return states;
}

export function prepareFormSubmission(columns: SysColumn[], rules: FormRule[], data: Record<string, unknown>, original?: Record<string, unknown> | null, creationDefaults: Record<string, unknown> = EMPTY_FORM_DEFAULTS) {
  if (!rules.length) return { ...data };
  data = restoreProtectedFormValues(columns, rules, data, original, creationDefaults);
  const states = evaluateFormRules(columns, rules, data);
  const oldStates = original ? evaluateFormRules(columns, rules, original) : {};
  return Object.fromEntries(Object.entries(data).filter(([field]) => !states[field]
    || (states[field].visible && !states[field].readOnly && !oldStates[field]?.readOnly)));
}

export function restoreProtectedFormValues(columns: SysColumn[], rules: FormRule[], draft: Record<string, unknown>, original?: Record<string, unknown> | null, creationDefaults: Record<string, unknown> = EMPTY_FORM_DEFAULTS) {
  if (!rules.length) return draft;
  const baseline = original || { ...formDefaultValues(columns), ...creationDefaults };
  const oldStates = original ? evaluateFormRules(columns, rules, original) : {};
  let data = draft;
  // Each changed value only moves to its fixed stored/default value, so this converges.
  for (let pass = 0; pass <= columns.length; pass++) {
    const states = evaluateFormRules(columns, rules, data);
    let changed = false;
    for (const column of columns) {
      const field = column.columnCode;
      const hidden = !states[field].visible && rules.some(r => r.targetField === field && r.property === 'visible');
      if (!hidden && !states[field].readOnly && !oldStates[field]?.readOnly) continue;
      if (JSON.stringify(data[field]) === JSON.stringify(baseline[field])) continue;
      if (data === draft) data = { ...draft };
      if (baseline[field] === undefined) delete data[field]; else data[field] = baseline[field];
      changed = true;
    }
    if (!changed) break;
  }
  return data;
}

export function formDefaultValues(columns: SysColumn[]): Record<string, unknown> {
  return Object.fromEntries(columns.filter(c => c.defaultValue?.trim() && !c.formula?.trim() && !['AUTO_NUMBER', 'SUB_TABLE'].includes(c.dataType.toUpperCase()))
    .map(c => {
      const raw = c.defaultValue!.trim();
      const type = c.dataType.toUpperCase();
      let value: unknown = raw;
      // Keep defaults consistent with the backend's invariant ImportValueNormalizer.
      if (type === 'BOOLEAN') {
        const normalized = raw.toLowerCase();
        if (['true', '1', 'yes', 'có', 'co'].includes(normalized)) value = true;
        else if (['false', '0', 'no', 'không', 'khong'].includes(normalized)) value = false;
      } else if (['NUMBER', 'CURRENCY'].includes(type) && /^[+-]?(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?$/.test(raw)) {
        const canonical = raw.replace(/,/g, '');
        const parsed = Number(canonical);
        value = Math.abs(parsed) <= Number.MAX_SAFE_INTEGER ? parsed : canonical;
      }
      return [c.columnCode, value];
    }));
}

export function requiredFormErrors(columns: SysColumn[], rules: FormRule[], data: Record<string, unknown>, original?: Record<string, unknown> | null, creationDefaults: Record<string, unknown> = EMPTY_FORM_DEFAULTS) {
  data = restoreProtectedFormValues(columns, rules, data, original, creationDefaults);
  const states = evaluateFormRules(columns, rules, data);
  // Computed and auto-number values are supplied by the backend, not by the user.
  return columns.filter(c => !c.formula && c.dataType !== 'AUTO_NUMBER' && states[c.columnCode].visible && states[c.columnCode].required && empty(data[c.columnCode]))
    .map(c => ({ field: c.columnCode, message: `Trường '${c.columnName}' là bắt buộc theo biểu mẫu.` }));
}
