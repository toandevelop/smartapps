'use client';

import React, { useMemo, useState } from 'react';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import ChevronRightRoundedIcon from '@mui/icons-material/ChevronRightRounded';
import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded';
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded';
import { FormLayoutConfig, FormLayoutSection, SysColumn } from '@/lib/types';
import { normalizeFormLayout } from '@/lib/form-layout';

interface FormLayoutRendererProps {
  layout?: FormLayoutConfig | null;
  columns: SysColumn[];
  renderField: (col: SysColumn, colSpan: number, sectionCols: number) => React.ReactNode;
  isColumnVisible?: (col: SysColumn) => boolean;
  onValidateStep?: (sectionColumns: SysColumn[]) => boolean;
}

export default function FormLayoutRenderer({
  layout,
  columns,
  renderField,
  isColumnVisible,
  onValidateStep,
}: FormLayoutRendererProps) {
  const normalized = useMemo(() => normalizeFormLayout(layout, columns), [layout, columns]);
  const colMap = useMemo(() => new Map(columns.map((c) => [c.columnCode, c])), [columns]);
  const [activeIndex, setActiveIndex] = useState(0);
  const [collapsedMap, setCollapsedMap] = useState<Record<string, boolean>>({});

  const visibleSections = useMemo(() => {
    return normalized.sections
      .map((sec) => {
        const items = sec.fields
          .map((f) => ({ field: f, col: colMap.get(f.columnCode) }))
          .filter((pair): pair is { field: typeof pair.field; col: SysColumn } => {
            if (!pair.col) return false;
            return isColumnVisible ? isColumnVisible(pair.col) : true;
          });
        return { section: sec, items };
      })
      .filter((entry) => entry.items.length > 0 || Boolean(entry.section.calloutText));
  }, [normalized.sections, colMap, isColumnVisible]);

  if (visibleSections.length === 0) return null;

  const safeIdx = Math.min(activeIndex, visibleSections.length - 1);

  const renderCallout = (sec: FormLayoutSection) => {
    if (!sec.calloutText?.trim()) return null;
    const tone = sec.calloutTone === 'warning' ? 'warning' : sec.calloutTone === 'success' ? 'success' : 'info';
    const bg = tone === 'warning' ? 'var(--warning-bg)' : tone === 'success' ? 'var(--success-bg)' : 'var(--info-bg)';
    const border = tone === 'warning' ? 'var(--warning-border)' : tone === 'success' ? 'var(--success-border)' : 'var(--info-border)';
    const color = tone === 'warning' ? 'var(--warning-text)' : tone === 'success' ? 'var(--success-text)' : 'var(--info-text)';
    return (
      <div
        style={{
          padding: '10px 12px',
          borderRadius: 10,
          backgroundColor: bg,
          border: `1px solid ${border}`,
          color,
          fontSize: 12.5,
          display: 'flex',
          alignItems: 'center',
          gap: 8,
          marginBottom: 12,
        }}
      >
        {tone === 'warning' ? (
          <WarningAmberRoundedIcon sx={{ fontSize: 17, flexShrink: 0 }} />
        ) : tone === 'success' ? (
          <CheckCircleRoundedIcon sx={{ fontSize: 17, flexShrink: 0 }} />
        ) : (
          <InfoOutlinedIcon sx={{ fontSize: 17, flexShrink: 0 }} />
        )}
        <span>{sec.calloutText}</span>
      </div>
    );
  };

  const renderSectionFields = (entry: (typeof visibleSections)[number]) => {
    const colCount = Math.max(1, Math.min(3, Number(entry.section.columns) || 2));
    return (
      <>
        {renderCallout(entry.section)}
        <div
          className={`form-layout-grid form-layout-cols-${colCount}`}
          style={{
            display: 'grid',
            gridTemplateColumns: `repeat(${colCount}, minmax(0, 1fr))`,
            gap: 14,
          }}
        >
          {entry.items.map(({ field, col }) => {
            const span = Math.max(1, Math.min(colCount, Number(field.colSpan) || 1));
            return (
              <div key={col.id} style={{ gridColumn: `span ${span} / span ${span}`, minWidth: 0 }}>
                {renderField(col, span, colCount)}
              </div>
            );
          })}
        </div>
      </>
    );
  };

  if (normalized.mode === 'tabs' && visibleSections.length > 1) {
    const current = visibleSections[safeIdx];
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
        <div className="segmented-tabs subtle" role="tablist">
          {visibleSections.map((entry, idx) => (
            <button
              key={entry.section.id}
              type="button"
              role="tab"
              aria-selected={idx === safeIdx}
              className={`segmented-tab ${idx === safeIdx ? 'active' : ''}`}
              onClick={() => setActiveIndex(idx)}
            >
              {entry.section.title}
            </button>
          ))}
        </div>
        {current.section.description && (
          <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>{current.section.description}</div>
        )}
        {renderSectionFields(current)}
      </div>
    );
  }

  if (normalized.mode === 'wizard' && visibleSections.length > 1) {
    const current = visibleSections[safeIdx];
    const goNext = () => {
      if (onValidateStep && !onValidateStep(current.items.map((x) => x.col))) return;
      setActiveIndex((i) => Math.min(visibleSections.length - 1, i + 1));
    };
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            flexWrap: 'wrap',
            padding: '10px 12px',
            borderRadius: 12,
            backgroundColor: 'var(--bg-primary)',
            border: '1px solid var(--border-light)',
          }}
        >
          {visibleSections.map((entry, idx) => {
            const done = idx < safeIdx;
            const active = idx === safeIdx;
            return (
              <button
                key={entry.section.id}
                type="button"
                onClick={() => {
                  if (idx <= safeIdx || !onValidateStep || onValidateStep(current.items.map((x) => x.col))) {
                    setActiveIndex(idx);
                  }
                }}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 6,
                  padding: '5px 10px',
                  borderRadius: 999,
                  border: `1.5px solid ${active ? 'var(--accent-primary)' : done ? 'var(--success-border)' : 'var(--border-subtle)'}`,
                  backgroundColor: active ? 'var(--accent-primary)' : done ? 'var(--success-bg)' : 'var(--bg-surface)',
                  color: active ? 'var(--accent-on-primary)' : done ? 'var(--success-text)' : 'var(--text-secondary)',
                  fontSize: 12,
                  fontWeight: 600,
                  cursor: 'pointer',
                  fontFamily: 'inherit',
                }}
              >
                <span>{done ? '✓' : idx + 1}</span>
                <span>{entry.section.title}</span>
              </button>
            );
          })}
        </div>

        <div>
          <div style={{ fontSize: 14.5, fontWeight: 700, color: 'var(--text-primary)' }}>
            Bước {safeIdx + 1}/{visibleSections.length}: {current.section.title}
          </div>
          {current.section.description && (
            <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>{current.section.description}</div>
          )}
        </div>

        {renderSectionFields(current)}

        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            paddingTop: 10,
            borderTop: '1px solid var(--border-light)',
          }}
        >
          <button
            type="button"
            className="btn btn-outline btn-xs"
            disabled={safeIdx === 0}
            onClick={() => setActiveIndex((i) => Math.max(0, i - 1))}
          >
            <ArrowBackRoundedIcon sx={{ fontSize: 14 }} /> Bước trước
          </button>
          <span style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
            Bước {safeIdx + 1} / {visibleSections.length}
          </span>
          {safeIdx < visibleSections.length - 1 ? (
            <button type="button" className="btn btn-primary btn-xs" onClick={goNext}>
              Bước tiếp theo <ArrowForwardRoundedIcon sx={{ fontSize: 14 }} />
            </button>
          ) : (
            <span className="badge badge-success" style={{ fontSize: 11 }}>
              Đã tới bước cuối — sẵn sàng lưu
            </span>
          )}
        </div>
      </div>
    );
  }

  // Default: stacked sections
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {visibleSections.map((entry, idx) => {
        const sec = entry.section;
        const isCollapsed = Boolean(sec.collapsible && collapsedMap[sec.id]);
        const showHeader = visibleSections.length > 1 || Boolean(sec.description) || Boolean(sec.collapsible);
        return (
          <div
            key={sec.id}
            style={
              showHeader
                ? {
                    border: '1px solid var(--border-light)',
                    borderRadius: 12,
                    padding: '14px 16px',
                    backgroundColor: 'var(--bg-surface)',
                  }
                : undefined
            }
          >
            {showHeader && (
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  gap: 8,
                  marginBottom: isCollapsed ? 0 : 12,
                  paddingBottom: isCollapsed ? 0 : 8,
                  borderBottom: isCollapsed ? 'none' : '1px solid var(--border-light)',
                  cursor: sec.collapsible ? 'pointer' : 'default',
                }}
                onClick={() => {
                  if (sec.collapsible) {
                    setCollapsedMap((prev) => ({ ...prev, [sec.id]: !prev[sec.id] }));
                  }
                }}
              >
                <div>
                  <div style={{ fontSize: 13.5, fontWeight: 700, color: 'var(--text-primary)' }}>
                    {visibleSections.length > 1 ? `${idx + 1}. ` : ''}
                    {sec.title}
                  </div>
                  {sec.description && (
                    <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 2 }}>{sec.description}</div>
                  )}
                </div>
                {sec.collapsible && (
                  <span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}>
                    {isCollapsed ? <ChevronRightRoundedIcon sx={{ fontSize: 18 }} /> : <ExpandMoreRoundedIcon sx={{ fontSize: 18 }} />}
                  </span>
                )}
              </div>
            )}
            {!isCollapsed && renderSectionFields(entry)}
          </div>
        );
      })}
    </div>
  );
}
