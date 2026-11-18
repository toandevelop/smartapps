'use client';

import React, { useMemo, useState } from 'react';
import ChevronLeftRoundedIcon from '@mui/icons-material/ChevronLeftRounded';
import ChevronRightRoundedIcon from '@mui/icons-material/ChevronRightRounded';
import { AppRecord, SysColumn } from '@/lib/types';

export interface CalendarViewProps {
  dateColumn: SysColumn;
  columns: SysColumn[];
  records: AppRecord[];
  accentColor?: string;
  onSelectRecord: (record: AppRecord) => void;
}

const WEEKDAYS = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'];

export default function CalendarView({
  dateColumn,
  columns,
  records,
  accentColor = '#1a73e8',
  onSelectRecord,
}: CalendarViewProps) {
  const initialDate = useMemo(() => {
    const firstWithDate = records.find((r) => r.data?.[dateColumn.columnCode]);
    if (firstWithDate) {
      const parsed = new Date(String(firstWithDate.data[dateColumn.columnCode]));
      if (!Number.isNaN(parsed.getTime())) return parsed;
    }
    return new Date();
  }, [records, dateColumn.columnCode]);

  const [cursor, setCursor] = useState<Date>(() => new Date(initialDate.getFullYear(), initialDate.getMonth(), 1));

  const year = cursor.getFullYear();
  const month = cursor.getMonth();

  const days = useMemo(() => {
    const firstDayOfMonth = new Date(year, month, 1);
    const jsDay = firstDayOfMonth.getDay(); // 0=Sun..6=Sat
    const mondayOffset = jsDay === 0 ? 6 : jsDay - 1;
    const totalDays = new Date(year, month + 1, 0).getDate();

    const cells: Array<{ dateStr: string | null; dayNum: number | null }> = [];
    for (let i = 0; i < mondayOffset; i++) cells.push({ dateStr: null, dayNum: null });
    for (let d = 1; d <= totalDays; d++) {
      const mm = String(month + 1).padStart(2, '0');
      const dd = String(d).padStart(2, '0');
      cells.push({ dateStr: `${year}-${mm}-${dd}`, dayNum: d });
    }
    while (cells.length % 7 !== 0) cells.push({ dateStr: null, dayNum: null });
    return cells;
  }, [year, month]);

  const labelCol = columns.find((c) => !c.isIdentityKey && c.columnCode !== dateColumn.columnCode) || columns[0];

  const recordsByDate = useMemo(() => {
    const map = new Map<string, AppRecord[]>();
    for (const r of records) {
      const raw = r.data?.[dateColumn.columnCode];
      if (!raw) continue;
      const key = String(raw).substring(0, 10);
      if (!map.has(key)) map.set(key, []);
      map.get(key)!.push(r);
    }
    return map;
  }, [records, dateColumn.columnCode]);

  return (
    <div className="card" style={{ padding: 18 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
        <div>
          <strong style={{ fontSize: 15.5 }}>
            Tháng {month + 1} / {year}
          </strong>
          <span style={{ fontSize: 12.5, color: 'var(--text-muted)', marginLeft: 10 }}>
            Hiển thị theo trường: <strong>{dateColumn.columnName}</strong>
          </span>
        </div>
        <div style={{ display: 'flex', gap: 6 }}>
          <button
            type="button"
            className="btn btn-outline btn-xs"
            onClick={() => setCursor(new Date(year, month - 1, 1))}
          >
            <ChevronLeftRoundedIcon sx={{ fontSize: 17 }} />
          </button>
          <button
            type="button"
            className="btn btn-outline btn-xs"
            onClick={() => setCursor(new Date(new Date().getFullYear(), new Date().getMonth(), 1))}
          >
            Hôm nay
          </button>
          <button
            type="button"
            className="btn btn-outline btn-xs"
            onClick={() => setCursor(new Date(year, month + 1, 1))}
          >
            <ChevronRightRoundedIcon sx={{ fontSize: 17 }} />
          </button>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', border: '1px solid var(--border-subtle)', borderRadius: 12, overflow: 'hidden' }}>
        {WEEKDAYS.map((w) => (
          <div
            key={w}
            style={{
              padding: '8px 6px',
              textAlign: 'center',
              fontSize: 12,
              fontWeight: 700,
              color: 'var(--text-secondary)',
              backgroundColor: '#f8f9fa',
              borderBottom: '1px solid var(--border-subtle)',
            }}
          >
            {w}
          </div>
        ))}

        {days.map((cell, idx) => {
          const items = cell.dateStr ? recordsByDate.get(cell.dateStr) || [] : [];
          return (
            <div
              key={idx}
              style={{
                minHeight: 96,
                padding: 6,
                borderRight: (idx + 1) % 7 === 0 ? 'none' : '1px solid var(--border-light)',
                borderBottom: '1px solid var(--border-light)',
                backgroundColor: cell.dayNum ? '#ffffff' : '#fafafa',
                display: 'flex',
                flexDirection: 'column',
                gap: 4,
              }}
            >
              {cell.dayNum && (
                <div style={{ fontSize: 11.5, fontWeight: 700, color: 'var(--text-muted)' }}>{cell.dayNum}</div>
              )}
              {items.map((rec) => (
                <button
                  key={rec.id}
                  type="button"
                  onClick={() => onSelectRecord(rec)}
                  style={{
                    textAlign: 'left',
                    border: 'none',
                    borderRadius: 6,
                    padding: '3px 6px',
                    fontSize: 11.5,
                    fontWeight: 600,
                    backgroundColor: '#e8f0fe',
                    color: accentColor,
                    cursor: 'pointer',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                  }}
                  title={`${rec.recordKey || rec.id}: ${String(rec.data?.[labelCol?.columnCode || ''] ?? '')}`}
                >
                  {rec.recordKey ? `${rec.recordKey} • ` : ''}
                  {String(rec.data?.[labelCol?.columnCode || ''] ?? rec.id)}
                </button>
              ))}
            </div>
          );
        })}
      </div>
    </div>
  );
}
