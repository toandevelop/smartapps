'use client';

import React, { useEffect, useState, useCallback } from 'react';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded';
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded';
import VisibilityRoundedIcon from '@mui/icons-material/VisibilityRounded';
import VisibilityOffRoundedIcon from '@mui/icons-material/VisibilityOffRounded';
import DonutLargeRoundedIcon from '@mui/icons-material/DonutLargeRounded';
import BarChartRoundedIcon from '@mui/icons-material/BarChartRounded';
import FormatListBulletedRoundedIcon from '@mui/icons-material/FormatListBulletedRounded';
import FunctionsRoundedIcon from '@mui/icons-material/FunctionsRounded';
import CheckRoundedIcon from '@mui/icons-material/CheckRounded';
import ShowChartRoundedIcon from '@mui/icons-material/ShowChartRounded';
import { AppRuntimeTree, AppWidget } from '@/lib/types';
import WidgetConfigModal from './WidgetConfigModal';
import StatusBadge from './StatusBadge';
import EmptyState from './EmptyState';

export interface AppDashboardViewProps {
  appTree: AppRuntimeTree;
  canCustomize: boolean;
  personalMode?: boolean;
  onJumpToTable: (tableId: string, searchHint?: string) => void;
}

function LineChartSvg({
  items,
  color,
}: {
  items: Array<{ code: string; label: string; color: string; count: number }>;
  color: string;
}) {
  if (items.length === 0) {
    return <div style={{ color: 'var(--text-muted)', fontSize: 12.5, padding: 20 }}>Chưa có dữ liệu theo thời gian</div>;
  }
  const maxVal = Math.max(...items.map((i) => i.count), 1);
  const w = 420;
  const h = 135;
  const padX = 24;
  const padY = 18;
  const stepX = items.length > 1 ? (w - padX * 2) / (items.length - 1) : 0;

  const points = items.map((it, idx) => {
    const x = items.length === 1 ? w / 2 : padX + idx * stepX;
    const y = h - padY - ((it.count / maxVal) * (h - padY * 2));
    return { x, y, ...it };
  });

  const polyPoints = points.map((p) => `${p.x},${p.y}`).join(' ');
  const areaPath =
    points.length > 1
      ? `M ${points[0].x},${h - padY} L ${points.map((p) => `${p.x},${p.y}`).join(' L ')} L ${points[points.length - 1].x},${h - padY} Z`
      : '';

  return (
    <div style={{ width: '100%' }}>
      <svg viewBox={`0 0 ${w} ${h}`} style={{ width: '100%', height: 140, display: 'block' }}>
        <line x1={padX} y1={h - padY} x2={w - padX} y2={h - padY} stroke="#e8eaed" strokeWidth="1" />
        {areaPath && <path d={areaPath} fill={`${color}1a`} />}
        {points.length > 1 && <polyline fill="none" stroke={color} strokeWidth="2.5" points={polyPoints} />}
        {points.map((p) => (
          <g key={p.code}>
            <circle cx={p.x} cy={p.y} r="4.5" fill="#ffffff" stroke={color} strokeWidth="2.5" />
            <text x={p.x} y={p.y - 8} textAnchor="middle" fontSize="10" fontWeight="700" fill="#202124">
              {p.count}
            </text>
            <text x={p.x} y={h - 3} textAnchor="middle" fontSize="9.5" fill="#5f6368">
              {p.label}
            </text>
          </g>
        ))}
      </svg>
    </div>
  );
}

function DonutSvg({ items }: { items: Array<{ code: string; label: string; color: string; count: number }> }) {
  const total = items.reduce((s, i) => s + i.count, 0);
  if (total === 0) {
    return <div style={{ color: 'var(--text-muted)', fontSize: 12.5, padding: 20 }}>Chưa có dữ liệu biểu đồ</div>;
  }

  const radius = 52;
  const circumference = 2 * Math.PI * radius;
  let offset = 0;

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 20, flexWrap: 'wrap' }}>
      <div style={{ position: 'relative', width: 136, height: 136, flexShrink: 0 }}>
        <svg width="136" height="136" viewBox="0 0 136 136">
          <circle cx="68" cy="68" r={radius} fill="none" stroke="#f1f3f4" strokeWidth="18" />
          {items.map((item) => {
            const frac = item.count / total;
            const dash = frac * circumference;
            const seg = (
              <circle
                key={item.code}
                cx="68"
                cy="68"
                r={radius}
                fill="none"
                stroke={item.color || '#1a73e8'}
                strokeWidth="18"
                strokeDasharray={`${dash} ${circumference - dash}`}
                strokeDashoffset={-offset}
                transform="rotate(-90 68 68)"
              />
            );
            offset += dash;
            return seg;
          })}
        </svg>
        <div
          style={{
            position: 'absolute',
            inset: 0,
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <span style={{ fontSize: 22, fontWeight: 800, color: 'var(--text-primary)' }}>{total}</span>
          <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>Tổng</span>
        </div>
      </div>

      <div style={{ flex: 1, minWidth: 160, display: 'flex', flexDirection: 'column', gap: 8 }}>
        {items.map((item) => {
          const pct = Math.round((item.count / total) * 100);
          return (
            <div key={item.code} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', fontSize: 12.5 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <span style={{ width: 10, height: 10, borderRadius: '50%', backgroundColor: item.color || '#1a73e8', flexShrink: 0 }} />
                <span style={{ fontWeight: 500, color: 'var(--text-primary)' }}>{item.label}</span>
              </div>
              <span style={{ color: 'var(--text-secondary)', fontWeight: 700 }}>
                {item.count} <span style={{ color: 'var(--text-muted)', fontWeight: 400 }}>({pct}%)</span>
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export default function AppDashboardView({
  appTree,
  canCustomize,
  personalMode = false,
  onJumpToTable,
}: AppDashboardViewProps) {
  const [widgets, setWidgets] = useState<AppWidget[]>([]);
  const [loading, setLoading] = useState(true);
  const [customizeMode, setCustomizeMode] = useState(false);
  const [showModal, setShowModal] = useState(false);
  const [editingWidget, setEditingWidget] = useState<AppWidget | null>(null);
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');

  const loadWidgets = useCallback(async () => {
    setLoading(true);
    try {
      const qs = new URLSearchParams({
        appId: appTree.app.id,
        includeHidden: String(customizeMode),
      });
      if (personalMode) qs.set('personalMode', 'true');
      if (fromDate) qs.set('fromDate', fromDate);
      if (toDate) qs.set('toDate', toDate);
      const res = await fetch(`/api/widgets?${qs.toString()}`).then((r) => r.json());
      if (res.success && Array.isArray(res.data)) {
        setWidgets(res.data);
      }
    } finally {
      setLoading(false);
    }
  }, [appTree.app.id, customizeMode, personalMode, fromDate, toDate]);

  useEffect(() => {
    loadWidgets();
  }, [loadWidgets]);

  const persistLayout = async (nextWidgets: AppWidget[]) => {
    setWidgets(nextWidgets);
    await fetch('/api/widgets/layout', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(
        nextWidgets.map((w, idx) => ({
          id: w.id,
          orderIndex: idx + 1,
          colSpan: w.colSpan,
          isVisible: w.isVisible,
        }))
      ),
    });
  };

  const moveWidget = async (index: number, delta: -1 | 1) => {
    const target = index + delta;
    if (target < 0 || target >= widgets.length) return;
    const copy = [...widgets];
    const [removed] = copy.splice(index, 1);
    copy.splice(target, 0, removed);
    await persistLayout(copy);
  };

  const cycleSpan = async (w: AppWidget) => {
    const nextSpan = w.colSpan >= 4 ? 1 : w.colSpan + 1;
    const copy = widgets.map((item) => (item.id === w.id ? { ...item, colSpan: nextSpan } : item));
    await persistLayout(copy);
  };

  const toggleVisibility = async (w: AppWidget) => {
    const copy = widgets.map((item) => (item.id === w.id ? { ...item, isVisible: !item.isVisible } : item));
    await persistLayout(copy);
  };

  const deleteWidget = async (id: string) => {
    await fetch(`/api/widgets/${encodeURIComponent(id)}`, { method: 'DELETE' });
    await loadWidgets();
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {/* Dashboard Toolbar */}
      <div
        className="card"
        style={{
          padding: '12px 18px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: 10,
        }}
      >
        <div>
          <strong style={{ fontSize: 15, color: 'var(--text-primary)' }}>
            Dashboard Điều hành — {appTree.app.name}
          </strong>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
            {customizeMode
              ? 'Chế độ Tùy chỉnh: Sắp xếp thứ tự, đổi kích thước (1/4 → 4/4), ẩn/hiện hoặc thêm Widget mới'
              : `Đang hiển thị ${widgets.length} widget tổng hợp dữ liệu trực tiếp`}
          </div>
        </div>

        <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
          <input
            type="date"
            className="form-input"
            style={{ width: 138, height: 32, fontSize: 12, padding: '2px 8px' }}
            value={fromDate}
            onChange={(e) => setFromDate(e.target.value)}
            title="Từ ngày"
          />
          <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>→</span>
          <input
            type="date"
            className="form-input"
            style={{ width: 138, height: 32, fontSize: 12, padding: '2px 8px' }}
            value={toDate}
            onChange={(e) => setToDate(e.target.value)}
            title="Đến ngày"
          />
          {(fromDate || toDate) && (
            <button
              type="button"
              className="btn btn-outline btn-xs"
              onClick={() => {
                setFromDate('');
                setToDate('');
              }}
            >
              Xóa lọc ngày
            </button>
          )}
          {canCustomize && (
            <>
              {customizeMode && (
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  style={{ backgroundColor: appTree.app.color }}
                  onClick={() => {
                    setEditingWidget(null);
                    setShowModal(true);
                  }}
                >
                  <AddRoundedIcon sx={{ fontSize: 16 }} />
                  Thêm Widget
                </button>
              )}
              <button
                type="button"
                className={`btn btn-sm ${customizeMode ? 'btn-primary' : 'btn-outline'}`}
                onClick={() => setCustomizeMode((p) => !p)}
              >
                {customizeMode ? <CheckRoundedIcon sx={{ fontSize: 16 }} /> : <TuneRoundedIcon sx={{ fontSize: 16 }} />}
                {customizeMode ? 'Hoàn tất Tùy chỉnh' : 'Tùy chỉnh Dashboard'}
              </button>
            </>
          )}
        </div>
      </div>

      {/* 4-Column Responsive Widget Grid */}
      {loading ? (
        <div className="card" style={{ textAlign: 'center', padding: 48, color: 'var(--text-muted)' }}>
          Đang tổng hợp dữ liệu Widget...
        </div>
      ) : widgets.length === 0 ? (
        <EmptyState
          title="App chưa có Widget trên Dashboard"
          description="Nhấn 'Tùy chỉnh Dashboard' để thêm các thẻ KPI, biểu đồ Donut/Bar hoặc danh sách theo dõi cho ứng dụng này."
          action={
            canCustomize ? (
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => {
                  setCustomizeMode(true);
                  setEditingWidget(null);
                  setShowModal(true);
                }}
              >
                <AddRoundedIcon sx={{ fontSize: 16 }} /> Tạo Widget đầu tiên
              </button>
            ) : undefined
          }
        />
      ) : (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(4, minmax(0, 1fr))',
            gap: 16,
          }}
        >
          {widgets.map((w, idx) => {
            const wt = w.widgetType.toUpperCase();
            const isKpi = wt.startsWith('KPI_');
            const maxCount = Math.max(...(w.chartItems?.map((i) => i.count) || [1]), 1);

            return (
              <div
                key={w.id}
                className="card"
                style={{
                  gridColumn: `span ${Math.min(Math.max(w.colSpan || 1, 1), 4)} / span ${Math.min(Math.max(w.colSpan || 1, 1), 4)}`,
                  padding: 18,
                  opacity: w.isVisible ? 1 : 0.45,
                  borderTop: isKpi ? `3px solid ${w.color}` : undefined,
                  borderStyle: customizeMode ? 'dashed' : 'solid',
                  display: 'flex',
                  flexDirection: 'column',
                  justifyContent: 'space-between',
                  gap: 12,
                }}
              >
                {/* Customize Toolbar */}
                {customizeMode && (
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      gap: 4,
                      paddingBottom: 8,
                      borderBottom: '1px dashed var(--border-subtle)',
                    }}
                  >
                    <div style={{ display: 'flex', gap: 4 }}>
                      <button type="button" className="btn btn-outline btn-xs" disabled={idx === 0} onClick={() => moveWidget(idx, -1)} title="Di chuyển lên">
                        <ArrowBackRoundedIcon sx={{ fontSize: 13 }} />
                      </button>
                      <button type="button" className="btn btn-outline btn-xs" disabled={idx === widgets.length - 1} onClick={() => moveWidget(idx, 1)} title="Di chuyển xuống">
                        <ArrowForwardRoundedIcon sx={{ fontSize: 13 }} />
                      </button>
                      <button type="button" className="btn btn-outline btn-xs" onClick={() => cycleSpan(w)} title="Đổi độ rộng">
                        {w.colSpan}/4
                      </button>
                    </div>
                    <div style={{ display: 'flex', gap: 4 }}>
                      <button type="button" className="btn btn-outline btn-xs" onClick={() => toggleVisibility(w)} title={w.isVisible ? 'Ẩn widget' : 'Hiện widget'}>
                        {w.isVisible ? <VisibilityRoundedIcon sx={{ fontSize: 13 }} /> : <VisibilityOffRoundedIcon sx={{ fontSize: 13 }} />}
                      </button>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        onClick={() => {
                          setEditingWidget(w);
                          setShowModal(true);
                        }}
                        title="Cấu hình"
                      >
                        <EditRoundedIcon sx={{ fontSize: 13 }} />
                      </button>
                      <button type="button" className="btn btn-outline btn-xs" style={{ color: '#d93025' }} onClick={() => deleteWidget(w.id)} title="Xóa">
                        <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} />
                      </button>
                    </div>
                  </div>
                )}

                {/* Widget Header */}
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
                  <span style={{ fontSize: 13.5, fontWeight: 700, color: 'var(--text-secondary)' }}>{w.title}</span>
                  <div
                    style={{
                      width: 32,
                      height: 32,
                      borderRadius: 10,
                      backgroundColor: `${w.color}18`,
                      color: w.color,
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      flexShrink: 0,
                    }}
                  >
                    {wt === 'DONUT_CHART' ? (
                      <DonutLargeRoundedIcon sx={{ fontSize: 18 }} />
                    ) : wt === 'BAR_CHART' ? (
                      <BarChartRoundedIcon sx={{ fontSize: 18 }} />
                    ) : wt === 'LINE_CHART' ? (
                      <ShowChartRoundedIcon sx={{ fontSize: 18 }} />
                    ) : wt === 'RECENT_LIST' ? (
                      <FormatListBulletedRoundedIcon sx={{ fontSize: 18 }} />
                    ) : (
                      <FunctionsRoundedIcon sx={{ fontSize: 18 }} />
                    )}
                  </div>
                </div>

                {/* Widget Body */}
                {isKpi ? (
                  <div
                    onClick={() => !customizeMode && onJumpToTable(w.tableId)}
                    style={{ cursor: customizeMode ? 'default' : 'pointer' }}
                    title="Nhấp để mở bảng dữ liệu nguồn"
                  >
                    <div style={{ fontSize: 26, fontWeight: 800, color: 'var(--text-primary)', letterSpacing: '-0.02em' }}>
                      {w.formattedValue}
                    </div>
                    {w.subLabel && <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4 }}>{w.subLabel}</div>}
                  </div>
                ) : wt === 'DONUT_CHART' ? (
                  <DonutSvg items={w.chartItems || []} />
                ) : wt === 'LINE_CHART' ? (
                  <LineChartSvg items={w.chartItems || []} color={w.color || '#1a73e8'} />
                ) : wt === 'BAR_CHART' ? (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                    {(w.chartItems || []).length === 0 ? (
                      <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Chưa có dữ liệu</div>
                    ) : (
                      w.chartItems.map((it) => (
                        <div
                          key={it.code}
                          onClick={() => !customizeMode && onJumpToTable(w.tableId, it.code === '(Khác)' ? '' : it.code)}
                          style={{ cursor: customizeMode ? 'default' : 'pointer' }}
                          title="Nhấp để xem các dòng thuộc nhóm này"
                        >
                          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12.5, marginBottom: 4 }}>
                            <span style={{ fontWeight: 600 }}>{it.label}</span>
                            <strong>{it.count}</strong>
                          </div>
                          <div style={{ height: 9, borderRadius: 999, backgroundColor: 'var(--bg-secondary)', overflow: 'hidden' }}>
                            <div
                              style={{
                                width: `${Math.round((it.count / maxCount) * 100)}%`,
                                height: '100%',
                                borderRadius: 999,
                                backgroundColor: it.color || w.color,
                              }}
                            />
                          </div>
                        </div>
                      ))
                    )}
                  </div>
                ) : (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                    {(w.recentRows || []).length === 0 ? (
                      <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Chưa có bản ghi nào</div>
                    ) : (
                      w.recentRows.map((rec) => {
                        const firstVals = Object.entries(rec.data || {}).slice(0, 3);
                        return (
                          <div
                            key={rec.id}
                            onClick={() => !customizeMode && onJumpToTable(w.tableId)}
                            style={{
                              padding: '8px 10px',
                              borderRadius: 10,
                              backgroundColor: '#f8f9fa',
                              border: '1px solid var(--border-light)',
                              display: 'flex',
                              justifyContent: 'space-between',
                              alignItems: 'center',
                              gap: 10,
                              cursor: 'pointer',
                              fontSize: 12.5,
                            }}
                          >
                            <div style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                              <code>{rec.recordKey || rec.id}</code>{' '}
                              <span style={{ color: 'var(--text-secondary)', marginLeft: 6 }}>
                                {firstVals.slice(1).map(([, v]) => String(v ?? '')).filter(Boolean).join(' • ')}
                              </span>
                            </div>
                            <StatusBadge status={rec.status} size="sm" />
                          </div>
                        );
                      })
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      <WidgetConfigModal
        open={showModal}
        appTree={appTree}
        editing={editingWidget}
        nextOrderIndex={widgets.length + 1}
        onClose={() => setShowModal(false)}
        onSaved={async () => {
          setShowModal(false);
          await loadWidgets();
        }}
      />
    </div>
  );
}
