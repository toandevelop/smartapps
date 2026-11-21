'use client';

import React, { useState, useEffect, useMemo, useRef } from 'react';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import { DynamicFieldInput, FormLayoutRenderer } from '@/app/components/ui';
import AiOcrButton from '@/app/components/ui/AiOcrButton';
import RelatedChildTabPanel, { ChildDraftRow } from '@/app/components/ui/RelatedChildTabPanel';
import { useAuth } from '@/lib/auth-context';
import { batchFetchLookups } from '@/lib/lookup-service';
import type { AppRecord, ChildRelationMutation, FormLayoutConfig, FormRule, RelatedTableGroup, SysColumn } from '@/lib/types';
import {
  evaluateFormRules,
  prepareFormSubmission,
  requiredFormErrors,
  restoreProtectedFormValues,
  formDefaultValues,
  EMPTY_FORM_RULES,
  EMPTY_FORM_DEFAULTS,
} from '@/lib/form-rules';

/* =========================================================
 * 5. RECORD EDITOR MODAL (DYNAMIC FORM)
 * ========================================================= */
export function RecordEditorModal(props: {
  open: boolean;
  record: AppRecord | null;
  tableId?: string;
  tableName: string;
  columns: SysColumn[];
  formRules?: FormRule[];
  formLayout?: FormLayoutConfig | null;
  defaultOverrides?: Record<string, unknown>;
  lockedFields?: string[];
  saving: boolean;
  onClose: () => void;
  onSave: (data: Record<string, unknown>) => void;
  onSaveWithRelations?: (data: Record<string, unknown>, childMutations: ChildRelationMutation[]) => void | Promise<void>;
}) {
  if (!props.open) return null;
  return (
    <RecordEditorModalBody
      key={props.record?.id || '__new__'}
      record={props.record}
      tableId={props.tableId}
      tableName={props.tableName}
      columns={props.columns}
      formRules={props.formRules}
      formLayout={props.formLayout}
      defaultOverrides={props.defaultOverrides}
      lockedFields={props.lockedFields}
      saving={props.saving}
      onClose={props.onClose}
      onSave={props.onSave}
      onSaveWithRelations={props.onSaveWithRelations}
    />
  );
}

function RecordEditorModalBody({
  record,
  tableId,
  tableName,
  columns,
  defaultOverrides = EMPTY_FORM_DEFAULTS,
  formRules = EMPTY_FORM_RULES,
  formLayout,
  lockedFields = [],
  saving,
  onClose,
  onSave,
  onSaveWithRelations,
}: {
  record: AppRecord | null;
  tableId?: string;
  tableName: string;
  columns: SysColumn[];
  formRules?: FormRule[];
  formLayout?: FormLayoutConfig | null;
  defaultOverrides?: Record<string, unknown>;
  lockedFields?: string[];
  saving: boolean;
  onClose: () => void;
  onSave: (data: Record<string, unknown>) => void;
  onSaveWithRelations?: (data: Record<string, unknown>, childMutations: ChildRelationMutation[]) => void | Promise<void>;
}) {
  const { user } = useAuth();
  const initialFormData = useMemo(() => {
    const base: Record<string, unknown> = record
      ? { ...(record.data || {}) }
      : (() => {
          const init: Record<string, unknown> = { ...formDefaultValues(columns) };
          Object.entries(defaultOverrides).forEach(([k, v]) => {
            if (v !== undefined && v !== null && v !== '') init[k] = v;
          });
          return init;
        })();
    return formRules.length
      ? restoreProtectedFormValues(columns, formRules, base, record?.data, defaultOverrides)
      : base;
  }, [record, columns, formRules, defaultOverrides]);
  const initialSnapshot = useMemo(() => JSON.stringify(initialFormData), [initialFormData]);

  const [formData, setFormData] = useState<Record<string, unknown>>(initialFormData);
  const [fieldErrors, setFieldErrors] = useState<{ field: string; message: string }[]>([]);
  const [activeTab, setActiveTab] = useState<string>('main');
  const [relatedGroups, setRelatedGroups] = useState<RelatedTableGroup[]>([]);
  const [childDrafts, setChildDrafts] = useState<Record<string, ChildDraftRow[]>>({});
  const [loadingRelations, setLoadingRelations] = useState(false);
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);
  const errorRef = useRef<HTMLDivElement>(null);
  const states = evaluateFormRules(columns, formRules, formData);
  const oldStates = record ? evaluateFormRules(columns, formRules, record.data || {}) : {};
  useEffect(() => { if (fieldErrors.length) errorRef.current?.focus(); }, [fieldErrors]);
  const userRoles = user?.roleCodes?.length ? user.roleCodes : (user?.roleCode ? [user.roleCode] : []);
  const isUserAdmin = Boolean(user?.isAdmin || userRoles.some((r) => r.toUpperCase() === 'ADMIN'));

  const updateFormData = (updater: (prev: Record<string, unknown>) => Record<string, unknown>) => {
    setFieldErrors([]);
    setFormData((prev) => {
      const next = updater(prev);
      return formRules.length
        ? restoreProtectedFormValues(columns, formRules, next, record?.data, defaultOverrides)
        : next;
    });
  };

  useEffect(() => {
    let cancelled = false;
    void Promise.resolve().then(() => {
      if (cancelled) return;
      if (!tableId) {
        setRelatedGroups([]);
        setChildDrafts({});
        return;
      }
      setLoadingRelations(true);
      const qs = new URLSearchParams({ tableId });
      if (record?.id) qs.set('recordId', record.id);
      fetch(`/api/records/table-relations?${qs.toString()}`, { credentials: 'include' })
        .then((r) => r.json())
        .then(async (res) => {
          if (cancelled) return;
          const groups: RelatedTableGroup[] = res?.success && Array.isArray(res.data) ? res.data : [];
          const initDrafts: Record<string, ChildDraftRow[]> = {};
          groups.forEach((g) => {
            const gKey = `${g.tableId}::${g.viaColumnCode || ''}`;
            initDrafts[gKey] = (g.records || []).map((rec) => ({
              localKey: rec.id,
              id: rec.id,
              version: rec.version,
              data: { ...(rec.data || {}) },
              status: 'existing',
              approvalStatus: rec.approvalStatus,
            }));
          });
          setRelatedGroups(groups);
          setChildDrafts(initDrafts);
          const childCols = groups.flatMap((g) => g.columns);
          if (childCols.some((c) => c.refTableId)) {
            await batchFetchLookups(childCols);
          }
        })
        .catch(() => {
          if (!cancelled) {
            setRelatedGroups([]);
            setChildDrafts({});
          }
        })
        .finally(() => {
          if (!cancelled) setLoadingRelations(false);
        });
    });
    return () => { cancelled = true; };
  }, [tableId, record?.id]);

  const hasRelatedTabs = relatedGroups.length > 0;

  const requestClose = () => {
    if (saving) return;
    const isDirty =
      JSON.stringify(formData) !== initialSnapshot ||
      Object.values(childDrafts).some((rows) => rows.some((r) => r.status !== 'existing'));
    if (isDirty && !showDiscardConfirm) {
      setShowDiscardConfirm(true);
      return;
    }
    setShowDiscardConfirm(false);
    onClose();
  };

  return (
    <div className="modal-overlay" onClick={requestClose}>
      <div
        className="modal-dialog"
        style={hasRelatedTabs ? { maxWidth: 980, width: '95vw' } : undefined}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
          <div>
            <h3 style={{ fontSize: 17, fontWeight: 700 }}>
              {record ? `Chỉnh sửa bản ghi (${record.recordKey || record.id})` : `Thêm mới — ${tableName}`}
            </h3>
            <p style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              Form tự sinh theo cấu trúc động • Hỗ trợ Lookup đa App, Công thức tự tính &amp; Bảng phụ liên kết
            </p>
          </div>
          <button type="button" className="btn btn-outline btn-xs" aria-label="Đóng" onClick={requestClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        {activeTab === 'main' && tableId && (
          <AiOcrButton
            tableId={tableId}
            onExtracted={(res) => {
              updateFormData((prev) => {
                const next: Record<string, unknown> = { ...prev };
                for (const [k, v] of Object.entries(res.fields || {})) {
                  if (v !== null && v !== undefined && v !== '') next[k] = v;
                }
                for (const [subKey, rows] of Object.entries(res.subTables || {})) {
                  if (Array.isArray(rows) && rows.length > 0) next[subKey] = rows;
                }
                return next;
              });
            }}
          />
        )}

        {(hasRelatedTabs || loadingRelations) && (
          <div
            style={{
              display: 'flex',
              gap: 6,
              overflowX: 'auto',
              paddingBottom: 10,
              marginBottom: 14,
              borderBottom: '1px solid var(--border-light)',
            }}
          >
            <button
              type="button"
              className={`btn btn-xs ${activeTab === 'main' ? 'btn-primary' : 'btn-outline'}`}
              onClick={() => setActiveTab('main')}
            >
              Thông tin chính
              {fieldErrors.length > 0 && (
                <span style={{ marginLeft: 6, padding: '0 5px', borderRadius: 99, background: '#ef4444', color: '#fff', fontSize: 10 }}>
                  {fieldErrors.length}
                </span>
              )}
            </button>
            {relatedGroups.map((g) => {
              const gKey = `${g.tableId}::${g.viaColumnCode || ''}`;
              const rows = childDrafts[gKey] || [];
              const activeCount = rows.filter((r) => r.status !== 'deleted').length;
              const hasPending = rows.some((r) => r.status !== 'existing');
              return (
                <button
                  key={gKey}
                  type="button"
                  className={`btn btn-xs ${activeTab === gKey ? 'btn-primary' : 'btn-outline'}`}
                  onClick={() => setActiveTab(gKey)}
                  style={{ whiteSpace: 'nowrap' }}
                >
                  {g.tableDisplayName} ({activeCount})
                  {hasPending && (
                    <span
                      title="Có thay đổi chưa lưu"
                      style={{
                        display: 'inline-block',
                        width: 6,
                        height: 6,
                        borderRadius: '50%',
                        background: activeTab === gKey ? '#fde047' : '#f59e0b',
                        marginLeft: 6,
                      }}
                    />
                  )}
                </button>
              );
            })}
            {loadingRelations && (
              <span style={{ fontSize: 11.5, color: 'var(--text-muted)', alignSelf: 'center', marginLeft: 4 }}>
                Đang tải bảng phụ...
              </span>
            )}
          </div>
        )}

        <form
          onSubmit={(e) => {
            e.preventDefault();
            const issues = formRules.length ? requiredFormErrors(columns, formRules, formData, record?.data, defaultOverrides) : [];
            if (issues.length) {
              setFieldErrors(issues);
              setActiveTab('main');
              return;
            }
            const prepared = prepareFormSubmission(columns, formRules, formData, record?.data, defaultOverrides);
            if (onSaveWithRelations) {
              const mutations: ChildRelationMutation[] = [];
              for (const g of relatedGroups) {
                if (!g.moduleId || !g.viaColumnCode) continue;
                const gKey = `${g.tableId}::${g.viaColumnCode}`;
                const rows = childDrafts[gKey] || [];
                const upserts = rows
                  .filter((r) => r.status === 'new' || r.status === 'edited')
                  .map((r) => ({
                    id: r.id,
                    expectedVersion: r.version,
                    data: r.data,
                  }));
                const deleteIds = rows
                  .filter((r) => r.status === 'deleted' && r.id)
                  .map((r) => r.id!);
                if (upserts.length > 0 || deleteIds.length > 0) {
                  mutations.push({
                    appId: g.appId,
                    moduleId: g.moduleId,
                    tableId: g.tableId,
                    viaColumnCode: g.viaColumnCode,
                    refValueField: g.refValueField || undefined,
                    upserts,
                    deleteIds,
                  });
                }
              }
              onSaveWithRelations(prepared, mutations);
            } else {
              onSave(prepared);
            }
          }}
        >
          {fieldErrors.length > 0 && activeTab === 'main' && <div ref={errorRef} role="alert" tabIndex={-1} style={{ marginBottom: 12 }}>
            <strong>Cần bổ sung thông tin:</strong><ul>{fieldErrors.map(error => <li key={error.field}>{error.message}</li>)}</ul>
          </div>}
          {activeTab !== 'main' ? (() => {
            const activeGroup = relatedGroups.find((g) => `${g.tableId}::${g.viaColumnCode || ''}` === activeTab);
            if (!activeGroup) return null;
            const gKey = `${activeGroup.tableId}::${activeGroup.viaColumnCode || ''}`;
            return (
              <RelatedChildTabPanel
                group={activeGroup}
                rows={childDrafts[gKey] || []}
                onChangeRows={(next) => setChildDrafts((prev) => ({ ...prev, [gKey]: next }))}
              />
            );
          })() : (() => {
            const visibleCols = columns.filter(
              (c) => states[c.columnCode]?.visible && (isUserAdmin || !(c.hiddenForRoles || []).some(r => userRoles.some(ur => ur.toUpperCase() === r.toUpperCase()))),
            );
            const mainCols = visibleCols.filter((c) => !['SUB_TABLE', 'TEXTAREA', 'FILE', 'MULTI_FILE', 'IMAGE', 'SIGNATURE'].includes((c.dataType || '').toUpperCase()));
            const subTableCols = visibleCols.filter((c) => (c.dataType || '').toUpperCase() === 'SUB_TABLE');
            const attachmentNoteCols = visibleCols.filter((c) => ['TEXTAREA', 'FILE', 'MULTI_FILE', 'IMAGE', 'SIGNATURE'].includes((c.dataType || '').toUpperCase()));
            const useSections = subTableCols.length > 0 || (mainCols.length > 0 && attachmentNoteCols.length > 0);

            const renderField = (col: SysColumn) => {
              const isLocked = lockedFields.includes(col.columnCode);
              const isRoleReadOnly = !isUserAdmin && (col.readOnlyForRoles || []).some((rr) =>
                userRoles.some((ur) => ur.toUpperCase() === rr.toUpperCase()),
              );
              const isRuleReadOnly = states[col.columnCode].readOnly || oldStates[col.columnCode]?.readOnly;
              const isDisabled = isLocked || isRoleReadOnly || isRuleReadOnly;
              const required = states[col.columnCode].required;
              return (
                <div
                  key={col.id}
                  className="form-group"
                  style={{
                    marginBottom: 4,
                    gridColumn: col.dataType === 'TEXTAREA' || col.dataType === 'SUB_TABLE' ? '1 / -1' : undefined,
                  }}
                >
                  <label className="form-label">
                    {col.columnName} {required && !isDisabled && <span style={{ color: '#d93025' }}>*</span>}
                    {isLocked && (
                      <span style={{ fontSize: 10.5, color: '#1a73e8', fontWeight: 600, marginLeft: 6 }}>
                        (Tự động gắn tài khoản)
                      </span>
                    )}
                    {!isLocked && isRoleReadOnly && (
                      <span style={{ fontSize: 10.5, color: '#64748b', fontWeight: 600, marginLeft: 6 }}>
                        (Chỉ đọc)
                      </span>
                    )}
                  </label>
                  {isRuleReadOnly && <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>Chỉ đọc theo quy tắc</span>}
                  <DynamicFieldInput
                    column={{ ...col, isRequired: required }}
                    value={formData[col.columnCode]}
                    formData={formData}
                    disabled={isDisabled}
                    onChange={(val) =>
                      updateFormData((prev) => {
                        const next = { ...prev, [col.columnCode]: val };
                        // Auto-clear dependent child lookup fields when parent source field changes
                        for (const dep of columns) {
                          if (dep.lookupSourceField === col.columnCode && dep.columnCode !== col.columnCode) {
                            next[dep.columnCode] = '';
                          }
                        }
                        return next;
                      })
                    }
                    onSelectDetail={(opt) => {
                      if (!opt.data) return;
                      updateFormData((prev) => {
                        const next = { ...prev };
                        for (const c of columns) {
                          const currentStates = evaluateFormRules(columns, formRules, prev);
                          if (!currentStates[c.columnCode]?.visible || currentStates[c.columnCode]?.readOnly || oldStates[c.columnCode]?.readOnly) continue;
                          if (c.columnCode === col.columnCode || c.isIdentityKey || c.formula || lockedFields.includes(c.columnCode)) continue;
                          const cur = next[c.columnCode];
                          const isEmpty = cur === undefined || cur === null || cur === '';
                          if (!isEmpty) continue;
                          const val = opt.data?.[c.columnCode];
                          if (val !== undefined && val !== null && val !== '') {
                            next[c.columnCode] = val;
                          }
                        }
                        return next;
                      });
                    }}
                  />
                </div>
              );
            };

            if (formLayout && formLayout.sections && formLayout.sections.length > 0) {
              const visibleSet = new Set(visibleCols.map((c) => c.columnCode));
              return (
                <FormLayoutRenderer
                  layout={formLayout}
                  columns={columns}
                  isColumnVisible={(c) => visibleSet.has(c.columnCode)}
                  renderField={(c) => renderField(c)}
                  onValidateStep={(stepCols) => {
                    const stepErrs = requiredFormErrors(stepCols, formRules, formData, oldStates);
                    if (stepErrs.length > 0) {
                      setFieldErrors(stepErrs);
                      return false;
                    }
                    setFieldErrors([]);
                    return true;
                  }}
                />
              );
            }

            if (!useSections) {
              return (
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 14 }}>
                  {visibleCols.map(renderField)}
                </div>
              );
            }

            return (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
                {mainCols.length > 0 && (
                  <div className="form-section-card">
                    <div className="form-section-title">
                      Thông tin chung
                    </div>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: 14 }}>
                      {mainCols.map(renderField)}
                    </div>
                  </div>
                )}
                {subTableCols.length > 0 && (
                  <div className="form-section-card" style={{ backgroundColor: 'var(--bg-surface)' }}>
                    <div className="form-section-title">
                      Chi tiết bảng con
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                      {subTableCols.map(renderField)}
                    </div>
                  </div>
                )}
                {attachmentNoteCols.length > 0 && (
                  <div className="form-section-card">
                    <div className="form-section-title">
                      Tệp đính kèm &amp; Ghi chú
                    </div>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: 14 }}>
                      {attachmentNoteCols.map(renderField)}
                    </div>
                  </div>
                )}
              </div>
            );
          })()}

          {showDiscardConfirm && (
            <div
              role="alert"
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 12,
                marginTop: 16,
                padding: '10px 14px',
                borderRadius: 10,
                background: 'var(--warning-bg)',
                border: '1px solid var(--warning-border)',
                color: 'var(--warning-text)',
                fontSize: 13,
              }}
            >
              <span>Bạn có thay đổi chưa lưu. Xác nhận đóng và bỏ các thay đổi này?</span>
              <div style={{ display: 'flex', gap: 8, flexShrink: 0 }}>
                <button type="button" className="btn btn-outline btn-xs" onClick={() => setShowDiscardConfirm(false)}>
                  Tiếp tục sửa
                </button>
                <button
                  type="button"
                  className="btn btn-danger btn-xs"
                  onClick={() => {
                    setShowDiscardConfirm(false);
                    onClose();
                  }}
                >
                  Bỏ thay đổi
                </button>
              </div>
            </div>
          )}

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20, paddingTop: 14, borderTop: '1px solid var(--border-light)' }}>
            <button type="button" className="btn btn-outline" onClick={requestClose}>Hủy</button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              <SaveRoundedIcon sx={{ fontSize: 16 }} /> {saving ? 'Đang lưu...' : 'Lưu bản ghi'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
