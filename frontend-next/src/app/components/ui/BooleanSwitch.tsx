'use client';

import React from 'react';

export function normalizeBoolVal(raw: unknown): boolean | null {
  if (raw === true || raw === 1) return true;
  if (raw === false || raw === 0) return false;
  if (typeof raw === 'string') {
    const s = raw.trim().toLowerCase();
    if (['true', '1', 'có', 'co', 'đúng', 'yes'].includes(s)) return true;
    if (['false', '0', 'không', 'khong', 'sai', 'no'].includes(s)) return false;
  }
  return null;
}

export function resolveDefaultBoolVal(defaultValue?: string | null): boolean {
  const parsed = normalizeBoolVal(defaultValue);
  return parsed !== null ? parsed : false;
}

interface BooleanSwitchProps {
  id?: string;
  checked: boolean;
  onChange: (nextVal: boolean) => void;
  disabled?: boolean;
  size?: 'sm' | 'md';
  trueLabel?: string;
  falseLabel?: string;
}

export default function BooleanSwitch({
  id,
  checked,
  onChange,
  disabled = false,
  size = 'md',
  trueLabel,
  falseLabel,
}: BooleanSwitchProps) {
  const isSm = size === 'sm';
  const resolvedTrue = trueLabel ?? (isSm ? 'Có' : 'Có / Đúng');
  const resolvedFalse = falseLabel ?? (isSm ? 'Không' : 'Không / Sai');

  return (
    <button
      id={id}
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      onClick={() => !disabled && onChange(!checked)}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 8,
        padding: isSm ? '3px 8px 3px 4px' : '6px 12px',
        width: isSm ? 'auto' : '100%',
        borderRadius: isSm ? 999 : 8,
        border: checked ? '1px solid var(--success-border)' : '1px solid var(--border-subtle)',
        backgroundColor: checked ? 'var(--success-bg)' : 'var(--bg-surface)',
        cursor: disabled ? 'not-allowed' : 'pointer',
        opacity: disabled ? 0.65 : 1,
      }}
    >
      <span style={{ fontSize: isSm ? 11.5 : 13, fontWeight: 600, color: checked ? 'var(--success-text)' : 'var(--text-secondary)' }}>
        {checked ? resolvedTrue : resolvedFalse}
      </span>
      <span
        style={{
          width: isSm ? 30 : 36,
          height: isSm ? 16 : 20,
          borderRadius: 999,
          backgroundColor: checked ? '#1e8e3e' : '#cbd5e1',
          position: 'relative',
          transition: 'background-color 0.16s ease',
          flexShrink: 0,
        }}
      >
        <span
          style={{
            position: 'absolute',
            top: 2,
            left: checked ? (isSm ? 16 : 18) : 2,
            width: isSm ? 12 : 16,
            height: isSm ? 12 : 16,
            borderRadius: '50%',
            backgroundColor: '#ffffff',
            transition: 'left 0.16s ease',
            boxShadow: '0 1px 2px rgba(0,0,0,0.2)',
          }}
        />
      </span>
    </button>
  );
}
