'use client';

import React, { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import SendRoundedIcon from '@mui/icons-material/SendRounded';
import AssignmentRoundedIcon from '@mui/icons-material/AssignmentRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import DynamicFieldInput from '@/app/components/ui/DynamicFieldInput';
import { SysCategoryItem, SysColumn, FormRule } from '@/lib/types';
import { evaluateFormRules, prepareFormSubmission, requiredFormErrors, formDefaultValues, restoreProtectedFormValues } from '@/lib/form-rules';

interface PublicFormPayload {
  app?: { id: string; code: string; name: string; color: string; department: string } | null;
  table: {
    id: string;
    displayName: string;
    description?: string | null;
    formRules?: FormRule[];
    publicFormExpiresAt?: string | null;
    publicFormMaxSubmissions?: number | null;
    recordCount?: number;
  };
  columns: SysColumn[];
  categories: Record<string, SysCategoryItem[]>;
}

const TRACK_STATUS_LABEL: Record<string, string> = {
  DRAFT: 'Đã tiếp nhận (Nháp)',
  PENDING: 'Đang chờ phê duyệt',
  APPROVED: 'Đã phê duyệt',
  REJECTED: 'Bị từ chối / Cần chỉnh sửa',
  RECALLED: 'Đã thu hồi',
  CANCELLED: 'Đã hủy',
};

export default function PublicIntakeFormPage() {
  const params = useParams();
  const tableId = String(params?.tableId || '');
  const [loading, setLoading] = useState(true);
  const [formDef, setFormDef] = useState<PublicFormPayload | null>(null);
  const [errorMsg, setErrorMsg] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [values, setValues] = useState<Record<string, unknown>>({});
  const [hasRestoredDraft, setHasRestoredDraft] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [submittedKey, setSubmittedKey] = useState<string | null>(null);
  const [mode, setMode] = useState<'submit' | 'track'>('submit');
  const [trackCode, setTrackCode] = useState('');
  const [trackLoading, setTrackLoading] = useState(false);
  const [trackError, setTrackError] = useState('');
  const [trackResult, setTrackResult] = useState<{
    recordKey: string;
    approvalStatus: string;
    currentStepName?: string | null;
    createdAt: string;
    updatedAt: string;
  } | null>(null);
  const draftKey = tableId ? `smartapps_public_draft_${tableId}` : '';

  const runTrackLookup = async (codeOverride?: string) => {
    const q = (codeOverride ?? trackCode).trim();
    if (!q || !tableId) return;
    setTrackLoading(true);
    setTrackError('');
    setTrackResult(null);
    try {
      const res = await fetch(`/api/public/forms/${encodeURIComponent(tableId)}/track/${encodeURIComponent(q)}`).then((r) => r.json());
      if (res.success && res.data) setTrackResult(res.data);
      else setTrackError(res.message || 'Không tìm thấy hồ sơ.');
    } catch {
      setTrackError('Lỗi kết nối khi tra cứu hồ sơ.');
    } finally {
      setTrackLoading(false);
    }
  };

  useEffect(() => {
    if (!tableId) return;
    let cancelled = false;
    fetch(`/api/public/forms/${encodeURIComponent(tableId)}`)
      .then((r) => r.json())
      .then((res) => {
        if (cancelled) return;
        if (res.success && res.data) {
          setFormDef(res.data);
          const defaults = formDefaultValues(res.data.columns);
          let initialValues = defaults;
          if (typeof window !== 'undefined' && draftKey) {
            try {
              const saved = localStorage.getItem(draftKey);
              if (saved) {
                const parsed = JSON.parse(saved);
                if (parsed && typeof parsed === 'object') {
                  initialValues = { ...defaults, ...parsed };
                  setHasRestoredDraft(true);
                }
              }
            } catch {
              // ignore malformed draft
            }
          }
          setValues(restoreProtectedFormValues(res.data.columns, res.data.table.formRules || [], initialValues));
        } else {
          setErrorMsg(res.message || 'Biểu mẫu công khai không tồn tại hoặc đã bị tắt.');
        }
      })
      .catch(() => {
        if (!cancelled) setErrorMsg('Không thể tải biểu mẫu từ máy chủ.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [tableId, draftKey]);

  useEffect(() => {
    if (!formDef || loading || submittedKey || !draftKey || typeof window === 'undefined') return;
    try {
      localStorage.setItem(draftKey, JSON.stringify(values));
    } catch {
      // ignore quota error
    }
  }, [values, formDef, loading, submittedKey, draftKey]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formDef) return;
    const issues = requiredFormErrors(formDef.columns, formDef.table.formRules || [], values);
    if (issues.length) {
      const nextErrs: Record<string, string> = {};
      for (const issue of issues) {
        if (issue.field && !nextErrs[issue.field]) nextErrs[issue.field] = issue.message;
      }
      setFieldErrors(nextErrs);
      setErrorMsg(`Vui lòng kiểm tra lại ${issues.length} trường bắt buộc bên dưới.`);
      const firstCol = formDef.columns.find((c) => nextErrs[c.columnCode]);
      if (firstCol && typeof document !== 'undefined') {
        const el = document.getElementById(`public-field-${firstCol.id}`);
        el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
        el?.focus();
      }
      return;
    }
    setFieldErrors({});
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
        setHasRestoredDraft(false);
        if (typeof window !== 'undefined' && draftKey) {
          localStorage.removeItem(draftKey);
        }
      } else {
        setErrorMsg(res.message || 'Không thể gửi biểu mẫu.');
      }
    } catch {
      setErrorMsg('Lỗi kết nối máy chủ.');
    } finally {
      setSubmitting(false);
    }
  };

  const updateFieldValue = (columnCode: string, nextVal: unknown) => {
    setValues((prev) => {
      const updated = { ...prev, [columnCode]: nextVal };
      return formDef
        ? restoreProtectedFormValues(formDef.columns, formDef.table.formRules || [], updated)
        : updated;
    });
    if (fieldErrors[columnCode]) {
      setFieldErrors((prev) => {
        const copy = { ...prev };
        delete copy[columnCode];
        return copy;
      });
    }
  };

  const accent = formDef?.app?.color || '#1a73e8';
  const states = evaluateFormRules(formDef?.columns || [], formDef?.table.formRules || [], values);

  return (
    <div
      style={{
        minHeight: '100vh',
        background: 'var(--bg-primary)',
        color: 'var(--text-primary)',
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
            <div style={{ display: 'flex', gap: 8, justifyContent: 'center', flexWrap: 'wrap' }}>
              <button
                type="button"
                className="btn btn-outline btn-sm"
                onClick={() => {
                  const key = submittedKey;
                  setSubmittedKey(null);
                  setTrackCode(key);
                  setMode('track');
                  void runTrackLookup(key);
                }}
              >
                <SearchRoundedIcon sx={{ fontSize: 15 }} /> Tra cứu trạng thái
              </button>
              <button type="button" className="btn btn-primary btn-sm" onClick={() => setSubmittedKey(null)}>
                Gửi phiếu khác
              </button>
            </div>
          </div>
        ) : (
          <>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12, marginBottom: 16 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
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

              <div className="segmented-tabs">
                <button
                  type="button"
                  className={`segmented-tab ${mode === 'submit' ? 'active' : ''}`}
                  onClick={() => setMode('submit')}
                >
                  Nộp biểu mẫu
                </button>
                <button
                  type="button"
                  className={`segmented-tab ${mode === 'track' ? 'active' : ''}`}
                  onClick={() => setMode('track')}
                >
                  Tra cứu tiến độ
                </button>
              </div>
            </div>

            {(formDef.table.publicFormExpiresAt || formDef.table.publicFormMaxSubmissions) && mode === 'submit' && (
              <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginBottom: 12 }}>
                {formDef.table.publicFormExpiresAt && (
                  <span>Hạn tiếp nhận: <strong>{new Date(formDef.table.publicFormExpiresAt).toLocaleString('vi-VN')}</strong></span>
                )}
                {formDef.table.publicFormExpiresAt && formDef.table.publicFormMaxSubmissions ? ' • ' : ''}
                {formDef.table.publicFormMaxSubmissions && (
                  <span>Đã tiếp nhận: <strong>{formDef.table.recordCount || 0}/{formDef.table.publicFormMaxSubmissions}</strong></span>
                )}
              </div>
            )}

            {mode === 'track' ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 12, paddingTop: 6 }}>
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    void runTrackLookup();
                  }}
                  style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}
                >
                  <input
                    className="form-input"
                    style={{ flex: 1, minWidth: 200 }}
                    placeholder="Nhập mã tiếp nhận hồ sơ (VD: REC-2026-0001)..."
                    value={trackCode}
                    onChange={(e) => setTrackCode(e.target.value)}
                    required
                  />
                  <button type="submit" className="btn btn-primary btn-sm" style={{ backgroundColor: accent }} disabled={trackLoading}>
                    <SearchRoundedIcon sx={{ fontSize: 16 }} />
                    {trackLoading ? 'Đang tra cứu...' : 'Tra cứu'}
                  </button>
                </form>

                {trackError && (
                  <div style={{ padding: '10px 12px', borderRadius: 8, backgroundColor: 'var(--danger-bg)', color: 'var(--danger-text)', fontSize: 13 }}>
                    {trackError}
                  </div>
                )}

                {trackResult && (
                  <div style={{ padding: 16, borderRadius: 12, backgroundColor: 'var(--bg-secondary)', border: '1px solid var(--border-subtle)', display: 'flex', flexDirection: 'column', gap: 8 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
                      <code style={{ fontSize: 13.5, fontWeight: 700 }}>{trackResult.recordKey}</code>
                      <span className="badge badge-info">
                        {TRACK_STATUS_LABEL[trackResult.approvalStatus?.toUpperCase()] || trackResult.approvalStatus}
                      </span>
                    </div>
                    {trackResult.currentStepName && (
                      <div style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
                        Bước xử lý hiện tại: <strong>{trackResult.currentStepName}</strong>
                      </div>
                    )}
                    <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                      Ngày nộp: {new Date(trackResult.createdAt).toLocaleString('vi-VN')} • Cập nhật: {new Date(trackResult.updatedAt).toLocaleString('vi-VN')}
                    </div>
                  </div>
                )}
              </div>
            ) : (
              <>

            {hasRestoredDraft && (
              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: 10,
                  padding: '8px 12px',
                  borderRadius: 8,
                  marginBottom: 14,
                  backgroundColor: 'var(--info-bg)',
                  border: '1px solid var(--info-border)',
                  color: 'var(--info-text)',
                  fontSize: 12.5,
                }}
              >
                <span>Đã khôi phục bản nháp chưa gửi trên trình duyệt này.</span>
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  onClick={() => {
                    if (formDef) setValues(formDefaultValues(formDef.columns));
                    setHasRestoredDraft(false);
                    setFieldErrors({});
                    if (typeof window !== 'undefined' && draftKey) localStorage.removeItem(draftKey);
                  }}
                >
                  Xóa nháp
                </button>
              </div>
            )}

            {errorMsg && (
              <div role="alert" className="badge badge-danger" style={{ padding: '8px 12px', borderRadius: 8, marginBottom: 14, width: '100%' }}>
                {errorMsg}
              </div>
            )}

            <form noValidate onSubmit={handleSubmit} className="public-form-grid">
              {formDef.columns
                .filter((c) => !c.formula && c.dataType.toUpperCase() !== 'AUTO_NUMBER' && states[c.columnCode]?.visible)
                .map((col) => {
                  const dt = (col.dataType || 'STRING').toUpperCase();
                  const required = states[col.columnCode].required;
                  const readOnly = states[col.columnCode].readOnly || col.readOnlyForRoles?.some(r => r.toUpperCase() === 'STAFF');
                  const catOpts = col.refCategoryCode ? formDef.categories[col.refCategoryCode] || [] : [];
                  const fullSpan = dt === 'TEXTAREA' || dt === 'SUB_TABLE' || dt === 'FILE' || dt === 'MULTI_FILE' || dt === 'IMAGE' || dt === 'SIGNATURE';
                  const fieldErr = fieldErrors[col.columnCode];
                  const inputId = `public-field-input-${col.id}`;
                  return (
                    <div key={col.id} id={`public-field-${col.id}`} className="form-group" style={{ gridColumn: fullSpan ? '1 / -1' : undefined, marginBottom: 2 }}>
                      <label htmlFor={inputId} className="form-label">
                        {col.columnName} {required && <span style={{ color: '#d93025' }}>*</span>}
                      </label>
                      <DynamicFieldInput
                        inputId={inputId}
                        required={required}
                        column={col}
                        value={values[col.columnCode]}
                        disabled={readOnly}
                        formData={values}
                        publicFormTableId={tableId}
                        publicCategories={catOpts}
                        onChange={(nextVal) => updateFieldValue(col.columnCode, nextVal)}
                        onSelectDetail={(opt) => {
                          if (!opt.data) return;
                          setValues((prev) => {
                            const next = { ...prev };
                            for (const targetCol of formDef.columns) {
                              if (
                                targetCol.columnCode === col.columnCode ||
                                targetCol.formula ||
                                targetCol.dataType.toUpperCase() === 'AUTO_NUMBER' ||
                                targetCol.readOnlyForRoles?.some((r) => r.toUpperCase() === 'STAFF')
                              ) continue;
                              const cur = next[targetCol.columnCode];
                              if (cur === undefined || cur === null || cur === '') {
                                const srcVal = opt.data?.[targetCol.columnCode];
                                if (srcVal !== undefined && srcVal !== null && srcVal !== '') {
                                  next[targetCol.columnCode] = srcVal;
                                }
                              }
                            }
                            return restoreProtectedFormValues(formDef.columns, formDef.table.formRules || [], next);
                          });
                        }}
                      />
                      {fieldErr && (
                        <div role="alert" style={{ fontSize: 11.5, color: '#d93025', marginTop: 4, fontWeight: 500 }}>
                          {fieldErr}
                        </div>
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
          </>
        )}
      </div>
    </div>
  );
}
