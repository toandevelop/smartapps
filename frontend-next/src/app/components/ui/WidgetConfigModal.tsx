'use client';

import React, { useState } from 'react';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import { APP_COLOR_PRESETS } from '@/lib/design-tokens';
import { AppRuntimeTree, AppWidget, SysColumn } from '@/lib/types';
import { useEscapeKey } from '@/lib/ui-hooks';

export interface WidgetConfigModalProps {
  open: boolean;
  appTree: AppRuntimeTree;
  editing: AppWidget | null;
  nextOrderIndex: number;
  onClose: () => void;
  onSaved: () => void;
}

export default function WidgetConfigModal(props: WidgetConfigModalProps) {
  useEscapeKey(props.open, props.onClose);
  if (!props.open) return null;
  return <WidgetConfigModalBody key={props.editing?.id || '__new__'} {...props} />;
}

function WidgetConfigModalBody({
  appTree,
  editing,
  nextOrderIndex,
  onClose,
  onSaved,
}: WidgetConfigModalProps) {
  const allTables = appTree.modules.flatMap((m) => m.tables);
  const defaultTableId = allTables[0]?.table.id || '';

  const [title, setTitle] = useState(() => editing?.title || '');
  const [tableId, setTableId] = useState(() => editing?.tableId || defaultTableId);
  const [widgetType, setWidgetType] = useState<string>(() => editing?.widgetType || 'KPI_COUNT');
  const [metricField, setMetricField] = useState(() => editing?.metricField || '');
  const [groupField, setGroupField] = useState(() => editing?.groupField || '');
  const [filterField, setFilterField] = useState(() => editing?.filterField || '');
  const [filterValue, setFilterValue] = useState(() => editing?.filterValue || '');
  const [colSpan, setColSpan] = useState(() => editing?.colSpan || 1);
  const [color, setColor] = useState(() => editing?.color || appTree.app.color || '#1a73e8');
  const [saving, setSaving] = useState(false);

  const selectedTableNode = allTables.find((t) => t.table.id === tableId);
  const cols: SysColumn[] = selectedTableNode?.columns || [];
  const numericCols = cols.filter((c) => ['NUMBER', 'CURRENCY'].includes(c.dataType.toUpperCase()));
  const groupCols = cols.filter((c) => ['SELECT', 'BOOLEAN', 'LOOKUP'].includes(c.dataType.toUpperCase()) || Boolean(c.refCategoryCode));
  const dateCols = cols.filter((c) => c.dataType.toUpperCase() === 'DATE');

  const needsMetric = widgetType === 'KPI_SUM' || widgetType === 'KPI_AVG';
  const needsGroup = widgetType === 'BAR_CHART' || widgetType === 'DONUT_CHART';
  const isLineChart = widgetType === 'LINE_CHART';

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!title.trim() || !tableId) return;
    setSaving(true);
    try {
      const res = await fetch('/api/widgets', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: editing?.id,
          appId: appTree.app.id,
          tableId,
          title,
          widgetType,
          metricField: needsMetric ? metricField || numericCols[0]?.columnCode : null,
          groupField: needsGroup ? groupField || groupCols[0]?.columnCode : isLineChart ? groupField || null : null,
          filterField: filterField || null,
          filterValue: filterValue || null,
          colSpan,
          color,
          orderIndex: editing ? editing.orderIndex : nextOrderIndex,
          isVisible: editing ? editing.isVisible : true,
        }),
      }).then((r) => r.json());
      if (res.success) onSaved();
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 620 }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
          <h3 style={{ fontSize: 17, fontWeight: 700 }}>
            {editing ? `Tùy chỉnh Widget: ${editing.title}` : `Thêm Widget vào Dashboard ${appTree.app.code}`}
          </h3>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        <form onSubmit={submit}>
          <div className="form-group">
            <label className="form-label">Tiêu đề Widget *</label>
            <input
              className="form-input"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="VD: Tổng giá trị tài sản, Nhân sự theo phòng ban..."
            />
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Nguồn dữ liệu (Bảng trong App)</label>
              <select className="form-select" value={tableId} onChange={(e) => setTableId(e.target.value)}>
                {allTables.map((t) => (
                  <option key={t.table.id} value={t.table.id}>
                    {t.table.displayName}
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Loại Widget</label>
              <select
                className="form-select"
                value={widgetType}
                onChange={(e) => {
                  const wt = e.target.value;
                  setWidgetType(wt);
                  if (['BAR_CHART', 'DONUT_CHART', 'LINE_CHART', 'RECENT_LIST'].includes(wt) && colSpan === 1) {
                    setColSpan(2);
                  }
                }}
              >
                <option value="KPI_COUNT">Thẻ KPI — Đếm số lượng (COUNT)</option>
                <option value="KPI_SUM">Thẻ KPI — Tổng giá trị (SUM)</option>
                <option value="KPI_AVG">Thẻ KPI — Trung bình (AVG)</option>
                <option value="DONUT_CHART">Biểu đồ Tròn (Donut Chart)</option>
                <option value="BAR_CHART">Biểu đồ Cột ngang (Bar Chart)</option>
                <option value="LINE_CHART">Biểu đồ Xu hướng Thời gian (Line Chart)</option>
                <option value="RECENT_LIST">Danh sách Bản ghi mới nhất</option>
              </select>
            </div>
          </div>

          {needsMetric && (
            <div className="form-group">
              <label className="form-label">Trường số liệu cần tính (NUMBER / CURRENCY)</label>
              <select className="form-select" value={metricField} onChange={(e) => setMetricField(e.target.value)}>
                <option value="">-- Chọn trường số --</option>
                {numericCols.map((c) => (
                  <option key={c.id} value={c.columnCode}>
                    {c.columnName} ({c.columnCode})
                  </option>
                ))}
              </select>
            </div>
          )}

          {needsGroup && (
            <div className="form-group">
              <label className="form-label">Nhóm dữ liệu theo trường (Danh mục / Đúng-Sai / Lookup)</label>
              <select className="form-select" value={groupField} onChange={(e) => setGroupField(e.target.value)}>
                <option value="">-- Chọn trường phân nhóm --</option>
                {groupCols.map((c) => (
                  <option key={c.id} value={c.columnCode}>
                    {c.columnName} ({c.columnCode})
                  </option>
                ))}
              </select>
            </div>
          )}

          {isLineChart && (
            <div className="form-group">
              <label className="form-label">Trục thời gian (Mặc định: Ngày tạo bản ghi)</label>
              <select className="form-select" value={groupField} onChange={(e) => setGroupField(e.target.value)}>
                <option value="">Ngày tạo bản ghi (created_at)</option>
                {dateCols.map((c) => (
                  <option key={c.id} value={c.columnCode}>
                    {c.columnName} ({c.columnCode})
                  </option>
                ))}
              </select>
            </div>
          )}

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Lọc theo trường (tùy chọn)</label>
              <select className="form-select" value={filterField} onChange={(e) => setFilterField(e.target.value)}>
                <option value="">-- Không lọc (toàn bảng) --</option>
                {cols.map((c) => (
                  <option key={c.id} value={c.columnCode}>
                    {c.columnName} ({c.columnCode})
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Giá trị lọc (VD: true, HIGH, CHO_DUYET)</label>
              <input
                className="form-input"
                value={filterValue}
                disabled={!filterField}
                onChange={(e) => setFilterValue(e.target.value)}
                placeholder="Nhập giá trị khớp..."
              />
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, alignItems: 'center' }}>
            <div className="form-group">
              <label className="form-label">Kích thước trên lưới 4 cột</label>
              <select className="form-select" value={colSpan} onChange={(e) => setColSpan(Number(e.target.value))}>
                <option value={1}>1/4 chiều rộng (Nhỏ - KPI)</option>
                <option value={2}>2/4 chiều rộng (Vừa - Biểu đồ)</option>
                <option value={3}>3/4 chiều rộng (Lớn)</option>
                <option value={4}>4/4 Toàn chiều rộng</option>
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Màu sắc Widget</label>
              <div style={{ display: 'flex', gap: 8, paddingTop: 4 }}>
                {APP_COLOR_PRESETS.map((p) => (
                  <button
                    key={p.color}
                    type="button"
                    onClick={() => setColor(p.color)}
                    style={{
                      width: 26,
                      height: 26,
                      borderRadius: '50%',
                      backgroundColor: p.color,
                      border: color === p.color ? '3px solid #202124' : '2px solid #fff',
                      boxShadow: '0 1px 3px rgba(0,0,0,0.2)',
                      cursor: 'pointer',
                    }}
                  />
                ))}
              </div>
            </div>
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 18 }}>
            <button type="button" className="btn btn-outline" onClick={onClose}>Hủy</button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              <SaveRoundedIcon sx={{ fontSize: 16 }} /> {saving ? 'Đang lưu...' : 'Lưu Widget'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
