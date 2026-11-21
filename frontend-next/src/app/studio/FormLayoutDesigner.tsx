'use client';

import React, { useMemo, useState } from 'react';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import ArrowUpwardRoundedIcon from '@mui/icons-material/ArrowUpwardRounded';
import ArrowDownwardRoundedIcon from '@mui/icons-material/ArrowDownwardRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import VisibilityRoundedIcon from '@mui/icons-material/VisibilityRounded';
import ViewQuiltRoundedIcon from '@mui/icons-material/ViewQuiltRounded';
import RestartAltRoundedIcon from '@mui/icons-material/RestartAltRounded';
import { FormLayoutConfig, FormLayoutSection, FormRule, SysColumn } from '@/lib/types';
import { buildDefaultFormLayout, normalizeFormLayout } from '@/lib/form-layout';
import { evaluateFormRules, formDefaultValues } from '@/lib/form-rules';
import FormLayoutRenderer from '@/app/components/ui/FormLayoutRenderer';
import DynamicFieldInput from '@/app/components/ui/DynamicFieldInput';

interface FormLayoutDesignerProps {
  columns: SysColumn[];
  formRules?: FormRule[];
  value: FormLayoutConfig | null | undefined;
  onChange: (next: FormLayoutConfig) => void;
}

export default function FormLayoutDesigner({
  columns,
  formRules = [],
  value,
  onChange,
}: FormLayoutDesignerProps) {
  const layout = useMemo(() => normalizeFormLayout(value, columns), [value, columns]);
  const colByCode = useMemo(() => new Map(columns.map((c) => [c.columnCode, c])), [columns]);
  const [showPreview, setShowPreview] = useState(false);
  const [previewRole, setPreviewRole] = useState<'ADMIN' | 'STAFF' | 'HR_MANAGER'>('STAFF');
  const [previewData, setPreviewData] = useState<Record<string, unknown>>(() => formDefaultValues(columns));

  const updateSections = (updater: (secs: FormLayoutSection[]) => FormLayoutSection[]) => {
    onChange({ ...layout, sections: updater(layout.sections) });
  };

  const addSection = () => {
    const idx = layout.sections.length + 1;
    updateSections((secs) => [
      ...secs,
      {
        id: `sec_${Date.now().toString(36)}_${idx}`,
        title: `Phần ${idx}`,
        description: '',
        columns: 2,
        collapsible: false,
        calloutText: '',
        calloutTone: 'info',
        fields: [],
      },
    ]);
  };

  const removeSection = (idx: number) => {
    if (layout.sections.length <= 1) return;
    const removedFields = layout.sections[idx]?.fields || [];
    const remaining = layout.sections.filter((_, i) => i !== idx);
    const targetIdx = Math.max(0, idx - 1);
    remaining[targetIdx] = {
      ...remaining[targetIdx],
      fields: [...remaining[targetIdx].fields, ...removedFields],
    };
    onChange({ ...layout, sections: remaining });
  };

  const moveSection = (idx: number, dir: -1 | 1) => {
    const target = idx + dir;
    if (target < 0 || target >= layout.sections.length) return;
    updateSections((secs) => {
      const next = [...secs];
      const [item] = next.splice(idx, 1);
      next.splice(target, 0, item);
      return next;
    });
  };

  const moveFieldWithinSection = (secIdx: number, fieldIdx: number, dir: -1 | 1) => {
    const sec = layout.sections[secIdx];
    const target = fieldIdx + dir;
    if (!sec || target < 0 || target >= sec.fields.length) return;
    updateSections((secs) =>
      secs.map((s, i) => {
        if (i !== secIdx) return s;
        const fields = [...s.fields];
        const [item] = fields.splice(fieldIdx, 1);
        fields.splice(target, 0, item);
        return { ...s, fields };
      }),
    );
  };

  const moveFieldToSection = (fromSecIdx: number, fieldCode: string, toSecId: string) => {
    const toSecIdx = layout.sections.findIndex((s) => s.id === toSecId);
    if (toSecIdx < 0 || toSecIdx === fromSecIdx) return;
    const fieldObj = layout.sections[fromSecIdx]?.fields.find((f) => f.columnCode === fieldCode);
    if (!fieldObj) return;
    updateSections((secs) =>
      secs.map((s, i) => {
        if (i === fromSecIdx) return { ...s, fields: s.fields.filter((f) => f.columnCode !== fieldCode) };
        if (i === toSecIdx) {
          const maxSpan = Math.max(1, Math.min(3, Number(s.columns) || 2));
          return {
            ...s,
            fields: [...s.fields, { columnCode: fieldObj.columnCode, colSpan: Math.min(fieldObj.colSpan, maxSpan) }],
          };
        }
        return s;
      }),
    );
  };

  const setFieldSpan = (secIdx: number, fieldCode: string, span: number) => {
    updateSections((secs) =>
      secs.map((s, i) =>
        i !== secIdx
          ? s
          : {
              ...s,
              fields: s.fields.map((f) => (f.columnCode === fieldCode ? { ...f, colSpan: span } : f)),
            },
      ),
    );
  };

  const previewRuleStates = useMemo(
    () => evaluateFormRules(columns, formRules, previewData),
    [columns, formRules, previewData],
  );

  return (
    <section
      aria-label="Thiết kế bố cục biểu mẫu"
      style={{
        padding: 14,
        border: '1px solid var(--border-subtle)',
        borderRadius: 12,
        backgroundColor: 'var(--bg-primary)',
        display: 'flex',
        flexDirection: 'column',
        gap: 12,
        marginBottom: 16,
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <ViewQuiltRoundedIcon sx={{ fontSize: 19, color: 'var(--accent-primary)' }} />
          <div>
            <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--text-primary)' }}>
              Thiết kế Bố cục Form trực quan (Sections / Tabs / Wizard)
            </div>
            <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
              Sắp xếp nhóm trường, số cột 1–3, độ rộng từng ô và xem trước theo vai trò trước khi áp dụng.
            </div>
          </div>
        </div>
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
          <button
            type="button"
            className={`btn btn-xs ${showPreview ? 'btn-primary' : 'btn-outline'}`}
            onClick={() => setShowPreview((v) => !v)}
          >
            <VisibilityRoundedIcon sx={{ fontSize: 14 }} />
            {showPreview ? 'Ẩn xem trước' : 'Xem trước theo Vai trò'}
          </button>
          <button
            type="button"
            className="btn btn-outline btn-xs"
            onClick={() => onChange(buildDefaultFormLayout(columns))}
            title="Khôi phục bố cục mặc định 1 phần 2 cột"
          >
            <RestartAltRoundedIcon sx={{ fontSize: 14 }} /> Mặc định
          </button>
        </div>
      </div>

      {/* Mode Switcher */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 8 }}>
        <div className="segmented-tabs subtle">
          {(
            [
              { id: 'sections', label: 'Các khối (Sections)' },
              { id: 'tabs', label: 'Thẻ Tab (Tabs)' },
              { id: 'wizard', label: 'Nhiều bước (Wizard)' },
            ] as const
          ).map((m) => (
            <button
              key={m.id}
              type="button"
              className={`segmented-tab ${layout.mode === m.id ? 'active' : ''}`}
              onClick={() => onChange({ ...layout, mode: m.id })}
            >
              {m.label}
            </button>
          ))}
        </div>
        <button type="button" className="btn btn-outline btn-xs" onClick={addSection}>
          <AddRoundedIcon sx={{ fontSize: 14 }} /> Thêm Phần / Bước
        </button>
      </div>

      {/* Sections Editor */}
      {columns.filter((c) => c.showInForm !== false).length === 0 ? (
        <div style={{ fontSize: 12.5, color: 'var(--text-muted)', padding: 10 }}>
          Tạo các cột cho bảng trước, sau đó mở sửa bảng để thiết kế bố cục biểu mẫu.
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {layout.sections.map((sec, secIdx) => {
            const colCount = Math.max(1, Math.min(3, Number(sec.columns) || 2));
            return (
              <div
                key={sec.id}
                style={{
                  border: '1px solid var(--border-subtle)',
                  borderRadius: 10,
                  padding: 12,
                  backgroundColor: 'var(--bg-surface)',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 10,
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, flex: 1, minWidth: 220 }}>
                    <span className="badge badge-info" style={{ fontSize: 11 }}>#{secIdx + 1}</span>
                    <input
                      className="form-input"
                      style={{ height: 32, fontSize: 12.5, fontWeight: 600 }}
                      aria-label={`Tiêu đề phần ${secIdx + 1}`}
                      value={sec.title}
                      onChange={(e) =>
                        updateSections((secs) => secs.map((s, i) => (i === secIdx ? { ...s, title: e.target.value } : s)))
                      }
                      placeholder="Tiêu đề phần / bước..."
                    />
                  </div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                    <select
                      className="form-select"
                      style={{ width: 96, height: 30, fontSize: 12, padding: '0 8px' }}
                      aria-label={`Số cột phần ${secIdx + 1}`}
                      value={colCount}
                      onChange={(e) => {
                        const nextCols = Number(e.target.value) as 1 | 2 | 3;
                        updateSections((secs) =>
                          secs.map((s, i) =>
                            i !== secIdx
                              ? s
                              : {
                                  ...s,
                                  columns: nextCols,
                                  fields: s.fields.map((f) => ({ ...f, colSpan: Math.min(f.colSpan, nextCols) })),
                                },
                          ),
                        );
                      }}
                    >
                      <option value={1}>1 cột</option>
                      <option value={2}>2 cột</option>
                      <option value={3}>3 cột</option>
                    </select>
                    <label style={{ display: 'inline-flex', alignItems: 'center', gap: 4, fontSize: 11.5, color: 'var(--text-secondary)' }}>
                      <input
                        type="checkbox"
                        checked={Boolean(sec.collapsible)}
                        onChange={(e) =>
                          updateSections((secs) =>
                            secs.map((s, i) => (i === secIdx ? { ...s, collapsible: e.target.checked } : s)),
                          )
                        }
                      />
                      Thu gọn
                    </label>
                    <button type="button" className="btn btn-outline btn-xs" disabled={secIdx === 0} onClick={() => moveSection(secIdx, -1)} aria-label="Di chuyển phần lên">
                      <ArrowUpwardRoundedIcon sx={{ fontSize: 14 }} />
                    </button>
                    <button type="button" className="btn btn-outline btn-xs" disabled={secIdx === layout.sections.length - 1} onClick={() => moveSection(secIdx, 1)} aria-label="Di chuyển phần xuống">
                      <ArrowDownwardRoundedIcon sx={{ fontSize: 14 }} />
                    </button>
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      disabled={layout.sections.length <= 1}
                      onClick={() => removeSection(secIdx)}
                      aria-label="Xóa phần"
                    >
                      <DeleteOutlineRoundedIcon sx={{ fontSize: 14 }} />
                    </button>
                  </div>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
                  <input
                    className="form-input"
                    style={{ height: 30, fontSize: 12 }}
                    value={sec.description || ''}
                    onChange={(e) =>
                      updateSections((secs) => secs.map((s, i) => (i === secIdx ? { ...s, description: e.target.value } : s)))
                    }
                    placeholder="Mô tả ngắn dưới tiêu đề (tùy chọn)..."
                  />
                  <input
                    className="form-input"
                    style={{ height: 30, fontSize: 12 }}
                    value={sec.calloutText || ''}
                    onChange={(e) =>
                      updateSections((secs) => secs.map((s, i) => (i === secIdx ? { ...s, calloutText: e.target.value } : s)))
                    }
                    placeholder="Ghi chú Callout hướng dẫn nhập liệu (tùy chọn)..."
                  />
                </div>

                {/* Fields inside this section */}
                {sec.fields.length === 0 ? (
                  <div style={{ fontSize: 12, color: 'var(--text-muted)', padding: '6px 8px', borderRadius: 8, backgroundColor: 'var(--bg-primary)' }}>
                    Chưa có trường nào trong phần này — chọn chuyển trường từ phần khác sang đây.
                  </div>
                ) : (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    {sec.fields.map((f, fIdx) => {
                      const col = colByCode.get(f.columnCode);
                      if (!col) return null;
                      return (
                        <div
                          key={f.columnCode}
                          style={{
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            gap: 8,
                            flexWrap: 'wrap',
                            padding: '6px 10px',
                            borderRadius: 8,
                            backgroundColor: 'var(--bg-primary)',
                            border: '1px solid var(--border-light)',
                          }}
                        >
                          <div style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 160 }}>
                            <strong style={{ fontSize: 12.5 }}>{col.columnName}</strong>
                            <code style={{ fontSize: 11 }}>{col.columnCode}</code>
                            <span className="badge" style={{ fontSize: 10, backgroundColor: 'var(--bg-secondary)', color: 'var(--text-muted)' }}>
                              {col.dataType}
                            </span>
                          </div>
                          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                            <select
                              className="form-select"
                              style={{ width: 96, height: 26, fontSize: 11.5, padding: '0 6px' }}
                              aria-label={`Độ rộng ${col.columnName}`}
                              value={f.colSpan}
                              onChange={(e) => setFieldSpan(secIdx, f.columnCode, Number(e.target.value))}
                            >
                              <option value={1}>1/{colCount} dòng</option>
                              {colCount >= 2 && <option value={2}>2/{colCount} dòng</option>}
                              {colCount >= 3 && <option value={3}>Toàn dòng</option>}
                            </select>
                            {layout.sections.length > 1 && (
                              <select
                                className="form-select"
                                style={{ width: 130, height: 26, fontSize: 11.5, padding: '0 6px' }}
                                aria-label={`Chuyển ${col.columnName} sang phần`}
                                value={sec.id}
                                onChange={(e) => moveFieldToSection(secIdx, f.columnCode, e.target.value)}
                              >
                                {layout.sections.map((targetSec, tIdx) => (
                                  <option key={targetSec.id} value={targetSec.id}>
                                    → #{tIdx + 1} {targetSec.title}
                                  </option>
                                ))}
                              </select>
                            )}
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              disabled={fIdx === 0}
                              onClick={() => moveFieldWithinSection(secIdx, fIdx, -1)}
                              aria-label={`Đưa ${col.columnName} lên`}
                            >
                              <ArrowUpwardRoundedIcon sx={{ fontSize: 13 }} />
                            </button>
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              disabled={fIdx === sec.fields.length - 1}
                              onClick={() => moveFieldWithinSection(secIdx, fIdx, 1)}
                              aria-label={`Đưa ${col.columnName} xuống`}
                            >
                              <ArrowDownwardRoundedIcon sx={{ fontSize: 13 }} />
                            </button>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* Live Role Preview */}
      {showPreview && (
        <div
          style={{
            border: '1.5px dashed var(--accent-primary)',
            borderRadius: 12,
            padding: 14,
            backgroundColor: 'var(--bg-surface)',
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8, marginBottom: 12 }}>
            <strong style={{ fontSize: 13, color: 'var(--accent-primary)' }}>
              Xem trước trực tiếp (Không lưu dữ liệu thật)
            </strong>
            <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>Giả lập vai trò:</span>
              <select
                className="form-select"
                style={{ width: 150, height: 28, fontSize: 12 }}
                value={previewRole}
                onChange={(e) => setPreviewRole(e.target.value as 'ADMIN' | 'STAFF' | 'HR_MANAGER')}
              >
                <option value="STAFF">Nhân viên (STAFF)</option>
                <option value="HR_MANAGER">Quản lý (HR_MANAGER)</option>
                <option value="ADMIN">Quản trị (ADMIN)</option>
              </select>
            </div>
          </div>
          <FormLayoutRenderer
            layout={layout}
            columns={columns}
            isColumnVisible={(col) => {
              if (previewRole !== 'ADMIN' && col.hiddenForRoles?.some((r) => r.toUpperCase() === previewRole)) return false;
              const ruleState = previewRuleStates[col.columnCode];
              return ruleState ? ruleState.visible : true;
            }}
            renderField={(col) => {
              const ruleState = previewRuleStates[col.columnCode];
              const isRo =
                Boolean(col.formula) ||
                Boolean(ruleState?.readOnly) ||
                (previewRole !== 'ADMIN' && Boolean(col.readOnlyForRoles?.some((r) => r.toUpperCase() === previewRole)));
              return (
                <div>
                  <label className="form-label" style={{ fontSize: 12 }}>
                    {col.columnName} {(ruleState?.required ?? col.isRequired) && <span style={{ color: 'var(--danger)' }}>*</span>}
                  </label>
                  <DynamicFieldInput
                    column={col}
                    value={previewData[col.columnCode]}
                    disabled={isRo}
                    formData={previewData}
                    onChange={(val) => setPreviewData((prev) => ({ ...prev, [col.columnCode]: val }))}
                  />
                </div>
              );
            }}
          />
        </div>
      )}
    </section>
  );
}
