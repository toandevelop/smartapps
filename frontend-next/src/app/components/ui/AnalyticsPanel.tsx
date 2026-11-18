'use client';

import React, { useEffect, useState } from 'react';
import { TableAnalytics } from '@/lib/types';
import { formatCurrencyVnd } from '@/lib/column-utils';
import BarChartRoundedIcon from '@mui/icons-material/BarChartRounded';
import FunctionsRoundedIcon from '@mui/icons-material/FunctionsRounded';

export default function AnalyticsPanel({ tableId }: { tableId: string }) {
  const [stats, setStats] = useState<TableAnalytics | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!tableId) return;
    setLoading(true);
    fetch(`/api/records/analytics?tableId=${encodeURIComponent(tableId)}`)
      .then((r) => r.json())
      .then((res) => {
        if (res.success && res.data) setStats(res.data);
      })
      .finally(() => setLoading(false));
  }, [tableId]);

  if (loading) {
    return (
      <div className="card" style={{ textAlign: 'center', padding: 40, color: 'var(--text-muted)' }}>
        Đang tổng hợp phân tích dữ liệu động...
      </div>
    );
  }

  if (!stats) return null;

  const ab = stats.approvalBreakdown || {};
  const hasApprovalStats = Object.values(ab).some((n) => n > 0);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
      {hasApprovalStats && (
        <div className="card" style={{ padding: '12px 18px', display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
          <strong style={{ fontSize: 13.5, color: 'var(--text-secondary)' }}>Tiến độ Phê duyệt:</strong>
          <span className="badge badge-info">Nháp: {ab.DRAFT || 0}</span>
          <span className="badge badge-warning">Chờ duyệt: {ab.PENDING || 0}</span>
          <span className="badge badge-success">Đã duyệt: {ab.APPROVED || 0}</span>
          <span className="badge badge-danger">Từ chối: {ab.REJECTED || 0}</span>
        </div>
      )}
      {stats.numericSummaries.length > 0 && (
        <div className="stats-grid">
          {stats.numericSummaries.map((num) => {
            const fmt = (v: number) =>
              num.dataType.toUpperCase() === 'CURRENCY'
                ? formatCurrencyVnd(v)
                : new Intl.NumberFormat('vi-VN').format(v);
            return (
              <div key={num.columnCode} className="card" style={{ padding: 18 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
                  <span style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-secondary)' }}>{num.columnName}</span>
                  <FunctionsRoundedIcon sx={{ fontSize: 18, color: '#1a73e8' }} />
                </div>
                <div style={{ fontSize: 22, fontWeight: 800, color: 'var(--text-primary)', marginBottom: 8 }}>
                  Tổng: {fmt(num.sum)}
                </div>
                <div style={{ fontSize: 12, color: 'var(--text-muted)', display: 'flex', justifyContent: 'space-between' }}>
                  <span>TB: <strong>{fmt(num.avg)}</strong></span>
                  <span>Min: {fmt(num.min)} • Max: {fmt(num.max)}</span>
                </div>
              </div>
            );
          })}
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: 16 }}>
        {stats.distributions.map((dist) => {
          const maxCount = Math.max(...dist.items.map((i) => i.count), 1);
          return (
            <div key={dist.columnCode} className="card" style={{ padding: 18 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 14 }}>
                <BarChartRoundedIcon sx={{ fontSize: 20, color: '#1a73e8' }} />
                <strong style={{ fontSize: 14.5 }}>Phân bổ theo {dist.columnName}</strong>
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                {dist.items.map((item) => {
                  const pct = Math.round((item.count / Math.max(stats.totalRecords, 1)) * 100);
                  const barW = Math.round((item.count / maxCount) * 100);
                  return (
                    <div key={item.code}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12.5, marginBottom: 4 }}>
                        <span style={{ fontWeight: 600 }}>{item.label}</span>
                        <span style={{ color: 'var(--text-muted)' }}>
                          <strong>{item.count}</strong> ({pct}%)
                        </span>
                      </div>
                      <div style={{ width: '100%', height: 9, borderRadius: 999, backgroundColor: 'var(--bg-secondary)', overflow: 'hidden' }}>
                        <div
                          style={{
                            width: `${barW}%`,
                            height: '100%',
                            borderRadius: 999,
                            backgroundColor: item.color || '#1a73e8',
                            transition: 'width 0.3s ease',
                          }}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
