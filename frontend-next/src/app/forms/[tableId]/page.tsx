'use client';

import React, { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import SendRoundedIcon from '@mui/icons-material/SendRounded';
import AssignmentRoundedIcon from '@mui/icons-material/AssignmentRounded';
import { SysCategoryItem, SysColumn, FormRule } from '@/lib/types';
import { evaluateFormRules, prepareFormSubmission, requiredFormErrors, formDefaultValues, restoreProtectedFormValues } from '@/lib/form-rules';

interface PublicFormPayload {
  app?: { id: string; code: string; name: string; color: string; department: string } | null;
  table: { id: string; displayName: string; description?: string | null; formRules?: FormRule[] };
  columns: SysColumn[];
  categories: Record<string, SysCategoryItem[]>;
}

export default function PublicIntakeFormPage() {
  const params = useParams();
  const tableId = String(params?.tableId || '');
  const [loading, setLoading] = useState(true);
  const [formDef, setFormDef] = useState<PublicFormPayload | null>(null);
  const [errorMsg, setErrorMsg] = useState('');
  const [values, setValues] = useState<Record<string, any>>({});
  const [submitting, setSubmitting] = useState(false);
  const [submittedKey, setSubmittedKey] = useState<string | null>(null);

  useEffect(() => {
    if (!tableId) return;
    setLoading(true);
    fetch(`/api/public/forms/${encodeURIComponent(tableId)}`)
      .then((r) => r.json())
      .then((res) => {
        if (res.success && res.data) {
          setFormDef(res.data);
          setValues(formDefaultValues(res.data.columns));
        } else {
          setErrorMsg(res.message || 'Biểu mẫu công khai không tồn tại hoặc đã bị tắt.');
        }
      })
      .catch(() => setErrorMsg('Không thể tải biểu mẫu từ máy chủ.'))
      .finally(() => setLoading(false));
  }, [tableId]);

  useEffect(() => {
    if (!formDef?.table.formRules?.length) return;
    setValues(current => restoreProtectedFormValues(formDef.columns, formDef.table.formRules || [], current));
  }, [formDef, values]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formDef) return;
    const issues = requiredFormErrors(formDef.columns, formDef.table.formRules || [], values);
    if (issues.length) { setErrorMsg(issues.map(e => e.message).join(' ')); return; }
    const payload = prepareFormSubmission(formDef.columns, formDef.table.formRules || [], values);
    for (const column of formDef.columns) if (column.readOnlyForRoles?.some(r => r.toUpperCase() === 'STAFF')) delete payload[column.columnCode];
    setSubmitting(true);
    setErrorMsg('');
    try {
      const res = await fetch(`/api/public/forms/${encodeURIComponent(tableId)}/submit`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      }).then((r) => r.json());
      if (res.success) {
        setSubmittedKey(res.data?.recordKey || res.data?.id || 'OK');
        setValues(formDefaultValues(formDef.columns));
      } else {
        setErrorMsg(res.message || 'Không thể gửi biểu mẫu.');
      }
    } catch {
      setErrorMsg('Lỗi kết nối máy chủ.');
    } finally {
      setSubmitting(false);
    }
  };

  const accent = formDef?.app?.color || '#1a73e8';
  const states = evaluateFormRules(formDef?.columns || [], formDef?.table.formRules || [], values);

  return (
    <div
      style={{
        minHeight: '100vh',
        background: 'linear-gradient(135deg, #f0f4f9 0%, #f8f9fa 100%)',
        padding: '32px 16px',
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'flex-start',
      }}
    >
      <div
        style={{
          width: '100%',
          maxWidth: 720,
          backgroundColor: 'var(--bg-surface)',
          borderRadius: 20,
          border: '1px solid var(--border-subtle)',
          borderTop: `6px solid ${accent}`,
          boxShadow: 'var(--shadow-lg)',
          padding: 28,
        }}
      >
        {loading ? (
          <div style={{ textAlign: 'center', padding: 48, color: 'var(--text-muted)' }}>
            Đang nạp biểu mẫu...
          </div>
        ) : !formDef ? (
          <div style={{ textAlign: 'center', padding: 36 }}>
            <h2 style={{ fontSize: 18, fontWeight: 700, marginBottom: 8 }}>Biểu mẫu không khả dụng</h2>
            <p style={{ fontSize: 13.5, color: 'var(--text-muted)' }}>{errorMsg}</p>
          </div>
        ) : submittedKey ? (
          <div style={{ textAlign: 'center', padding: 32 }}>
            <CheckCircleRoundedIcon sx={{ fontSize: 54, color: '#1e8e3e', mb: 1 }} />
            <h2 style={{ fontSize: 20, fontWeight: 700, marginBottom: 6 }}>Gửi thông tin thành công!</h2>
            <p style={{ fontSize: 13.5, color: 'var(--text-secondary)', marginBottom: 16 }}>
              Mã tiếp nhận hồ sơ của bạn là: <code>{submittedKey}</code>
            </p>
            <button type="button" className="btn btn-primary btn-sm" onClick={() => setSubmittedKey(null)}>
              Gửi phiếu khác
            </button>
          </div>
        ) : (
          <>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 20 }}>
              <div
                style={{
                  width: 44,
                  height: 44,
                  borderRadius: 12,
                  backgroundColor: `${accent}18`,
                  color: accent,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                <AssignmentRoundedIcon sx={{ fontSize: 24 }} />
              </div>
              <div>
                <div style={{ fontSize: 11.5, fontWeight: 700, color: accent, textTransform: 'uppercase' }}>
                  {formDef.app?.name || 'SmartApps Public Intake'}
                </div>
                <h1 style={{ fontSize: 20, fontWeight: 700, margin: 0 }}>{formDef.table.displayName}</h1>
                {formDef.table.description && (
                  <p style={{ fontSize: 12.5, color: 'var(--text-muted)', margin: '4px 0 0 0' }}>
                    {formDef.table.description}
                  </p>
                )}
              </div>
            </div>

            {errorMsg && (
              <div role="alert" className="badge badge-danger" style={{ padding: '8px 12px', borderRadius: 8, marginBottom: 14, width: '100%' }}>
                {errorMsg}
              </div>
            )}

            <form onSubmit={handleSubmit} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14 }}>
              {formDef.columns
                .filter((c) => !c.formula && c.dataType.toUpperCase() !== 'AUTO_NUMBER' && states[c.columnCode]?.visible)
                .map((col) => {
                  const dt = (col.dataType || 'STRING').toUpperCase();
                  const required = states[col.columnCode].required;
                  const readOnly = states[col.columnCode].readOnly || col.readOnlyForRoles?.some(r => r.toUpperCase() === 'STAFF');
                  const catOpts = col.refCategoryCode ? formDef.categories[col.refCategoryCode] || [] : [];
                  const fullSpan = dt === 'TEXTAREA' || dt === 'SUB_TABLE';
                  return (
                    <div key={col.id} className="form-group" style={{ gridColumn: fullSpan ? '1 / -1' : undefined, marginBottom: 2 }}>
                      <label className="form-label" htmlFor={`public-field-${col.id}`}>
                        {col.columnName} {required && <span style={{ color: '#d93025' }}>*</span>}
                      </label>
                      {catOpts.length > 0 ? (
                        <select
                          id={`public-field-${col.id}`}
                          className="form-select"
                          required={required}
                          disabled={readOnly}
                          value={values[col.columnCode] || ''}
                          onChange={(e) => setValues({ ...values, [col.columnCode]: e.target.value })}
                        >
                          <option value="">-- Chọn --</option>
                          {catOpts.map((it) => (
                            <option key={it.id} value={it.itemCode}>
                              {it.itemName}
                            </option>
                          ))}
                        </select>
                      ) : dt === 'BOOLEAN' ? (
                        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
                          <input
                            id={`public-field-${col.id}`}
                            aria-label={col.columnName}
                            type="checkbox"
                            disabled={readOnly}
                            checked={Boolean(values[col.columnCode])}
                            onChange={(e) => setValues({ ...values, [col.columnCode]: e.target.checked })}
                          />
                          Có / Xác nhận
                        </label>
                      ) : dt === 'TEXTAREA' ? (
                        <textarea
                          id={`public-field-${col.id}`}
                          className="form-textarea"
                          rows={3}
                          required={required}
                          disabled={readOnly}
                          value={values[col.columnCode] || ''}
                          onChange={(e) => setValues({ ...values, [col.columnCode]: e.target.value })}
                        />
                      ) : (
                        <input
                          id={`public-field-${col.id}`}
                          type={
                            dt === 'DATE'
                              ? 'date'
                              : dt === 'NUMBER' || dt === 'CURRENCY'
                              ? 'number'
                              : dt === 'EMAIL'
                              ? 'email'
                              : dt === 'TEL'
                              ? 'tel'
                              : 'text'
                          }
                          className="form-input"
                          required={required}
                          disabled={readOnly}
                          value={values[col.columnCode] ?? ''}
                          onChange={(e) =>
                            setValues({
                              ...values,
                              [col.columnCode]:
                                (dt === 'NUMBER' || dt === 'CURRENCY') && e.target.value !== ''
                                  ? Number(e.target.value)
                                  : e.target.value,
                            })
                          }
                        />
                      )}
                    </div>
                  );
                })}

              <div style={{ gridColumn: '1 / -1', display: 'flex', justifyContent: 'flex-end', marginTop: 10 }}>
                <button
                  type="submit"
                  className="btn btn-primary"
                  style={{ backgroundColor: accent }}
                  disabled={submitting}
                >
                  <SendRoundedIcon sx={{ fontSize: 16 }} />
                  {submitting ? 'Đang gửi...' : 'Gửi Biểu mẫu'}
                </button>
              </div>
            </form>
          </>
        )}
      </div>
    </div>
  );
}
