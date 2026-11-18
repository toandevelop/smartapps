'use client';

import React from 'react';

export interface PageHeaderProps {
  title: string | React.ReactNode;
  subtitle?: React.ReactNode;
  icon?: React.ReactNode;
  badge?: React.ReactNode;
  actions?: React.ReactNode;
  breadcrumbs?: Array<{ label: string; href?: string }>;
  accentColor?: string;
}

export default function PageHeader({
  title,
  subtitle,
  icon,
  badge,
  actions,
  breadcrumbs,
  accentColor = 'var(--accent-primary)',
}: PageHeaderProps) {
  return (
    <header className="top-bar">
      <div>
        {breadcrumbs && breadcrumbs.length > 0 && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 11.5, color: 'var(--text-muted)', marginBottom: 4 }}>
            {breadcrumbs.map((b, i) => (
              <React.Fragment key={i}>
                {i > 0 && <span>/</span>}
                {b.href ? (
                  <a href={b.href} style={{ color: accentColor, textDecoration: 'none', fontWeight: 500 }}>
                    {b.label}
                  </a>
                ) : (
                  <span>{b.label}</span>
                )}
              </React.Fragment>
            ))}
          </div>
        )}
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          {icon && (
            <div
              style={{
                width: 42,
                height: 42,
                borderRadius: 12,
                backgroundColor: 'rgba(26, 115, 232, 0.1)',
                color: accentColor,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
              }}
            >
              {icon}
            </div>
          )}
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <h1 style={{ margin: 0, fontSize: 19, fontWeight: 700, color: 'var(--text-primary)' }}>
                {title}
              </h1>
              {badge}
            </div>
            {subtitle && (
              <div style={{ marginTop: 2, fontSize: 12.5, color: 'var(--text-muted)' }}>
                {subtitle}
              </div>
            )}
          </div>
        </div>
      </div>

      {actions && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          {actions}
        </div>
      )}
    </header>
  );
}
