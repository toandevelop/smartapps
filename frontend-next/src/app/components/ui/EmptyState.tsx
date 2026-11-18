'use client';

import React from 'react';
import InboxRoundedIcon from '@mui/icons-material/InboxRounded';

export interface EmptyStateProps {
  title: string;
  description?: string;
  icon?: React.ReactNode;
  action?: React.ReactNode;
  compact?: boolean;
}

export default function EmptyState({
  title,
  description,
  icon,
  action,
  compact = false,
}: EmptyStateProps) {
  return (
    <div
      style={{
        padding: compact ? '28px 16px' : '44px 24px',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        textAlign: 'center',
        backgroundColor: 'var(--bg-surface)',
        borderRadius: 16,
        border: '1px dashed var(--border-subtle)',
      }}
    >
      <div
        style={{
          width: compact ? 42 : 54,
          height: compact ? 42 : 54,
          borderRadius: '50%',
          backgroundColor: 'var(--bg-secondary)',
          color: 'var(--text-muted)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          marginBottom: compact ? 10 : 14,
        }}
      >
        {icon || <InboxRoundedIcon sx={{ fontSize: compact ? 22 : 28 }} />}
      </div>
      <h3 style={{ fontSize: compact ? 14 : 16, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 4 }}>
        {title}
      </h3>
      {description && (
        <p style={{ fontSize: compact ? 12 : 13, color: 'var(--text-muted)', maxWidth: 420, marginBottom: action ? 14 : 0 }}>
          {description}
        </p>
      )}
      {action && <div>{action}</div>}
    </div>
  );
}
