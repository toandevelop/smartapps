'use client';

import React from 'react';

export interface StatCardProps {
  label: string;
  value: string | number;
  subtext?: string;
  icon?: React.ReactNode;
  iconBg?: string;
  iconColor?: string;
  color?: 'blue' | 'red' | 'green' | 'yellow' | 'purple' | string;
  onClick?: () => void;
}

const COLOR_MAP: Record<string, { bg: string; fg: string }> = {
  blue: { bg: '#e8f0fe', fg: '#1a73e8' },
  red: { bg: '#fce8e6', fg: '#d93025' },
  green: { bg: '#e6f4ea', fg: '#1e8e3e' },
  yellow: { bg: '#fef7e0', fg: '#ea8600' },
  purple: { bg: '#f3e5f5', fg: '#7b1fa2' },
};

export default function StatCard({
  label,
  value,
  subtext,
  icon,
  iconBg,
  iconColor,
  color = 'blue',
  onClick,
}: StatCardProps) {
  const resolvedBg = iconBg || COLOR_MAP[color]?.bg || '#e8f0fe';
  const resolvedColor = iconColor || COLOR_MAP[color]?.fg || '#1a73e8';

  return (
    <div
      onClick={onClick}
      role={onClick ? 'button' : undefined}
      tabIndex={onClick ? 0 : undefined}
      style={{
        backgroundColor: 'var(--bg-surface)',
        border: '1px solid var(--border-subtle)',
        borderRadius: 16,
        padding: '18px 20px',
        boxShadow: 'var(--shadow-sm)',
        cursor: onClick ? 'pointer' : 'default',
        display: 'flex',
        flexDirection: 'column',
        justifyContent: 'space-between',
        transition: 'box-shadow 0.2s ease, border-color 0.2s ease',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
        <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-secondary)' }}>{label}</span>
        {icon && (
          <div
            style={{
              width: 36,
              height: 36,
              borderRadius: '50%',
              backgroundColor: resolvedBg,
              color: resolvedColor,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            {icon}
          </div>
        )}
      </div>
      <div style={{ fontSize: 26, fontWeight: 800, color: 'var(--text-primary)', letterSpacing: '-0.02em' }}>
        {value}
      </div>
      {subtext && <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4 }}>{subtext}</div>}
    </div>
  );
}
