'use client';

import React, { useMemo, useState } from 'react';
import TimelineRoundedIcon from '@mui/icons-material/TimelineRounded';
import { AppRecord, SysColumn } from '@/lib/types';
import StatusBadge from './StatusBadge';
import EmptyState from './EmptyState';

interface TimelineGanttViewProps {
  records: AppRecord[];
  columns: SysColumn[];
  accentColor?: string;
  onSelectRecord: (rec: AppRecord) => void;
}

type ZoomDays = 14 | 30 | 90;

function parseDateValue(raw: unknown): Date | null {
  if (!raw) return null;
  const s = String(raw).trim().slice(0, 10);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(s)) return null;
  const d = new Date(`${s}T00:00:00`);
  return Number.isNaN(d.getTime()) ? null : d;
}

export default function TimelineGanttView({
  records,
  columns,
  accentColor = 'var(--accent-primary)',
  onSelectRecord,
}: TimelineGanttViewProps) {
  const dateCols = useMemo(
    () => columns.filter((c) => (c.dataType || '').toUpperCase() === 'DATE'),
    [columns],
  );
  const titleCol = useMemo(
    () =>
      columns.find((c) => c.showInTable !== false && !c.isIdentityKey && (c.dataType === 'STRING' || c.dataType === 'TEXTAREA'))
      || columns.find((c) => c.showInTable !== false),
    [columns],
  );

  const [startColCode, setStartColCode] = useState<string>(() => dateCols[0]?.columnCode || '');
  const [endColCode, setEndColCode] = useState<string>(() => dateCols[1]?.columnCode || dateCols[0]?.columnCode || '');
  const [zoomDays, setZoomDays] = useState<ZoomDays>(30);

  const effectiveStartCol = dateCols.some((c) => c.columnCode === startColCode)
    ? startColCode
    : dateCols[0]?.columnCode || '';
  const effectiveEndCol = dateCols.some((c) => c.columnCode === endColCode)
    ? endColCode
    : dateCols[1]?.columnCode || effectiveStartCol;

  const items = useMemo(() => {
    return records
      .map((rec) => {
        const sDate = parseDateValue(rec.data?.[effectiveStartCol]);
        const eDate = parseDateValue(rec.data?.[effectiveEndCol]) || sDate;
        if (!sDate || !eDate) return null;
        const start = sDate <= eDate ? sDate : eDate;
        const end = eDate >= sDate ? eDate : sDate;
        const titleText = titleCol ? String(rec.data?.[titleCol.columnCode] || '').trim() : '';
        const label =
          rec.recordKey && titleText && titleText !== rec.recordKey
            ? `${rec.recordKey} • ${titleText}`
            : titleText || rec.recordKey || rec.id;
        return { rec, start, end, label };
      })
      .filter((x): x is NonNullable<typeof x> => Boolean(x))
      .sort((a, b) => a.start.getTime() - b.start.getTime());
  }, [records, effectiveStartCol, effectiveEndCol, titleCol]);

  const { windowStart, totalDays, ticks, todayPct } = useMemo(() => {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const minDate = items.length > 0 ? new Date(items[0].start.getTime()) : new Date(today.getTime());
    minDate.setDate(minDate.getDate() - 2);
    const span = Math.max(zoomDays, 14);
    const tickStep = span <= 14 ? 2 : span <= 30 ? 5 : 15;
    const tickArr: Array<{ label: string; pct: number }> = [];
    for (let d = 0; d <= span; d += tickStep) {
      const dt = new Date(minDate.getTime());
      dt.setDate(dt.getDate() + d);
      tickArr.push({
        label: `${String(dt.getDate()).padStart(2, '0')}/${String(dt.getMonth() + 1).padStart(2, '0')}`,
        pct: (d / span) * 100,
      });
    }
    const diffToday = (today.getTime() - minDate.getTime()) / 86400000;
    const tPct = diffToday >= 0 && diffToday <= span ? (diffToday / span) * 100 : null;
    return { windowStart: minDate, totalDays: span, ticks: tickArr, todayPct: tPct };
  }, [items, zoomDays]);

  if (dateCols.length === 0) {
    return (
      <div className="card" style={{ padding: 28 }}>
        <EmptyState
          title="Bảng chưa có cột Ngày tháng (DATE)"
          description="Thêm ít nhất 1 cột kiểu DATE trong Studio để kích hoạt chế độ Tiến độ (Timeline / Gantt)."
          icon={<TimelineRoundedIcon sx={{ fontSize: 28, color: 'var(--text-muted)' }} />}
        />
      </div>
    );
  }

  return (
    <div className="card" style={{ padding: 16, display: 'flex', flexDirection: 'column', gap: 14 }}>
      {/* Controls */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <strong style={{ fontSize: 13.5, display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <TimelineRoundedIcon sx={{ fontSize: 18, color: 'var(--accent-primary)' }} />
            Tiến độ Timeline ({items.length} mốc)
          </strong>
          <select
            className="form-select"
            style={{ width: 155, height: 30, fontSize: 12 }}
            aria-label="Cột ngày bắt đầu"
            value={effectiveStartCol}
            onChange={(e) => setStartColCode(e.target.value)}
          >
            {dateCols.map((c) => (
              <option key={c.id} value={c.columnCode}>Bắt đầu: {c.columnName}</option>
            ))}
          </select>
          <select
            className="form-select"
            style={{ width: 155, height: 30, fontSize: 12 }}
            aria-label="Cột ngày kết thúc"
            value={effectiveEndCol}
            onChange={(e) => setEndColCode(e.target.value)}
          >
            {dateCols.map((c) => (
              <option key={c.id} value={c.columnCode}>Kết thúc: {c.columnName}</option>
            ))}
          </select>
        </div>

        <div className="segmented-tabs subtle">
          {(
            [
              { days: 14, label: '2 tuần' },
              { days: 30, label: '1 tháng' },
              { days: 90, label: '1 quý' },
            ] as const
          ).map((z) => (
            <button
              key={z.days}
              type="button"
              className={`segmented-tab ${zoomDays === z.days ? 'active' : ''}`}
              onClick={() => setZoomDays(z.days)}
            >
              {z.label}
            </button>
          ))}
        </div>
      </div>

      {items.length === 0 ? (
        <div style={{ padding: 24, textAlign: 'center', fontSize: 12.5, color: 'var(--text-muted)' }}>
          Chưa có bản ghi nào có giá trị ngày hợp lệ ở cột đang chọn.
        </div>
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <div style={{ minWidth: 680 }}>
            {/* Header Ticks */}
            <div style={{ display: 'grid', gridTemplateColumns: '240px 1fr', borderBottom: '1px solid var(--border-subtle)', paddingBottom: 8 }}>
              <div style={{ fontSize: 11.5, fontWeight: 700, color: 'var(--text-muted)', textTransform: 'uppercase' }}>
                Hồ sơ / Công việc
              </div>
              <div style={{ position: 'relative', height: 20 }}>
                {ticks.map((tk, i) => (
                  <span
                    key={i}
                    style={{
                      position: 'absolute',
                      left: `${tk.pct}%`,
                      transform: 'translateX(-50%)',
                      fontSize: 11,
                      color: 'var(--text-muted)',
                      whiteSpace: 'nowrap',
                    }}
                  >
                    {tk.label}
                  </span>
                ))}
              </div>
            </div>

            {/* Gantt Rows */}
            <div style={{ display: 'flex', flexDirection: 'column' }}>
              {items.map(({ rec, start, end, label }) => {
                const startOffset = (start.getTime() - windowStart.getTime()) / 86400000;
                const durationDays = Math.max(1, Math.round((end.getTime() - start.getTime()) / 86400000) + 1);
                const leftPct = Math.max(0, Math.min(96, (startOffset / totalDays) * 100));
                const widthPct = Math.max(4, Math.min(100 - leftPct, (durationDays / totalDays) * 100));

                const statusKey = (rec.approvalStatus || rec.status || '').toUpperCase();
                const barColor =
                  statusKey === 'APPROVED'
                    ? 'var(--success)'
                    : statusKey === 'REJECTED' || statusKey === 'ERROR'
                    ? 'var(--danger)'
                    : statusKey === 'PENDING'
                    ? 'var(--warning)'
                    : accentColor;

                return (
                  <div
                    key={rec.id}
                    style={{
                      display: 'grid',
                      gridTemplateColumns: '240px 1fr',
                      alignItems: 'center',
                      borderBottom: '1px solid var(--border-light)',
                      minHeight: 42,
                    }}
                  >
                    <button
                      type="button"
                      onClick={() => onSelectRecord(rec)}
                      style={{
                        border: 'none',
                        background: 'transparent',
                        textAlign: 'left',
                        padding: '6px 10px 6px 0',
                        cursor: 'pointer',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        gap: 6,
                        color: 'var(--text-primary)',
                        fontFamily: 'inherit',
                      }}
                    >
                      <span
                        style={{
                          fontSize: 12.5,
                          fontWeight: 600,
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap',
                        }}
                        title={label}
                      >
                        {label}
                      </span>
                      <StatusBadge status={rec.approvalStatus || rec.status} size="sm" />
                    </button>

                    <div style={{ position: 'relative', height: 34, backgroundColor: 'var(--bg-primary)', borderRadius: 6 }}>
                      {todayPct !== null && (
                        <div
                          title="Hôm nay"
                          style={{
                            position: 'absolute',
                            left: `${todayPct}%`,
                            top: 0,
                            bottom: 0,
                            width: 2,
                            backgroundColor: 'var(--danger)',
                            opacity: 0.7,
                            zIndex: 1,
                          }}
                        />
                      )}
                      <button
                        type="button"
                        onClick={() => onSelectRecord(rec)}
                        title={`${label} (${start.toLocaleDateString('vi-VN')} → ${end.toLocaleDateString('vi-VN')})`}
                        style={{
                          position: 'absolute',
                          left: `${leftPct}%`,
                          width: `${widthPct}%`,
                          top: 5,
                          height: 24,
                          borderRadius: 6,
                          border: 'none',
                          backgroundColor: barColor,
                          color: '#ffffff',
                          fontSize: 11,
                          fontWeight: 600,
                          padding: '0 8px',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap',
                          cursor: 'pointer',
                          zIndex: 2,
                          textAlign: 'left',
                          boxShadow: 'var(--shadow-sm)',
                        }}
                      >
                        {durationDays} ngày • {label}
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
