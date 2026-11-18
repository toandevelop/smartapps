'use client';

import React, { useState, useEffect, useRef } from 'react';
import * as XLSX from 'xlsx';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import CloudUploadRoundedIcon from '@mui/icons-material/CloudUploadRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import { ClusterIcon, APP_ICON_OPTIONS, DynamicFieldInput } from '@/app/components/ui';
import RelatedChildTabPanel, { ChildDraftRow } from '@/app/components/ui/RelatedChildTabPanel';
import { APP_COLOR_PRESETS } from '@/lib/design-tokens';
import { usePlatform } from '@/lib/app-context';
import { useAuth } from '@/lib/auth-context';
import { batchFetchLookups } from '@/lib/lookup-service';
import { AppAutomation, ChildRelationMutation, RelatedTableGroup, SysApp, SysCategory, SysColumn, SysModule, SysRole, SysTable, AppRecord, AppTemplateSummary, FormRule } from '@/lib/types';
import FormRulesEditor from './FormRulesEditor';
import { evaluateFormRules, prepareFormSubmission, requiredFormErrors, restoreProtectedFormValues, formDefaultValues, EMPTY_FORM_RULES, EMPTY_FORM_DEFAULTS } from '@/lib/form-rules';
import { parseSubTableSchema, serializeSubTableSchema, SubTableColumnDef, SubTableDataType } from '@/lib/sub-table';

/* =========================================================
 * 1. APP MODAL
 * ========================================================= */
export interface AppFormState {
  id: string;
  code: string;
  name: string;
  description: string;
  department: string;
  icon: string;
  color: string;
  bgColor: string;
  version: string;
  orderIndex: number;
}

export function AppModal({
  open,
  editing,
  initial,
  saving,
  onClose,
  onSave,
}: {
  open: boolean;
  editing: SysApp | null;
  initial: AppFormState;
  saving: boolean;
  onClose: () => void;
  onSave: (form: AppFormState) => void;
}) {
  const [form, setForm] = useState<AppFormState>(initial);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setForm(initial);
      setErr(null);
    }
  }, [open, initial]);

  if (!open) return null;

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!form.code.trim() || !form.name.trim()) {
      setErr('Vui lòng nhập Mã App và Tên App.');
      return;
    }
    onSave(form);
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
          <h3 style={{ fontSize: 17, fontWeight: 700 }}>
            {editing ? `Chỉnh sửa App: ${editing.name}` : 'Tạo Ứng dụng Công ty Mới'}
          </h3>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        {err && <div className="badge badge-danger" style={{ marginBottom: 12, width: '100%', padding: '8px 12px', borderRadius: 8 }}>{err}</div>}

        <form onSubmit={submit}>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Mã App (Code) *</label>
              <input
                className="form-input"
                value={form.code}
                disabled={Boolean(editing)}
                onChange={(e) => setForm({ ...form, code: e.target.value.toUpperCase().replace(/[^A-Z0-9_]/g, '') })}
                placeholder="VD: CRM, KPI, SALE"
              />
            </div>
            <div className="form-group">
              <label className="form-label">Tên Ứng dụng *</label>
              <input
                className="form-input"
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                placeholder="VD: Quản lý Khách hàng (CRM)"
              />
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Phòng ban phụ trách</label>
              <input
                className="form-input"
                value={form.department}
                onChange={(e) => setForm({ ...form, department: e.target.value })}
                placeholder="VD: Khối Kinh doanh"
              />
            </div>
            <div className="form-group">
              <label className="form-label">Phiên bản</label>
              <input
                className="form-input"
                value={form.version}
                onChange={(e) => setForm({ ...form, version: e.target.value })}
              />
            </div>
          </div>

          <div className="form-group">
            <label className="form-label">Biểu tượng & Màu chủ đạo (Google M3)</label>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 10 }}>
              {APP_COLOR_PRESETS.map((p) => (
                <button
                  key={p.color}
                  type="button"
                  onClick={() => setForm({ ...form, color: p.color, bgColor: p.bg })}
                  style={{
                    width: 28,
                    height: 28,
                    borderRadius: '50%',
                    backgroundColor: p.color,
                    border: form.color === p.color ? '3px solid #202124' : '2px solid #ffffff',
                    boxShadow: '0 1px 3px rgba(0,0,0,0.2)',
                    cursor: 'pointer',
                  }}
                  title={p.label}
                />
              ))}
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(135px, 1fr))', gap: 6 }}>
              {APP_ICON_OPTIONS.slice(0, 8).map((opt) => {
                const selected = form.icon === opt.value;
                return (
                  <button
                    key={opt.value}
                    type="button"
                    onClick={() => setForm({ ...form, icon: opt.value })}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 6,
                      padding: '6px 8px',
                      borderRadius: 8,
                      border: `1px solid ${selected ? form.color : 'var(--border-subtle)'}`,
                      backgroundColor: selected ? form.bgColor : '#fff',
                      color: selected ? form.color : 'var(--text-secondary)',
                      fontSize: 11.5,
                      fontWeight: 600,
                      cursor: 'pointer',
                    }}
                  >
                    <ClusterIcon icon={opt.value} fontSize={16} />
                    <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{opt.value}</span>
                  </button>
                );
              })}
            </div>
          </div>

          <div className="form-group">
            <label className="form-label">Mô tả</label>
            <textarea
              className="form-textarea"
              rows={2}
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
            />
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 18 }}>
            <button type="button" className="btn btn-outline" onClick={onClose}>
              Hủy
            </button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              <SaveRoundedIcon sx={{ fontSize: 16 }} />
              {saving ? 'Đang lưu...' : 'Lưu Ứng dụng'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

/* =========================================================
 * 2. MODULE MODAL
 * ========================================================= */
export interface ModuleFormState {
  id: string;
  appId: string;
  code: string;
  name: string;
  icon: string;
  orderIndex: number;
  description: string;
}

export function ModuleModal({
  open,
  editing,
  initial,
  saving,
  onClose,
  onSave,
}: {
  open: boolean;
  editing: SysModule | null;
  initial: ModuleFormState;
  saving: boolean;
  onClose: () => void;
  onSave: (form: ModuleFormState) => void;
}) {
  const [form, setForm] = useState<ModuleFormState>(initial);
  useEffect(() => {
    if (open) setForm(initial);
  }, [open, initial]);

  if (!open) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 520 }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
          <h3 style={{ fontSize: 17, fontWeight: 700 }}>{editing ? 'Sửa Phân hệ' : 'Thêm Phân hệ Mới'}</h3>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (!form.code.trim() || !form.name.trim()) return;
            onSave(form);
          }}
        >
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Mã Phân hệ *</label>
              <input
                className="form-input"
                value={form.code}
                onChange={(e) => setForm({ ...form, code: e.target.value.toUpperCase() })}
                placeholder="VD: KHACHHANG"
              />
            </div>
            <div className="form-group">
              <label className="form-label">Tên Phân hệ *</label>
              <input
                className="form-input"
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                placeholder="VD: Quản lý Khách hàng"
              />
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Biểu tượng</label>
              <select
                className="form-select"
                value={form.icon}
                onChange={(e) => setForm({ ...form, icon: e.target.value })}
              >
                {APP_ICON_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.value} - {o.label}
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Thứ tự</label>
              <input
                type="number"
                className="form-input"
                value={form.orderIndex}
                onChange={(e) => setForm({ ...form, orderIndex: Number(e.target.value) || 1 })}
              />
            </div>
          </div>

          <div className="form-group">
            <label className="form-label">Mô tả</label>
            <textarea
              className="form-textarea"
              rows={2}
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
            />
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            <button type="button" className="btn btn-outline" onClick={onClose}>Hủy</button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              <SaveRoundedIcon sx={{ fontSize: 16 }} /> {saving ? 'Đang lưu...' : 'Lưu Phân hệ'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

/* =========================================================
 * 3. TABLE MODAL
 * ========================================================= */
export interface TableFormState {
  id: string;
  appId: string;
  moduleId: string;
  tableName: string;
  displayName: string;
  description: string;
  primaryView: string;
  rowScope: string;
  approvalEnabled: boolean;
  approvalField: string;
  approverRoles: string[];
  publicFormEnabled: boolean;
  orderIndex: number;
  formRules?: FormRule[];
}

export function TableModal({
  open,
  editing,
  initial,
  allRoles = [],
  saving,
  onClose,
  onSave,
}: {
  open: boolean;
  editing: SysTable | null;
  initial: TableFormState;
  allRoles?: SysRole[];
  saving: boolean;
  onClose: () => void;
  onSave: (form: TableFormState) => void;
}) {
  const [form, setForm] = useState<TableFormState>(initial);
  const [ruleColumns, setRuleColumns] = useState<SysColumn[]>([]);
  const [loadingRuleColumns, setLoadingRuleColumns] = useState(false);
  const [ruleColumnError, setRuleColumnError] = useState('');
  useEffect(() => {
    if (open) setForm(initial);
  }, [open, editing?.id]);
  useEffect(() => {
    setRuleColumns([]); setRuleColumnError(''); setLoadingRuleColumns(false);
    if (!open || !editing?.id) return;
    const controller = new AbortController();
    setLoadingRuleColumns(true);
    fetch(`/api/columns?tableId=${encodeURIComponent(editing.id)}`, { credentials: 'include', signal: controller.signal })
      .then(async response => {
        const result = await response.json();
        if (!response.ok || !result.success) throw new Error(result.message || 'Không tải được cấu trúc cột.');
        if (!controller.signal.aborted) setRuleColumns(result.data || []);
      })
      .catch(error => { if (!controller.signal.aborted) setRuleColumnError(error instanceof Error ? error.message : 'Lỗi tải cấu trúc cột.'); })
      .finally(() => { if (!controller.signal.aborted) setLoadingRuleColumns(false); });
    return () => controller.abort();
  }, [open, editing?.id]);

  if (!open) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 960 }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
          <h3 style={{ fontSize: 17, fontWeight: 700 }}>{editing ? 'Sửa Bảng Động' : 'Tạo Bảng Động Mới'}</h3>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (!form.tableName.trim() || !form.displayName.trim()) return;
            onSave(form);
          }}
        >
          <div className="form-group">
            <label className="form-label">Tên Bảng Kỹ Thuật (snake_case) *</label>
            <input
              className="form-input"
              value={form.tableName}
              onChange={(e) => setForm({ ...form, tableName: e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_') })}
              placeholder="VD: crm_khach_hang"
            />
          </div>
          <div className="form-group">
            <label className="form-label">Tên Hiển Thị Giao Diện *</label>
            <input
              className="form-input"
              value={form.displayName}
              onChange={(e) => setForm({ ...form, displayName: e.target.value })}
              placeholder="VD: Danh sách Khách hàng Tiềm năng"
            />
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Chế độ hiển thị mặc định</label>
              <select
                className="form-select"
                value={form.primaryView}
                onChange={(e) => setForm({ ...form, primaryView: e.target.value })}
              >
                <option value="table">Bảng động (Dynamic Table)</option>
                <option value="kanban">Kanban Board</option>
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Thứ tự</label>
              <input
                type="number"
                className="form-input"
                value={form.orderIndex}
                onChange={(e) => setForm({ ...form, orderIndex: Number(e.target.value) || 1 })}
              />
            </div>
          </div>
          <div className="form-group">
            <label className="form-label">Phạm vi dữ liệu (Row-Level Security)</label>
            <select
              className="form-select"
              value={form.rowScope || 'ALL'}
              onChange={(e) => setForm({ ...form, rowScope: e.target.value })}
            >
              <option value="ALL">ALL — Mọi user có quyền truy cập App đều xem được</option>
              <option value="DEPARTMENT">DEPARTMENT — Xem bản ghi thuộc phòng ban của mình</option>
              <option value="OWNER_ONLY">OWNER_ONLY — Nhân viên (STAFF) chỉ xem được bản ghi của mình</option>
            </select>
          </div>
          <div style={{ padding: 12, borderRadius: 10, backgroundColor: '#f8fafc', border: '1px solid var(--border-light)', marginBottom: 12 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, fontWeight: 700, cursor: 'pointer', marginBottom: form.approvalEnabled ? 10 : 0 }}>
              <input
                type="checkbox"
                checked={Boolean(form.approvalEnabled)}
                onChange={(e) => setForm({ ...form, approvalEnabled: e.target.checked })}
              />
              Bật quy trình Phê duyệt Đa cấp (Approval Workflow)
            </label>
            {form.approvalEnabled && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                <div>
                  <label className="form-label">Trường đồng bộ trạng thái (tùy chọn)</label>
                  <input
                    className="form-input"
                    value={form.approvalField || 'trang_thai_duyet'}
                    onChange={(e) => setForm({ ...form, approvalField: e.target.value })}
                    placeholder="trang_thai_duyet"
                  />
                </div>
                <div>
                  <div style={{ fontSize: 11.5, color: 'var(--text-secondary)', marginBottom: 6 }}>
                    Vai trò được duyệt bảng này (để trống = mọi vai trò có quyền <code>record.approve</code> của App):
                  </div>
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10 }}>
                    {(allRoles.length > 0
                      ? allRoles.map((r) => ({ code: r.code, name: r.name }))
                      : ['ADMIN', 'HR_MANAGER', 'ASSET_LEAD'].map((code) => ({ code, name: code }))
                    ).map((role) => {
                      const checked = (form.approverRoles || []).includes(role.code);
                      return (
                        <label key={role.code} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, cursor: 'pointer' }}>
                          <input
                            type="checkbox"
                            checked={checked}
                            onChange={(e) => {
                              const prev = form.approverRoles || [];
                              const next = e.target.checked
                                ? Array.from(new Set([...prev, role.code]))
                                : prev.filter((r) => r !== role.code);
                              setForm({ ...form, approverRoles: next });
                            }}
                          />
                          <span>{role.code}</span>
                          {role.name !== role.code && (
                            <span style={{ color: 'var(--text-muted)', fontSize: 11 }}>({role.name})</span>
                          )}
                        </label>
                      );
                    })}
                  </div>
                </div>
              </div>
            )}
          </div>
          <div style={{ padding: 10, borderRadius: 10, backgroundColor: '#f0f6ff', border: '1px solid #d2e3fc', marginBottom: 12 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, fontWeight: 700, cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={Boolean(form.publicFormEnabled)}
                onChange={(e) => setForm({ ...form, publicFormEnabled: e.target.checked })}
              />
              Mở Biểu mẫu Công khai (Public Form không cần đăng nhập)
            </label>
            {form.publicFormEnabled && form.id && (
              <div style={{ fontSize: 11.5, color: '#1a73e8', marginTop: 4 }}>
                Đường dẫn chia sẻ: <code>/forms/{form.id}</code>
              </div>
            )}
          </div>
          {loadingRuleColumns && <p role="status">Đang tải các trường để cấu hình quy tắc...</p>}
          {ruleColumnError && <p role="alert">{ruleColumnError}</p>}
          {!loadingRuleColumns && !ruleColumnError && (
            <FormRulesEditor columns={ruleColumns} rules={form.formRules || []} onChange={formRules => setForm({ ...form, formRules })} />
          )}
          <div className="form-group">
            <label className="form-label">Mô tả</label>
            <textarea
              className="form-textarea"
              rows={2}
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
            />
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            <button type="button" className="btn btn-outline" onClick={onClose}>Hủy</button>
            <button type="submit" className="btn btn-primary" disabled={saving || loadingRuleColumns || Boolean(ruleColumnError)}>
              <SaveRoundedIcon sx={{ fontSize: 16 }} /> {saving ? 'Đang lưu...' : 'Lưu Bảng Động'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

/* =========================================================
 * 4. COLUMN MODAL (Supports Cross-App LOOKUP!)
 * ========================================================= */
export interface ColumnFormState {
  id: string;
  tableId: string;
  columnCode: string;
  columnName: string;
  excelHeader: string;
  dataType: string;
  isRequired: boolean;
  isIdentityKey: boolean;
  showInTable: boolean;
  showInForm: boolean;
  showInParentForm?: boolean;
  width: number;
  validationRegex: string;
  refCategoryCode: string;
  allowQuickCreate: boolean;
  refAppId: string;
  refTableId: string;
  refValueField: string;
  refDisplayField: string;
  formula: string;
  defaultValue: string;
  hiddenForRoles: string[];
  readOnlyForRoles: string[];
  orderIndex: number;
}

const REGEX_PRESETS = [
  { label: 'Email hợp lệ', val: '^[^\\s@]+@[^\\s@]+\\.[^\\s@]{2,}$' },
  { label: 'Số ĐT VN (10-11 số)', val: '^(0|\\+84)[0-9]{9,10}$' },
  { label: 'CCCD 12 số', val: '^[0-9]{12}$' },
  { label: 'Chỉ chữ số', val: '^[0-9]+$' },
];

export function ColumnModal({
  open,
  editing,
  initial,
  categories,
  allApps,
  allTables,
  allRoles = [],
  saving,
  onClose,
  onSave,
}: {
  open: boolean;
  editing: SysColumn | null;
  initial: ColumnFormState;
  categories: SysCategory[];
  allApps: SysApp[];
  allTables: SysTable[];
  allRoles?: SysRole[];
  saving: boolean;
  onClose: () => void;
  onSave: (form: ColumnFormState) => void;
}) {
  const [form, setForm] = useState<ColumnFormState>(initial);
  const [targetCols, setTargetCols] = useState<SysColumn[]>([]);

  useEffect(() => {
    if (open) setForm(initial);
  }, [open, initial]);

  useEffect(() => {
    if (!form.refTableId) {
      setTargetCols([]);
      return;
    }
    fetch(`/api/columns?tableId=${encodeURIComponent(form.refTableId)}`)
      .then((r) => r.json())
      .then((res) => {
        if (res.success && Array.isArray(res.data)) {
          setTargetCols(res.data);
          if (!form.refValueField && res.data.length > 0) {
            const keyCol = res.data.find((c: SysColumn) => c.isIdentityKey) || res.data[0];
            const dispCol = res.data[1] || keyCol;
            setForm((prev) => ({
              ...prev,
              refValueField: prev.refValueField || keyCol.columnCode,
              refDisplayField: prev.refDisplayField || dispCol.columnCode,
            }));
          }
        }
      });
  }, [form.refTableId]);

  if (!open) return null;

  const filteredTables = form.refAppId
    ? allTables.filter((t) => t.appId === form.refAppId)
    : allTables;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
          <h3 style={{ fontSize: 17, fontWeight: 700 }}>{editing ? `Sửa Cột: ${editing.columnName}` : 'Thêm Trường Dữ liệu Động'}</h3>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (!form.columnCode.trim() || !form.columnName.trim()) return;
            onSave(form);
          }}
        >
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Mã trường (JSON Key) *</label>
              <input
                className="form-input"
                value={form.columnCode}
                onChange={(e) => setForm({ ...form, columnCode: e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_') })}
                placeholder="VD: nguoi_phu_trach"
              />
            </div>
            <div className="form-group">
              <label className="form-label">Tên hiển thị cột *</label>
              <input
                className="form-input"
                value={form.columnName}
                onChange={(e) => setForm({ ...form, columnName: e.target.value, excelHeader: form.excelHeader || e.target.value })}
                placeholder="VD: Nhân viên phụ trách"
              />
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 100px 90px', gap: 10 }}>
            <div className="form-group">
              <label className="form-label">Kiểu dữ liệu</label>
              <select
                className="form-select"
                value={form.dataType}
                onChange={(e) => setForm({ ...form, dataType: e.target.value })}
              >
                <option value="STRING">Văn bản (STRING)</option>
                <option value="NUMBER">Số (NUMBER)</option>
                <option value="CURRENCY">Tiền tệ VNĐ (CURRENCY)</option>
                <option value="DATE">Ngày tháng (DATE)</option>
                <option value="BOOLEAN">Đúng / Sai (BOOLEAN)</option>
                <option value="SELECT">Danh mục (SELECT)</option>
                <option value="LOOKUP">Liên kết Đa App (LOOKUP)</option>
                <option value="EMAIL">Email (EMAIL)</option>
                <option value="TEL">Điện thoại (TEL)</option>
                <option value="TEXTAREA">Đoạn văn dài (TEXTAREA)</option>
                <option value="FILE">Tệp đính kèm (FILE)</option>
                <option value="MULTI_FILE">Nhiều tệp đính kèm (MULTI_FILE)</option>
                <option value="IMAGE">Hình ảnh (IMAGE)</option>
                <option value="MULTI_SELECT">Chọn nhiều danh mục (MULTI_SELECT)</option>
                <option value="USER">Người dùng hệ thống (USER)</option>
                <option value="SIGNATURE">Chữ ký tay (SIGNATURE)</option>
                <option value="SUB_TABLE">Bảng con Master–Detail (SUB_TABLE)</option>
                <option value="AUTO_NUMBER">Sinh mã tự động (AUTO_NUMBER)</option>
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Danh mục dùng chung</label>
              <select
                className="form-select"
                value={form.refCategoryCode}
                onChange={(e) =>
                  setForm({
                    ...form,
                    refCategoryCode: e.target.value,
                    dataType: e.target.value ? 'SELECT' : form.dataType,
                  })
                }
              >
                <option value="">-- Không liên kết --</option>
                {categories.map((c) => (
                  <option key={c.categoryCode} value={c.categoryCode}>
                    {c.categoryCode} ({c.categoryName})
                  </option>
                ))}
              </select>
              {form.refCategoryCode && (
                <label style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 6, fontSize: 12, color: 'var(--text-secondary)', cursor: 'pointer' }}>
                  <input
                    type="checkbox"
                    checked={Boolean(form.allowQuickCreate)}
                    onChange={(e) => setForm({ ...form, allowQuickCreate: e.target.checked })}
                  />
                  <span>Cho phép thêm nhanh mục danh mục ngay tại ô nhập</span>
                </label>
              )}
            </div>
            <div className="form-group">
              <label className="form-label">Rộng (px)</label>
              <input
                type="number"
                className="form-input"
                value={form.width}
                onChange={(e) => setForm({ ...form, width: Number(e.target.value) || 160 })}
              />
            </div>
            <div className="form-group">
              <label className="form-label">Thứ tự</label>
              <input
                type="number"
                className="form-input"
                value={form.orderIndex}
                onChange={(e) => setForm({ ...form, orderIndex: Number(e.target.value) || 1 })}
              />
            </div>
          </div>

          {form.dataType === 'LOOKUP' && (
            <div style={{ padding: 12, borderRadius: 10, backgroundColor: '#e0f2fe', border: '1px solid #bae6fd', marginBottom: 14 }}>
              <div style={{ fontSize: 12.5, fontWeight: 700, color: '#0369a1', marginBottom: 8 }}>
                Cấu hình Liên kết Dữ liệu Đa App (Cross-App Lookup)
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                <div className="form-group" style={{ marginBottom: 6 }}>
                  <label className="form-label">App nguồn</label>
                  <select
                    className="form-select"
                    value={form.refAppId}
                    onChange={(e) => setForm({ ...form, refAppId: e.target.value, refTableId: '' })}
                  >
                    <option value="">-- Chọn App --</option>
                    {allApps.map((a) => (
                      <option key={a.id} value={a.id}>{a.code} — {a.name}</option>
                    ))}
                  </select>
                </div>
                <div className="form-group" style={{ marginBottom: 6 }}>
                  <label className="form-label">Bảng đích</label>
                  <select
                    className="form-select"
                    value={form.refTableId}
                    onChange={(e) => setForm({ ...form, refTableId: e.target.value })}
                  >
                    <option value="">-- Chọn Bảng --</option>
                    {filteredTables.map((t) => (
                      <option key={t.id} value={t.id}>{t.displayName} ({t.tableName})</option>
                    ))}
                  </select>
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Trường lưu giá trị (Key)</label>
                  <select
                    className="form-select"
                    value={form.refValueField}
                    onChange={(e) => setForm({ ...form, refValueField: e.target.value })}
                  >
                    <option value="">-- Chọn trường --</option>
                    {targetCols.map((c) => (
                      <option key={c.id} value={c.columnCode}>{c.columnCode} ({c.columnName})</option>
                    ))}
                  </select>
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Trường hiển thị nhãn</label>
                  <select
                    className="form-select"
                    value={form.refDisplayField}
                    onChange={(e) => setForm({ ...form, refDisplayField: e.target.value })}
                  >
                    <option value="">-- Chọn trường --</option>
                    {targetCols.map((c) => (
                      <option key={c.id} value={c.columnCode}>{c.columnCode} ({c.columnName})</option>
                    ))}
                  </select>
                </div>
              </div>
              <label style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 10, fontSize: 12.5, cursor: 'pointer' }}>
                <input
                  type="checkbox"
                  checked={Boolean(form.showInParentForm)}
                  onChange={(e) => setForm({ ...form, showInParentForm: e.target.checked })}
                />
                <span>Hiện tab nhập kèm bảng này trong Form Thêm/Sửa của bảng cha (Master-Detail)</span>
              </label>
            </div>
          )}

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Công thức (VD: so_luong * don_gia)</label>
              <input
                className="form-input"
                value={form.formula}
                onChange={(e) => setForm({ ...form, formula: e.target.value })}
                placeholder="VD: IF(tong>1000,'Cao','Thấp')"
              />
            </div>
            <div className="form-group">
              <label className="form-label">
                {form.dataType === 'SUB_TABLE'
                  ? 'Cấu hình cột bảng con (Schema)'
                  : form.dataType === 'AUTO_NUMBER'
                  ? 'Tiền tố sinh mã ({YYYY})'
                  : 'Giá trị mặc định'}
              </label>
              {form.dataType === 'SUB_TABLE' ? (
                (() => {
                  const subCols = parseSubTableSchema(form.defaultValue);
                  const updateSubCols = (next: SubTableColumnDef[]) =>
                    setForm({ ...form, defaultValue: serializeSubTableSchema(next) });
                  return (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                      {subCols.map((sc, idx) => (
                        <div key={idx} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 110px 1fr 56px 28px', gap: 4, alignItems: 'center' }}>
                          <input
                            className="form-input"
                            style={{ height: 30, fontSize: 12 }}
                            placeholder="Mã cột"
                            value={sc.code}
                            onChange={(e) => {
                              const copy = [...subCols];
                              copy[idx] = { ...sc, code: e.target.value };
                              updateSubCols(copy);
                            }}
                          />
                          <input
                            className="form-input"
                            style={{ height: 30, fontSize: 12 }}
                            placeholder="Tên hiển thị"
                            value={sc.name}
                            onChange={(e) => {
                              const copy = [...subCols];
                              copy[idx] = { ...sc, name: e.target.value };
                              updateSubCols(copy);
                            }}
                          />
                          <select
                            className="form-select"
                            style={{ height: 30, fontSize: 12 }}
                            value={sc.dataType}
                            onChange={(e) => {
                              const copy = [...subCols];
                              copy[idx] = { ...sc, dataType: e.target.value as SubTableDataType };
                              updateSubCols(copy);
                            }}
                          >
                            <option value="STRING">Chữ</option>
                            <option value="NUMBER">Số</option>
                            <option value="CURRENCY">Tiền</option>
                            <option value="DATE">Ngày</option>
                            <option value="BOOLEAN">Đúng/Sai</option>
                            <option value="EMAIL">Email</option>
                          </select>
                          <input
                            className="form-input"
                            style={{ height: 30, fontSize: 12 }}
                            placeholder="Công thức (VD: so_luong*don_gia)"
                            value={sc.formula || ''}
                            onChange={(e) => {
                              const copy = [...subCols];
                              copy[idx] = { ...sc, formula: e.target.value };
                              updateSubCols(copy);
                            }}
                          />
                          <label style={{ fontSize: 11, display: 'flex', alignItems: 'center', gap: 3 }}>
                            <input
                              type="checkbox"
                              checked={Boolean(sc.required)}
                              onChange={(e) => {
                                const copy = [...subCols];
                                copy[idx] = { ...sc, required: e.target.checked };
                                updateSubCols(copy);
                              }}
                            />
                            Bắt buộc
                          </label>
                          <button
                            type="button"
                            className="btn btn-outline btn-xs"
                            style={{ color: '#d93025', height: 28, padding: 0 }}
                            disabled={subCols.length <= 1}
                            onClick={() => updateSubCols(subCols.filter((_, i) => i !== idx))}
                          >
                            ×
                          </button>
                        </div>
                      ))}
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        style={{ alignSelf: 'flex-start' }}
                        onClick={() =>
                          updateSubCols([
                            ...subCols,
                            { code: `cot_${subCols.length + 1}`, name: `Cột ${subCols.length + 1}`, dataType: 'STRING' },
                          ])
                        }
                      >
                        + Thêm cột con
                      </button>
                    </div>
                  );
                })()
              ) : (
                <input
                  className="form-input"
                  value={form.defaultValue}
                  onChange={(e) => setForm({ ...form, defaultValue: e.target.value })}
                  placeholder={form.dataType === 'AUTO_NUMBER' ? 'CP-{YYYY}-' : ''}
                />
              )}
            </div>
            <div className="form-group">
              <label className="form-label">Regex kiểm tra hợp lệ</label>
              <div style={{ display: 'flex', gap: 6 }}>
                <input
                  className="form-input"
                  value={form.validationRegex}
                  onChange={(e) => setForm({ ...form, validationRegex: e.target.value })}
                  placeholder="VD: ^[0-9]{10}$"
                />
                <select
                  className="form-select"
                  style={{ width: 110 }}
                  value=""
                  onChange={(e) => e.target.value && setForm({ ...form, validationRegex: e.target.value })}
                >
                  <option value="">Mẫu nhanh</option>
                  {REGEX_PRESETS.map((p) => (
                    <option key={p.label} value={p.val}>{p.label}</option>
                  ))}
                </select>
              </div>
            </div>
          </div>

          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 18, marginBottom: 16, padding: '10px 14px', background: '#f8f9fa', borderRadius: 10 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
              <input type="checkbox" checked={form.isRequired} onChange={(e) => setForm({ ...form, isRequired: e.target.checked })} />
              Bắt buộc nhập
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
              <input type="checkbox" checked={form.isIdentityKey} onChange={(e) => setForm({ ...form, isIdentityKey: e.target.checked })} />
              Khóa định danh
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
              <input type="checkbox" checked={form.showInTable} onChange={(e) => setForm({ ...form, showInTable: e.target.checked })} />
              Hiện trên bảng
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
              <input type="checkbox" checked={form.showInForm} onChange={(e) => setForm({ ...form, showInForm: e.target.checked })} />
              Hiện trên form
            </label>
          </div>

          <div className="form-group" style={{ marginBottom: 12 }}>
            <label className="form-label">Ẩn trường dữ liệu với vai trò (Field-Level Security)</label>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, padding: '8px 12px', background: '#fff8e1', borderRadius: 10, border: '1px solid #ffe082' }}>
              {(allRoles.length > 0
                ? allRoles.filter((r) => r.code.toUpperCase() !== 'ADMIN').map((r) => ({ code: r.code, name: r.name }))
                : ['STAFF', 'ASSET_LEAD', 'HR_MANAGER'].map((code) => ({ code, name: code }))
              ).map((role) => {
                const hidden = (form.hiddenForRoles || []).includes(role.code);
                return (
                  <label key={role.code} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5, cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      checked={hidden}
                      onChange={(e) => {
                        const prev = form.hiddenForRoles || [];
                        const next = e.target.checked
                          ? Array.from(new Set([...prev, role.code]))
                          : prev.filter((r) => r !== role.code);
                        setForm({ ...form, hiddenForRoles: next });
                      }}
                    />
                    Ẩn với {role.code}
                  </label>
                );
              })}
            </div>
          </div>

          <div className="form-group" style={{ marginBottom: 16 }}>
            <label className="form-label">Khóa chỉ đọc với vai trò (Cho xem nhưng không cho sửa)</label>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, padding: '8px 12px', background: '#f0f9ff', borderRadius: 10, border: '1px solid #bae6fd' }}>
              {(allRoles.length > 0
                ? allRoles.filter((r) => r.code.toUpperCase() !== 'ADMIN').map((r) => ({ code: r.code, name: r.name }))
                : ['STAFF', 'ASSET_LEAD', 'HR_MANAGER'].map((code) => ({ code, name: code }))
              ).map((role) => {
                const ro = (form.readOnlyForRoles || []).includes(role.code);
                return (
                  <label key={`ro_${role.code}`} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5, cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      checked={ro}
                      onChange={(e) => {
                        const prev = form.readOnlyForRoles || [];
                        const next = e.target.checked
                          ? Array.from(new Set([...prev, role.code]))
                          : prev.filter((r) => r !== role.code);
                        setForm({ ...form, readOnlyForRoles: next });
                      }}
                    />
                    Chỉ đọc với {role.code}
                  </label>
                );
              })}
            </div>
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            <button type="button" className="btn btn-outline" onClick={onClose}>Hủy</button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              <SaveRoundedIcon sx={{ fontSize: 16 }} /> {saving ? 'Đang lưu...' : 'Lưu Cột Động'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

/* =========================================================
 * 5. RECORD EDITOR MODAL (DYNAMIC FORM)
 * ========================================================= */
export function RecordEditorModal({
  open,
  record,
  tableId,
  tableName,
  columns,
  defaultOverrides = EMPTY_FORM_DEFAULTS,
  formRules = EMPTY_FORM_RULES,
  lockedFields = [],
  saving,
  onClose,
  onSave,
  onSaveWithRelations,
}: {
  open: boolean;
  record: AppRecord | null;
  tableId?: string;
  tableName: string;
  columns: SysColumn[];
  formRules?: FormRule[];
  defaultOverrides?: Record<string, any>;
  lockedFields?: string[];
  saving: boolean;
  onClose: () => void;
  onSave: (data: Record<string, any>) => void;
  onSaveWithRelations?: (data: Record<string, any>, childMutations: ChildRelationMutation[]) => void | Promise<void>;
}) {
  const { user } = useAuth();
  const [formData, setFormData] = useState<Record<string, any>>({});
  const [fieldErrors, setFieldErrors] = useState<{ field: string; message: string }[]>([]);
  const [activeTab, setActiveTab] = useState<string>('main');
  const [relatedGroups, setRelatedGroups] = useState<RelatedTableGroup[]>([]);
  const [childDrafts, setChildDrafts] = useState<Record<string, ChildDraftRow[]>>({});
  const [loadingRelations, setLoadingRelations] = useState(false);
  const errorRef = useRef<HTMLDivElement>(null);
  const states = evaluateFormRules(columns, formRules, formData);
  const oldStates = record ? evaluateFormRules(columns, formRules, record.data || {}) : {};
  useEffect(() => { setFieldErrors([]); }, [formData]);
  useEffect(() => { if (fieldErrors.length) errorRef.current?.focus(); }, [fieldErrors]);
  const userRoles = user?.roleCodes?.length ? user.roleCodes : (user?.roleCode ? [user.roleCode] : []);
  const isUserAdmin = Boolean(user?.isAdmin || userRoles.some((r) => r.toUpperCase() === 'ADMIN'));

  useEffect(() => {
    if (!open) return;
    setActiveTab('main');
    if (record) {
      setFormData({ ...(record.data || {}) });
    } else {
      const init: Record<string, any> = { ...formDefaultValues(columns) };
      Object.entries(defaultOverrides).forEach(([k, v]) => {
        if (v !== undefined && v !== null && v !== '') init[k] = v;
      });
      setFormData(init);
    }
  }, [open, record, columns, defaultOverrides]);

  useEffect(() => {
    if (!open || !tableId) {
      setRelatedGroups([]);
      setChildDrafts({});
      return;
    }
    let cancelled = false;
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
    return () => { cancelled = true; };
  }, [open, tableId, record?.id]);

  useEffect(() => {
    if (!open || !formRules.length) return;
    setFormData(current => restoreProtectedFormValues(columns, formRules, current, record?.data, defaultOverrides));
  }, [open, columns, formRules, formData, record, defaultOverrides]);

  if (!open) return null;

  const hasRelatedTabs = relatedGroups.length > 0;

  return (
    <div className="modal-overlay" onClick={onClose}>
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
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

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
                    onChange={(val) => setFormData((prev) => ({ ...prev, [col.columnCode]: val }))}
                    onSelectDetail={(opt) => {
                      if (!opt.data) return;
                      setFormData((prev) => {
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

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20, paddingTop: 14, borderTop: '1px solid var(--border-light)' }}>
            <button type="button" className="btn btn-outline" onClick={onClose}>Hủy</button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              <SaveRoundedIcon sx={{ fontSize: 16 }} /> {saving ? 'Đang lưu...' : 'Lưu bản ghi'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

/* =========================================================
 * 6. EXCEL TO APP WIZARD MODAL (30s App Builder)
 * ========================================================= */
export function ExcelToAppModal({
  open,
  onClose,
  onCreated,
}: {
  open: boolean;
  onClose: () => void;
  onCreated: (app: SysApp) => void;
}) {
  const [appCode, setAppCode] = useState('');
  const [appName, setAppName] = useState('');
  const [department, setDepartment] = useState('Toàn công ty');
  const [sheets, setSheets] = useState<Array<{ sheetName: string; rows: Record<string, any>[] }>>([]);
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    if (open) {
      setAppCode('');
      setAppName('');
      setSheets([]);
    }
  }, [open]);

  if (!open) return null;

  const handleFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const buf = await file.arrayBuffer();
    const wb = XLSX.read(buf);
    const parsed = wb.SheetNames.map((name) => ({
      sheetName: name,
      rows: XLSX.utils.sheet_to_json<Record<string, any>>(wb.Sheets[name]),
    })).filter((s) => s.rows.length > 0);
    setSheets(parsed);
    if (!appName) setAppName(file.name.replace(/\.(xlsx|xls|csv)$/i, ''));
    if (!appCode) setAppCode('XLS_' + Math.floor(100 + Math.random() * 900));
  };

  const handleGenerate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!appCode.trim() || !appName.trim() || sheets.length === 0) return;
    setCreating(true);
    try {
      const res = await fetch('/api/apps/from-excel', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ appCode, appName, department, sheets }),
      }).then((r) => r.json());
      if (res.success) {
        onCreated(res.data);
      }
    } finally {
      setCreating(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
          <h3 style={{ fontSize: 17, fontWeight: 700 }}>Sinh Ứng dụng Siêu tốc từ File Excel (.xlsx)</h3>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        <form onSubmit={handleGenerate}>
          <div className="form-group">
            <label className="form-label">1. Chọn file Excel dữ liệu của công ty (.xlsx)</label>
            <input type="file" accept=".xlsx,.xls" onChange={handleFile} className="form-input" style={{ paddingTop: 7 }} />
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Mã App *</label>
              <input className="form-input" value={appCode} onChange={(e) => setAppCode(e.target.value.toUpperCase().replace(/[^A-Z0-9_]/g, ''))} />
            </div>
            <div className="form-group">
              <label className="form-label">Tên App *</label>
              <input className="form-input" value={appName} onChange={(e) => setAppName(e.target.value)} />
            </div>
          </div>

          <div className="form-group">
            <label className="form-label">Phòng ban phụ trách</label>
            <input className="form-input" value={department} onChange={(e) => setDepartment(e.target.value)} />
          </div>

          {sheets.length > 0 && (
            <div style={{ padding: 12, borderRadius: 10, backgroundColor: '#f8f9fa', border: '1px solid var(--border-light)', marginBottom: 16 }}>
              <div style={{ fontSize: 12.5, fontWeight: 700, marginBottom: 6 }}>
                Sẽ tự động tạo {sheets.length} bảng động & nạp dữ liệu:
              </div>
              {sheets.map((s) => (
                <div key={s.sheetName} style={{ fontSize: 12.5, color: 'var(--text-secondary)', marginBottom: 3 }}>
                  • Sheet <strong>{s.sheetName}</strong>: {Object.keys(s.rows[0] || {}).length} cột, {s.rows.length} dòng
                </div>
              ))}
            </div>
          )}

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            <button type="button" className="btn btn-outline" onClick={onClose}>Hủy</button>
            <button type="submit" className="btn btn-primary" disabled={creating || sheets.length === 0}>
              <CloudUploadRoundedIcon sx={{ fontSize: 16 }} /> {creating ? 'Đang sinh App...' : 'Tạo App & Import Ngay'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

/* =========================================================
 * 7. TEMPLATE GALLERY MODAL (1-Click Enterprise App Store)
 * ========================================================= */
export function TemplateInstallModal({
  open,
  onClose,
  onInstalled,
  onInstalledMany,
}: {
  open: boolean;
  onClose: () => void;
  onInstalled: (app: SysApp) => void;
  onInstalledMany?: (apps: SysApp[]) => void;
}) {
  const { apps } = usePlatform();
  const [templates, setTemplates] = useState<AppTemplateSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [installing, setInstalling] = useState<string | null>(null);
  const [batchInstalling, setBatchInstalling] = useState(false);
  const [search, setSearch] = useState('');
  const [activeCategory, setActiveCategory] = useState('ALL');
  const [includeSampleData, setIncludeSampleData] = useState(true);
  const [selectedKeys, setSelectedKeys] = useState<string[]>([]);
  const [customOpenKey, setCustomOpenKey] = useState<string | null>(null);
  const [customCode, setCustomCode] = useState('');
  const [customName, setCustomName] = useState('');
  const [errorMsg, setErrorMsg] = useState('');

  useEffect(() => {
    if (!open) return;
    setErrorMsg('');
    setSelectedKeys([]);
    setCustomOpenKey(null);
    setLoading(true);
    fetch('/api/apps/templates', { credentials: 'include' })
      .then((r) => r.json())
      .then((res) => {
        if (res.success && Array.isArray(res.data)) {
          setTemplates(res.data);
        } else {
          setErrorMsg(res.message || 'Không thể tải thư viện App mẫu.');
        }
      })
      .catch(() => setErrorMsg('Lỗi kết nối máy chủ khi tải App mẫu.'))
      .finally(() => setLoading(false));
  }, [open]);

  if (!open) return null;

  const installedCodeSet = new Set(apps.map((a) => a.code.toUpperCase()));
  const categories = ['ALL', ...Array.from(new Set(templates.map((t) => t.category)))];

  const filtered = templates.filter((t) => {
    if (activeCategory !== 'ALL' && t.category !== activeCategory) return false;
    if (!search.trim()) return true;
    const q = search.trim().toLowerCase();
    return (
      t.name.toLowerCase().includes(q) ||
      t.code.toLowerCase().includes(q) ||
      t.description.toLowerCase().includes(q) ||
      t.department.toLowerCase().includes(q) ||
      t.features.some((f) => f.toLowerCase().includes(q))
    );
  });

  const toggleSelect = (key: string) => {
    setSelectedKeys((prev) => (prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key]));
  };

  const install = async (templateKey: string, useCustom = false) => {
    setInstalling(templateKey);
    setErrorMsg('');
    try {
      const payload: Record<string, unknown> = {
        templateKey,
        includeSampleData,
      };
      if (useCustom) {
        if (customCode.trim()) payload.customCode = customCode.trim().toUpperCase();
        if (customName.trim()) payload.customName = customName.trim();
      }
      const res = await fetch('/api/apps/from-template', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(payload),
      }).then((r) => r.json());
      if (res.success && res.data) {
        onInstalled(res.data);
      } else {
        setErrorMsg(res.message || 'Không thể cài đặt App mẫu.');
      }
    } catch {
      setErrorMsg('Lỗi kết nối khi cài đặt App mẫu.');
    } finally {
      setInstalling(null);
    }
  };

  const installSelectedBatch = async () => {
    if (selectedKeys.length === 0) return;
    setBatchInstalling(true);
    setErrorMsg('');
    const created: SysApp[] = [];
    try {
      for (const key of selectedKeys) {
        const res = await fetch('/api/apps/from-template', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({ templateKey: key, includeSampleData }),
        }).then((r) => r.json());
        if (res.success && res.data) {
          created.push(res.data);
        } else {
          setErrorMsg(res.message || `Cài đặt mẫu ${key} thất bại.`);
          break;
        }
      }
      if (created.length > 0) {
        if (onInstalledMany) onInstalledMany(created);
        else onInstalled(created[created.length - 1]);
      }
    } finally {
      setBatchInstalling(false);
    }
  };

  const busy = Boolean(installing) || batchInstalling;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-dialog"
        style={{ maxWidth: 1020, width: '95vw', maxHeight: '90vh', display: 'flex', flexDirection: 'column' }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12, marginBottom: 12 }}>
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <AutoAwesomeRoundedIcon sx={{ fontSize: 22, color: '#1a73e8' }} />
              <h3 style={{ fontSize: 18, fontWeight: 700, margin: 0 }}>
                Thư viện App Mẫu Doanh nghiệp (1-Click)
              </h3>
              <span className="badge badge-info">{templates.length} mẫu chuẩn công ty</span>
            </div>
            <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', margin: '4px 0 0' }}>
              Cài nhanh các ứng dụng doanh nghiệp phổ biến kèm Phân hệ, Bảng động, Công thức, Liên kết HRM, Dashboard Widget &amp; Dữ liệu mẫu.
            </p>
          </div>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        {/* Search & Filter Toolbar */}
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'center', marginBottom: 10 }}>
          <div style={{ position: 'relative', flex: '1 1 240px', maxWidth: 320 }}>
            <SearchRoundedIcon
              sx={{ fontSize: 17, position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--text-muted)' }}
            />
            <input
              className="form-input"
              style={{ paddingLeft: 34, height: 34, fontSize: 13 }}
              placeholder="Tìm CRM, Kho, Mua hàng, Dự án, OKR..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>

          <label
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 6,
              fontSize: 12.5,
              color: 'var(--text-primary)',
              cursor: 'pointer',
              backgroundColor: '#f8f9fa',
              padding: '6px 10px',
              borderRadius: 8,
              border: '1px solid var(--border-light)',
            }}
          >
            <input
              type="checkbox"
              checked={includeSampleData}
              onChange={(e) => setIncludeSampleData(e.target.checked)}
            />
            Tạo kèm dữ liệu mẫu &amp; Dashboard dùng thử ngay
          </label>
        </div>

        {/* Category Filter Pills */}
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginBottom: 14 }}>
          {categories.map((cat) => {
            const active = activeCategory === cat;
            const count = cat === 'ALL' ? templates.length : templates.filter((t) => t.category === cat).length;
            return (
              <button
                key={cat}
                type="button"
                className={active ? 'btn btn-primary btn-xs' : 'btn btn-outline btn-xs'}
                onClick={() => setActiveCategory(cat)}
              >
                {cat === 'ALL' ? `Tất cả (${count})` : `${cat} (${count})`}
              </button>
            );
          })}
        </div>

        {errorMsg && (
          <div className="badge badge-danger" style={{ padding: '8px 12px', marginBottom: 10 }}>
            {errorMsg}
          </div>
        )}

        {/* Template Grid */}
        <div style={{ flex: 1, overflowY: 'auto', paddingRight: 4 }}>
          {loading ? (
            <div style={{ textAlign: 'center', padding: 40, color: 'var(--text-muted)', fontSize: 13 }}>
              Đang tải danh sách App mẫu doanh nghiệp...
            </div>
          ) : filtered.length === 0 ? (
            <div style={{ textAlign: 'center', padding: 40, color: 'var(--text-muted)', fontSize: 13 }}>
              Không tìm thấy App mẫu phù hợp với từ khóa &ldquo;{search}&rdquo;.
            </div>
          ) : (
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(290px, 1fr))', gap: 14 }}>
              {filtered.map((tpl) => {
                const isInstalled = installedCodeSet.has(tpl.code.toUpperCase());
                const isSelected = selectedKeys.includes(tpl.templateKey);
                const isCustomOpen = customOpenKey === tpl.templateKey;
                return (
                  <div
                    key={tpl.templateKey}
                    className="card"
                    style={{
                      padding: 15,
                      borderTop: `4px solid ${tpl.color}`,
                      display: 'flex',
                      flexDirection: 'column',
                      justifyContent: 'space-between',
                      gap: 10,
                      outline: isSelected ? `2px solid ${tpl.color}` : undefined,
                      backgroundColor: isSelected ? '#fbfdff' : '#ffffff',
                    }}
                  >
                    <div>
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8, marginBottom: 8 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
                          <div
                            style={{
                              width: 38,
                              height: 38,
                              borderRadius: 10,
                              backgroundColor: tpl.bgColor,
                              color: tpl.color,
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'center',
                              flexShrink: 0,
                            }}
                          >
                            <ClusterIcon icon={tpl.icon} fontSize={20} />
                          </div>
                          <div style={{ minWidth: 0 }}>
                            <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--text-primary)', lineHeight: 1.25 }}>
                              {tpl.name}
                            </div>
                            <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 2 }}>
                              {tpl.department}
                            </div>
                          </div>
                        </div>

                        <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexShrink: 0 }}>
                          {isInstalled && (
                            <span
                              className="badge badge-success"
                              style={{ fontSize: 10.5, padding: '2px 7px' }}
                              title="Ứng dụng có mã này đã có trên hệ thống"
                            >
                              <CheckCircleRoundedIcon sx={{ fontSize: 12 }} /> Đã cài
                            </span>
                          )}
                          <input
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => toggleSelect(tpl.templateKey)}
                            title="Chọn để cài đặt hàng loạt"
                            style={{ cursor: 'pointer', width: 15, height: 15 }}
                          />
                        </div>
                      </div>

                      <p style={{ fontSize: 12.2, color: 'var(--text-secondary)', lineHeight: 1.45, margin: '0 0 8px' }}>
                        {tpl.description}
                      </p>

                      {tpl.highlights.length > 0 && (
                        <ul style={{ margin: '0 0 8px', paddingLeft: 16, fontSize: 11.5, color: 'var(--text-muted)', lineHeight: 1.45 }}>
                          {tpl.highlights.slice(0, 2).map((h, i) => (
                            <li key={i}>{h}</li>
                          ))}
                        </ul>
                      )}

                      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 5, marginBottom: 8 }}>
                        <span className="badge" style={{ backgroundColor: tpl.bgColor, color: tpl.color, fontSize: 10.5 }}>
                          {tpl.code}
                        </span>
                        {tpl.features.map((f) => (
                          <span
                            key={f}
                            className="badge"
                            style={{ backgroundColor: '#f1f3f4', color: '#3c4043', fontSize: 10.5, fontWeight: 500 }}
                          >
                            {f}
                          </span>
                        ))}
                      </div>
                    </div>

                    <div>
                      <div
                        style={{
                          fontSize: 11.5,
                          color: 'var(--text-muted)',
                          backgroundColor: '#f8f9fa',
                          padding: '6px 9px',
                          borderRadius: 8,
                          marginBottom: 8,
                          display: 'flex',
                          justifyContent: 'space-between',
                        }}
                      >
                        <span><strong>{tpl.moduleCount}</strong> phân hệ • <strong>{tpl.tableCount}</strong> bảng</span>
                        <span><strong>{tpl.sampleRecordCount}</strong> mẫu • <strong>{tpl.widgetCount}</strong> KPI</span>
                      </div>

                      {isCustomOpen && (
                        <div style={{ padding: 10, borderRadius: 8, backgroundColor: '#f8f9fa', border: '1px solid var(--border-light)', marginBottom: 8 }}>
                          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1.4fr', gap: 6, marginBottom: 6 }}>
                            <input
                              className="form-input"
                              style={{ height: 30, fontSize: 12 }}
                              placeholder={`Mã (${tpl.code})`}
                              value={customCode}
                              onChange={(e) => setCustomCode(e.target.value.toUpperCase().replace(/[^A-Z0-9_]/g, ''))}
                            />
                            <input
                              className="form-input"
                              style={{ height: 30, fontSize: 12 }}
                              placeholder="Tên tùy chỉnh..."
                              value={customName}
                              onChange={(e) => setCustomName(e.target.value)}
                            />
                          </div>
                          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 6 }}>
                            <button type="button" className="btn btn-outline btn-xs" onClick={() => setCustomOpenKey(null)}>
                              Đóng
                            </button>
                            <button
                              type="button"
                              className="btn btn-primary btn-xs"
                              style={{ backgroundColor: tpl.color }}
                              disabled={busy}
                              onClick={() => install(tpl.templateKey, true)}
                            >
                              Cài theo tên mới
                            </button>
                          </div>
                        </div>
                      )}

                      <div style={{ display: 'flex', gap: 6 }}>
                        <button
                          type="button"
                          className="btn btn-primary btn-sm"
                          style={{ flex: 1, backgroundColor: tpl.color }}
                          disabled={busy}
                          onClick={() => install(tpl.templateKey)}
                        >
                          <AutoAwesomeRoundedIcon sx={{ fontSize: 15 }} />
                          {installing === tpl.templateKey
                            ? 'Đang cài...'
                            : isInstalled
                            ? 'Cài thêm bản sao'
                            : 'Cài App ngay'}
                        </button>
                        <button
                          type="button"
                          className="btn btn-outline btn-sm"
                          title="Tùy chỉnh mã App & Tên App trước khi cài"
                          disabled={busy}
                          onClick={() => {
                            if (isCustomOpen) {
                              setCustomOpenKey(null);
                            } else {
                              setCustomOpenKey(tpl.templateKey);
                              setCustomCode(isInstalled ? `${tpl.code}_2` : tpl.code);
                              setCustomName(tpl.name);
                            }
                          }}
                        >
                          <TuneRoundedIcon sx={{ fontSize: 15 }} />
                        </button>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>

        {/* Footer Bar */}
        <div
          style={{
            marginTop: 14,
            paddingTop: 12,
            borderTop: '1px solid var(--border-light)',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            gap: 10,
            flexWrap: 'wrap',
          }}
        >
          <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
            {selectedKeys.length > 0
              ? `Đã chọn ${selectedKeys.length} App mẫu để cài đặt hàng loạt.`
              : 'Mẹo: Tích chọn nhiều mẫu để cài cùng lúc cho toàn công ty.'}
          </span>
          <div style={{ display: 'flex', gap: 8 }}>
            <button type="button" className="btn btn-outline btn-sm" onClick={onClose} disabled={busy}>
              Đóng
            </button>
            {selectedKeys.length > 0 && (
              <button
                type="button"
                className="btn btn-primary btn-sm"
                disabled={busy}
                onClick={installSelectedBatch}
              >
                <AutoAwesomeRoundedIcon sx={{ fontSize: 15 }} />
                {batchInstalling ? 'Đang cài hàng loạt...' : `Cài ${selectedKeys.length} App đã chọn`}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

/* =========================================================
 * 8. AUTOMATION RULE MODAL
 * ========================================================= */
export function AutomationModal({
  open,
  editing,
  allApps,
  allTables,
  allModules,
  allRoles,
  onClose,
  onSaved,
}: {
  open: boolean;
  editing?: AppAutomation | null;
  allApps: SysApp[];
  allTables: SysTable[];
  allModules: SysModule[];
  allRoles?: SysRole[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [sourceAppId, setSourceAppId] = useState('');
  const [sourceTableId, setSourceTableId] = useState('');
  const [triggerEvent, setTriggerEvent] = useState('RECORD_CREATED');
  const [conditionField, setConditionField] = useState('');
  const [conditionOperator, setConditionOperator] = useState('EQUALS');
  const [conditionValue, setConditionValue] = useState('');
  const [actionType, setActionType] = useState('CREATE_RECORD');
  const [matchSourceField, setMatchSourceField] = useState('');
  const [matchTargetField, setMatchTargetField] = useState('');
  const [targetAppId, setTargetAppId] = useState('');
  const [targetTableId, setTargetTableId] = useState('');
  const [notifyRecipient, setNotifyRecipient] = useState('*');
  const [notifyTemplate, setNotifyTemplate] = useState('Cập nhật bản ghi {{record_key}}');
  const [webhookUrl, setWebhookUrl] = useState('');
  const [webhookSecret, setWebhookSecret] = useState('');
  const [mappingsJson, setMappingsJson] = useState('{\n  "ma_ticket": "{{AUTO_KEY}}",\n  "tieu_de": "Tự động từ {{record_key}}",\n  "han_xu_ly": "{{TODAY}}"\n}');
  // System action structured mappings (CREATE_USER, UPDATE_USER_STATUS, CREATE_TASK)
  const [cuUsername, setCuUsername] = useState('{{EMAIL_PREFIX(email)}}');
  const [cuFullName, setCuFullName] = useState('{{ho_ten}}');
  const [cuEmail, setCuEmail] = useState('{{email}}');
  const [cuEmployeeCode, setCuEmployeeCode] = useState('{{ma_nv}}');
  const [cuDepartment, setCuDepartment] = useState('{{phong_ban}}');
  const [cuRoleCode, setCuRoleCode] = useState('STAFF');
  const [cuPassword, setCuPassword] = useState('123456');
  const [usMatchBy, setUsMatchBy] = useState('employeeCode');
  const [usMatchValue, setUsMatchValue] = useState('{{ma_nv}}');
  const [usIsActive, setUsIsActive] = useState('false');
  const [ctTitle, setCtTitle] = useState('Onboarding nhân sự: {{ho_ten}} ({{ma_nv}})');
  const [ctDescription, setCtDescription] = useState('Chuẩn bị thiết bị, email và bàn giao quy trình cho {{ho_ten}} - {{phong_ban}}.');
  const [ctAssigneeUsername, setCtAssigneeUsername] = useState('admin');
  const [ctPriority, setCtPriority] = useState('MEDIUM');
  const [ctDueDate, setCtDueDate] = useState('{{DATE_ADD(3)}}');
  const [sourceColumns, setSourceColumns] = useState<SysColumn[]>([]);
  const [localRoles, setLocalRoles] = useState<SysRole[]>([]);
  const [errorMsg, setErrorMsg] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!open) return;
    setErrorMsg('');
    if (allRoles && allRoles.length > 0) {
      setLocalRoles(allRoles);
    } else {
      fetch('/api/roles', { credentials: 'include' })
        .then((r) => r.json())
        .then((res) => { if (res.success) setLocalRoles(res.data || []); })
        .catch(() => {});
    }
  }, [open, allRoles]);

  useEffect(() => {
    if (!open || !sourceTableId) {
      setSourceColumns([]);
      return;
    }
    fetch(`/api/columns?tableId=${encodeURIComponent(sourceTableId)}`, { credentials: 'include' })
      .then((r) => r.json())
      .then((res) => { if (res.success) setSourceColumns(res.data || []); })
      .catch(() => {});
  }, [open, sourceTableId]);

  useEffect(() => {
    if (!open) return;
    if (editing) {
      const fm = (editing.fieldMappings || {}) as Record<string, string>;
      setName(editing.name || '');
      setDescription(editing.description || '');
      setSourceAppId(editing.sourceAppId || '');
      setSourceTableId(editing.sourceTableId || '');
      setTriggerEvent(editing.triggerEvent || 'RECORD_CREATED');
      setConditionField(editing.conditionField || '');
      setConditionOperator(editing.conditionOperator || 'EQUALS');
      setConditionValue(editing.conditionValue || '');
      setActionType(editing.actionType || 'CREATE_RECORD');
      setMatchSourceField(editing.matchSourceField || '');
      setMatchTargetField(editing.matchTargetField || '');
      setTargetAppId(editing.targetAppId || '');
      setTargetTableId(editing.targetTableId || '');
      setNotifyRecipient(editing.notifyRecipient || '*');
      setNotifyTemplate(editing.notifyTemplate || 'Cập nhật bản ghi {{record_key}}');
      setWebhookUrl(editing.webhookUrl || '');
      setWebhookSecret(editing.webhookSecret || '');
      setMappingsJson(JSON.stringify(editing.fieldMappings || {}, null, 2));
      setCuUsername(fm.username ?? '{{EMAIL_PREFIX(email)}}');
      setCuFullName(fm.fullName ?? '{{ho_ten}}');
      setCuEmail(fm.email ?? '{{email}}');
      setCuEmployeeCode(fm.employeeCode ?? '{{ma_nv}}');
      setCuDepartment(fm.department ?? '{{phong_ban}}');
      setCuRoleCode(fm.roleCode ?? 'STAFF');
      setCuPassword(fm.password ?? '123456');
      setUsMatchBy(fm.matchBy ?? 'employeeCode');
      setUsMatchValue(fm.matchValue ?? '{{ma_nv}}');
      setUsIsActive(fm.isActive ?? 'false');
      setCtTitle(fm.title ?? 'Onboarding nhân sự: {{ho_ten}} ({{ma_nv}})');
      setCtDescription(fm.description ?? 'Chuẩn bị thiết bị, email và bàn giao quy trình cho {{ho_ten}} - {{phong_ban}}.');
      setCtAssigneeUsername(fm.assigneeUsername ?? 'admin');
      setCtPriority(fm.priority ?? 'MEDIUM');
      setCtDueDate(fm.dueDate ?? '{{DATE_ADD(3)}}');
    } else {
      setName('');
      setDescription('');
      setSourceAppId(allApps[0]?.id || '');
      setSourceTableId('');
      setTriggerEvent('RECORD_CREATED');
      setConditionField('');
      setConditionOperator('EQUALS');
      setConditionValue('');
      setActionType('CREATE_USER');
      setMatchSourceField('');
      setMatchTargetField('');
      setTargetAppId('');
      setTargetTableId('');
      setNotifyRecipient('*');
      setNotifyTemplate('Cập nhật bản ghi {{record_key}}');
      setWebhookUrl('');
      setWebhookSecret('');
      setMappingsJson('{\n  "ma_ticket": "{{AUTO_KEY}}",\n  "tieu_de": "Tự động từ {{record_key}}",\n  "han_xu_ly": "{{TODAY}}"\n}');
      setCuUsername('{{EMAIL_PREFIX(email)}}');
      setCuFullName('{{ho_ten}}');
      setCuEmail('{{email}}');
      setCuEmployeeCode('{{ma_nv}}');
      setCuDepartment('{{phong_ban}}');
      setCuRoleCode('STAFF');
      setCuPassword('123456');
      setUsMatchBy('employeeCode');
      setUsMatchValue('{{ma_nv}}');
      setUsIsActive('false');
      setCtTitle('Onboarding nhân sự: {{ho_ten}} ({{ma_nv}})');
      setCtDescription('Chuẩn bị thiết bị, email và bàn giao quy trình cho {{ho_ten}} - {{phong_ban}}.');
      setCtAssigneeUsername('admin');
      setCtPriority('MEDIUM');
      setCtDueDate('{{DATE_ADD(3)}}');
    }
  }, [open, editing, allApps]);

  if (!open) return null;

  const srcTables = allTables.filter((t) => !sourceAppId || t.appId === sourceAppId);
  const tgtTables = allTables.filter((t) => !targetAppId || t.appId === targetAppId);
  const rolesList = localRoles.length > 0 ? localRoles : (allRoles || []);

  const applyPreset = (preset: 'HRM_CREATE_USER' | 'HRM_LOCK_USER' | 'HRM_ONBOARD_TASK' | 'SCHEDULED_30D_TASK') => {
    const hrmApp = allApps.find((a) => a.code.toUpperCase() === 'HRM');
    const hrmTable = allTables.find(
      (t) => t.id === 'tbl_hrm_nhanvien' || (hrmApp && t.appId === hrmApp.id && t.tableName.includes('nhanvien')),
    ) || allTables.find((t) => hrmApp && t.appId === hrmApp.id);

    if (hrmApp) setSourceAppId(hrmApp.id);
    if (hrmTable) setSourceTableId(hrmTable.id);

    if (preset === 'HRM_CREATE_USER') {
      setName('HRM: Thêm Nhân viên → Tự tạo User');
      setDescription('Tự động tạo tài khoản hệ thống, gắn mã NV và vai trò STAFF khi thêm nhân sự mới.');
      setTriggerEvent('RECORD_CREATED');
      setConditionField('');
      setConditionOperator('EQUALS');
      setConditionValue('');
      setActionType('CREATE_USER');
      setCuUsername('{{EMAIL_PREFIX(email)}}');
      setCuFullName('{{ho_ten}}');
      setCuEmail('{{email}}');
      setCuEmployeeCode('{{ma_nv}}');
      setCuDepartment('{{phong_ban}}');
      setCuRoleCode('STAFF');
      setCuPassword('123456');
    } else if (preset === 'HRM_LOCK_USER') {
      setName('HRM: Nhân viên nghỉ việc → Tự khóa User');
      setDescription('Tự động vô hiệu hóa tài khoản hệ thống khi trạng thái nhân sự chuyển sang NGHI_VIEC.');
      setTriggerEvent('RECORD_UPDATED');
      setConditionField('trang_thai');
      setConditionOperator('EQUALS');
      setConditionValue('NGHI_VIEC');
      setActionType('UPDATE_USER_STATUS');
      setUsMatchBy('employeeCode');
      setUsMatchValue('{{ma_nv}}');
      setUsIsActive('false');
    } else if (preset === 'SCHEDULED_30D_TASK') {
      setName('Lịch định kỳ: Trước 30 ngày đến hạn → Giao việc nhắc xử lý');
      setDescription('Quét hàng ngày các bản ghi có cột ngày sắp đến hạn trong 30 ngày để tự tạo công việc nhắc.');
      setTriggerEvent('SCHEDULED_DATE');
      setConditionField('han_xu_ly');
      setConditionOperator('DAYS_BEFORE');
      setConditionValue('30');
      setActionType('CREATE_TASK');
      setCtTitle('Sắp đến hạn: {{record_key}}');
      setCtDescription('Kiểm tra và xử lý gia hạn / hoàn tất hồ sơ {{record_key}} trước thời hạn.');
      setCtAssigneeUsername('admin');
      setCtPriority('HIGH');
      setCtDueDate('{{DATE_ADD(3)}}');
    } else {
      setName('HRM: Nhân viên mới → Giao việc Onboarding');
      setDescription('Tự động tạo đầu việc Onboarding bên App Giao việc & Tiến độ với hạn +3 ngày.');
      setTriggerEvent('RECORD_CREATED');
      setConditionField('');
      setConditionOperator('EQUALS');
      setConditionValue('');
      setActionType('CREATE_TASK');
      setCtTitle('Onboarding nhân sự mới: {{ho_ten}} ({{ma_nv}})');
      setCtDescription('Chuẩn bị tài khoản, thiết bị và phổ biến nội quy cho {{ho_ten}} - {{phong_ban}}.');
      setCtAssigneeUsername('admin');
      setCtPriority('HIGH');
      setCtDueDate('{{DATE_ADD(3)}}');
    }
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMsg('');
    const needsTgt = actionType === 'CREATE_RECORD' || actionType === 'UPDATE_RECORD';
    const needsMap = needsTgt || actionType === 'UPDATE_SELF';
    const tgtTable = allTables.find((t) => t.id === targetTableId);
    const srcTable = allTables.find((t) => t.id === sourceTableId);
    if (!name.trim() || !sourceTableId || (needsTgt && !tgtTable)) {
      setErrorMsg('Vui lòng nhập tên quy tắc, bảng nguồn và bảng đích (nếu có).');
      return;
    }

    let parsedMap: Record<string, string> = {};
    if (needsMap) {
      try {
        parsedMap = JSON.parse(mappingsJson);
      } catch {
        setErrorMsg('JSON ánh xạ dữ liệu chưa hợp lệ. Hãy kiểm tra dấu ngoặc và dấu phẩy.');
        return;
      }
    } else if (actionType === 'CREATE_USER') {
      parsedMap = {
        username: cuUsername.trim(),
        fullName: cuFullName.trim(),
        email: cuEmail.trim(),
        employeeCode: cuEmployeeCode.trim(),
        department: cuDepartment.trim(),
        roleCode: cuRoleCode.trim() || 'STAFF',
        password: cuPassword || '123456',
      };
    } else if (actionType === 'UPDATE_USER_STATUS') {
      parsedMap = {
        matchBy: usMatchBy,
        matchValue: usMatchValue.trim(),
        isActive: usIsActive,
      };
    } else if (actionType === 'CREATE_TASK') {
      parsedMap = {
        title: ctTitle.trim(),
        description: ctDescription.trim(),
        assigneeUsername: ctAssigneeUsername.trim() || 'admin',
        priority: ctPriority || 'MEDIUM',
        dueDate: ctDueDate.trim(),
      };
    }

    setSaving(true);
    try {
      const res = await fetch('/api/automations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          id: editing?.id,
          name,
          description: description || null,
          sourceAppId: sourceAppId || srcTable?.appId || '',
          sourceTableId,
          triggerEvent,
          conditionField: conditionField || null,
          conditionOperator,
          conditionValue: conditionValue || null,
          targetAppId: needsTgt ? (tgtTable?.appId || null) : null,
          targetModuleId: needsTgt ? (tgtTable?.moduleId || null) : null,
          targetTableId: needsTgt ? (tgtTable?.id || null) : null,
          actionType,
          matchSourceField: actionType === 'UPDATE_RECORD' ? matchSourceField : null,
          matchTargetField: actionType === 'UPDATE_RECORD' ? matchTargetField : null,
          notifyRecipient: actionType === 'SEND_NOTIFICATION' ? notifyRecipient : null,
          notifyTemplate: actionType === 'SEND_NOTIFICATION' ? notifyTemplate : null,
          webhookUrl: actionType === 'CALL_WEBHOOK' ? webhookUrl : null,
          webhookSecret: actionType === 'CALL_WEBHOOK' ? webhookSecret : null,
          fieldMappings: parsedMap,
          isActive: editing ? editing.isActive : true,
        }),
      }).then((r) => r.json());
      if (res.success) {
        onSaved();
      } else {
        setErrorMsg(res.message || 'Không thể lưu quy tắc tự động hóa.');
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 760, maxHeight: '90vh', overflowY: 'auto' }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
          <h3 style={{ fontSize: 17, fontWeight: 700 }}>
            {editing ? `Sửa Quy tắc: ${editing.name}` : 'Tạo Quy tắc Tự động hóa Liên App & Hệ thống'}
          </h3>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        {!editing && (
          <div style={{ marginBottom: 14, padding: '10px 12px', borderRadius: 10, backgroundColor: '#f8fafc', border: '1px solid var(--border-subtle)' }}>
            <div style={{ fontSize: 11.5, fontWeight: 700, color: 'var(--text-secondary)', marginBottom: 6 }}>
              Mẫu cấu hình nhanh 1-click (HRM → Hệ thống):
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
              <button type="button" className="btn btn-outline btn-xs" onClick={() => applyPreset('HRM_CREATE_USER')}>
                + Nhân sự mới → Tạo User
              </button>
              <button type="button" className="btn btn-outline btn-xs" onClick={() => applyPreset('HRM_LOCK_USER')}>
                + Nghỉ việc → Khóa User
              </button>
              <button type="button" className="btn btn-outline btn-xs" onClick={() => applyPreset('HRM_ONBOARD_TASK')}>
                + Nhân sự mới → Giao việc Onboarding
              </button>
              <button type="button" className="btn btn-outline btn-xs" onClick={() => applyPreset('SCHEDULED_30D_TASK')}>
                + Trước hạn 30 ngày → Giao việc nhắc
              </button>
            </div>
          </div>
        )}

        <form onSubmit={submit}>
          <div style={{ display: 'grid', gridTemplateColumns: '1.3fr 1fr', gap: 10 }}>
            <div className="form-group">
              <label className="form-label">Tên quy tắc *</label>
              <input className="form-input" value={name} onChange={(e) => setName(e.target.value)} placeholder="VD: HRM: Thêm nhân sự mới → Tạo tài khoản User" required />
            </div>
            <div className="form-group">
              <label className="form-label">Mô tả ngắn</label>
              <input className="form-input" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Tùy chọn" />
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 10 }}>
            <div className="form-group">
              <label className="form-label">Khi sự kiện ở App</label>
              <select className="form-select" value={sourceAppId} onChange={(e) => { setSourceAppId(e.target.value); setSourceTableId(''); }}>
                <option value="">-- Chọn App --</option>
                {allApps.map((a) => <option key={a.id} value={a.id}>{a.code} — {a.name}</option>)}
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Bảng nguồn *</label>
              <select className="form-select" value={sourceTableId} onChange={(e) => setSourceTableId(e.target.value)} required>
                <option value="">-- Chọn Bảng --</option>
                {srcTables.map((t) => <option key={t.id} value={t.id}>{t.displayName}</option>)}
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Thời điểm kích hoạt</label>
              <select className="form-select" value={triggerEvent} onChange={(e) => setTriggerEvent(e.target.value)}>
                <option value="RECORD_CREATED">Khi Tạo mới (Created)</option>
                <option value="RECORD_UPDATED">Khi Cập nhật (Updated)</option>
                <option value="ANY_SAVE">Mỗi khi Lưu (Any Save)</option>
                <option value="RECORD_APPROVED">Khi Phê duyệt xong (Workflow Approved)</option>
                <option value="RECORD_REJECTED">Khi Bị từ chối (Workflow Rejected)</option>
                <option value="SCHEDULED_DATE">Theo lịch định kỳ / Ngày đến hạn (Scheduled)</option>
              </select>
            </div>
          </div>

          {sourceColumns.length > 0 && (
            <div style={{ marginBottom: 12, padding: '8px 10px', borderRadius: 8, backgroundColor: '#f1f5f9', fontSize: 11.5 }}>
              <span style={{ fontWeight: 600, color: 'var(--text-secondary)', marginRight: 6 }}>
                Cột khả dụng ở bảng nguồn (dùng dạng {'{{mã_cột}}'}):
              </span>
              <span style={{ display: 'inline-flex', flexWrap: 'wrap', gap: 4 }}>
                {sourceColumns.map((col) => (
                  <code
                    key={col.id}
                    title={`${col.columnName} (${col.dataType})`}
                    style={{
                      padding: '1px 6px',
                      borderRadius: 4,
                      backgroundColor: '#fff',
                      border: '1px solid var(--border-subtle)',
                      color: '#0f172a',
                      fontSize: 11,
                    }}
                  >
                    {`{{${col.columnCode}}}`}
                  </code>
                ))}
              </span>
            </div>
          )}

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 0.8fr 1fr', gap: 10 }}>
            <div className="form-group">
              <label className="form-label">Điều kiện trường (tùy chọn)</label>
              <input className="form-input" value={conditionField} onChange={(e) => setConditionField(e.target.value)} placeholder="VD: trang_thai" />
            </div>
            <div className="form-group">
              <label className="form-label">Toán tử</label>
              <select className="form-select" value={conditionOperator} onChange={(e) => setConditionOperator(e.target.value)}>
                <option value="EQUALS">Bằng (=)</option>
                <option value="NOT_EQUALS">Khác (≠)</option>
                <option value="CONTAINS">Chứa</option>
                <option value="NOT_EMPTY">Không trống</option>
                <option value="GREATER_THAN">Lớn hơn (&gt;)</option>
                <option value="LESS_THAN">Nhỏ hơn (&lt;)</option>
                <option value="DAYS_BEFORE">Trước hạn ≤ N ngày (DATE)</option>
                <option value="DAYS_AFTER">Quá hạn ≤ N ngày (DATE)</option>
                <option value="EQUALS_TODAY">Đúng ngày hôm nay (DATE)</option>
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Giá trị so sánh</label>
              <input className="form-input" value={conditionValue} onChange={(e) => setConditionValue(e.target.value)} placeholder="VD: NGHI_VIEC" />
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: (actionType === 'CREATE_RECORD' || actionType === 'UPDATE_RECORD') ? '1fr 1fr 1fr' : '1fr', gap: 10 }}>
            <div className="form-group">
              <label className="form-label">Hành động thực thi *</label>
              <select className="form-select" value={actionType} onChange={(e) => setActionType(e.target.value)}>
                <option value="CREATE_USER">Tự động tạo Tài khoản User (sys_users)</option>
                <option value="UPDATE_USER_STATUS">Tự động Khóa / Mở khóa User</option>
                <option value="CREATE_TASK">Tự động giao Công việc (App Giao việc)</option>
                <option value="CREATE_RECORD">Tạo mới bản ghi ở Bảng đích</option>
                <option value="UPDATE_RECORD">Cập nhật bản ghi Bảng đích</option>
                <option value="UPDATE_SELF">Tự cập nhật chính bản ghi nguồn</option>
                <option value="SEND_NOTIFICATION">Gửi Thông báo nội bộ</option>
                <option value="CALL_WEBHOOK">Gọi Webhook ra ngoài</option>
              </select>
            </div>
            {(actionType === 'CREATE_RECORD' || actionType === 'UPDATE_RECORD') && (
              <>
                <div className="form-group">
                  <label className="form-label">App đích</label>
                  <select className="form-select" value={targetAppId} onChange={(e) => { setTargetAppId(e.target.value); setTargetTableId(''); }}>
                    <option value="">-- Chọn App đích --</option>
                    {allApps.map((a) => <option key={a.id} value={a.id}>{a.code}</option>)}
                  </select>
                </div>
                <div className="form-group">
                  <label className="form-label">Bảng đích *</label>
                  <select className="form-select" value={targetTableId} onChange={(e) => setTargetTableId(e.target.value)}>
                    <option value="">-- Chọn Bảng đích --</option>
                    {tgtTables.map((t) => <option key={t.id} value={t.id}>{t.displayName}</option>)}
                  </select>
                </div>
              </>
            )}
          </div>

          {actionType === 'CREATE_USER' && (
            <div style={{ padding: 12, borderRadius: 10, border: '1px solid #bfdbfe', backgroundColor: '#eff6ff', marginBottom: 14 }}>
              <div style={{ fontSize: 12.5, fontWeight: 700, color: '#1e40af', marginBottom: 8 }}>
                Cấu hình Tài khoản User được tạo tự động (nếu Username/Mã NV/Email đã tồn tại sẽ tự liên kết, không tạo trùng)
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 10 }}>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Username đăng nhập</label>
                  <input className="form-input" value={cuUsername} onChange={(e) => setCuUsername(e.target.value)} placeholder="{{EMAIL_PREFIX(email)}} hoặc {{SLUG(ho_ten)}}" />
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Họ và tên</label>
                  <input className="form-input" value={cuFullName} onChange={(e) => setCuFullName(e.target.value)} placeholder="{{ho_ten}}" />
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Email công ty</label>
                  <input className="form-input" value={cuEmail} onChange={(e) => setCuEmail(e.target.value)} placeholder="{{email}}" />
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Mã nhân viên liên kết</label>
                  <input className="form-input" value={cuEmployeeCode} onChange={(e) => setCuEmployeeCode(e.target.value)} placeholder="{{ma_nv}}" />
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Phòng ban</label>
                  <input className="form-input" value={cuDepartment} onChange={(e) => setCuDepartment(e.target.value)} placeholder="{{phong_ban}}" />
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Vai trò mặc định</label>
                  <select className="form-select" value={cuRoleCode} onChange={(e) => setCuRoleCode(e.target.value)}>
                    {rolesList.length > 0
                      ? rolesList.map((r) => <option key={r.id} value={r.code}>{r.code} — {r.name}</option>)
                      : (
                        <>
                          <option value="STAFF">STAFF — Nhân viên</option>
                          <option value="HR">HR — Nhân sự</option>
                          <option value="MANAGER">MANAGER — Quản lý</option>
                        </>
                      )}
                  </select>
                </div>
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 10, marginTop: 10, alignItems: 'center' }}>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Mật khẩu khởi tạo</label>
                  <input className="form-input" value={cuPassword} onChange={(e) => setCuPassword(e.target.value)} placeholder="123456" />
                </div>
                <div style={{ fontSize: 11.5, color: '#1e3a8a', paddingTop: 16 }}>
                  Hàm hỗ trợ: <code>{'{{EMAIL_PREFIX(email)}}'}</code> (lấy phần trước @), <code>{'{{SLUG(ho_ten)}}'}</code> (viết thường không dấu).
                </div>
              </div>
            </div>
          )}

          {actionType === 'UPDATE_USER_STATUS' && (
            <div style={{ padding: 12, borderRadius: 10, border: '1px solid #fed7aa', backgroundColor: '#fff7ed', marginBottom: 14 }}>
              <div style={{ fontSize: 12.5, fontWeight: 700, color: '#9a3412', marginBottom: 8 }}>
                Cấu hình Khóa / Mở khóa Tài khoản User
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 10 }}>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Tìm User theo</label>
                  <select className="form-select" value={usMatchBy} onChange={(e) => setUsMatchBy(e.target.value)}>
                    <option value="employeeCode">Mã nhân viên (employeeCode)</option>
                    <option value="email">Email</option>
                    <option value="username">Username</option>
                  </select>
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Giá trị đối chiếu</label>
                  <input className="form-input" value={usMatchValue} onChange={(e) => setUsMatchValue(e.target.value)} placeholder="{{ma_nv}} hoặc {{email}}" />
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Trạng thái mới</label>
                  <select className="form-select" value={usIsActive} onChange={(e) => setUsIsActive(e.target.value)}>
                    <option value="false">Khóa tài khoản (Inactive)</option>
                    <option value="true">Mở khóa tài khoản (Active)</option>
                  </select>
                </div>
              </div>
            </div>
          )}

          {actionType === 'CREATE_TASK' && (
            <div style={{ padding: 12, borderRadius: 10, border: '1px solid #bbf7d0', backgroundColor: '#f0fdf4', marginBottom: 14 }}>
              <div style={{ fontSize: 12.5, fontWeight: 700, color: '#166534', marginBottom: 8 }}>
                Cấu hình Đầu việc tự động trong App Giao việc &amp; Tiến độ
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: 10 }}>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Tiêu đề công việc *</label>
                  <input className="form-input" value={ctTitle} onChange={(e) => setCtTitle(e.target.value)} placeholder="VD: Onboarding nhân sự {{ho_ten}}" />
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Người thực hiện (username)</label>
                  <input className="form-input" value={ctAssigneeUsername} onChange={(e) => setCtAssigneeUsername(e.target.value)} placeholder="admin hoặc {{EMAIL_PREFIX(email)}}" />
                </div>
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 1fr', gap: 10, marginTop: 10 }}>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Mô tả chi tiết</label>
                  <input className="form-input" value={ctDescription} onChange={(e) => setCtDescription(e.target.value)} placeholder="Nội dung công việc..." />
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Độ ưu tiên</label>
                  <select className="form-select" value={ctPriority} onChange={(e) => setCtPriority(e.target.value)}>
                    <option value="LOW">Thấp (LOW)</option>
                    <option value="MEDIUM">Trung bình (MEDIUM)</option>
                    <option value="HIGH">Cao (HIGH)</option>
                    <option value="URGENT">Khẩn cấp (URGENT)</option>
                  </select>
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Hạn hoàn thành</label>
                  <input className="form-input" value={ctDueDate} onChange={(e) => setCtDueDate(e.target.value)} placeholder="{{DATE_ADD(3)}} hoặc {{TODAY}}" />
                </div>
              </div>
            </div>
          )}

          {actionType === 'SEND_NOTIFICATION' && (
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 10 }}>
              <div className="form-group">
                <label className="form-label">Người nhận (*, username, {'{{created_by}}'})</label>
                <input className="form-input" value={notifyRecipient} onChange={(e) => setNotifyRecipient(e.target.value)} placeholder="* hoặc {{created_by}}" />
              </div>
              <div className="form-group">
                <label className="form-label">Nội dung thông báo</label>
                <input className="form-input" value={notifyTemplate} onChange={(e) => setNotifyTemplate(e.target.value)} placeholder="Bản ghi {{record_key}} vừa được cập nhật!" />
              </div>
            </div>
          )}

          {actionType === 'CALL_WEBHOOK' && (
            <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: 10 }}>
              <div className="form-group">
                <label className="form-label">Webhook URL *</label>
                <input className="form-input" value={webhookUrl} onChange={(e) => setWebhookUrl(e.target.value)} placeholder="https://hooks.slack.com/services/..." />
              </div>
              <div className="form-group">
                <label className="form-label">HMAC Secret (tùy chọn)</label>
                <input className="form-input" value={webhookSecret} onChange={(e) => setWebhookSecret(e.target.value)} placeholder="secret-key" />
              </div>
            </div>
          )}

          {actionType === 'UPDATE_RECORD' && (
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
              <div className="form-group">
                <label className="form-label">Trường khớp ở bảng nguồn</label>
                <input className="form-input" value={matchSourceField} onChange={(e) => setMatchSourceField(e.target.value)} placeholder="VD: ma_ts" />
              </div>
              <div className="form-group">
                <label className="form-label">Trường khớp ở bảng đích</label>
                <input className="form-input" value={matchTargetField} onChange={(e) => setMatchTargetField(e.target.value)} placeholder="VD: ma_ts" />
              </div>
            </div>
          )}

          {(actionType === 'CREATE_RECORD' || actionType === 'UPDATE_RECORD' || actionType === 'UPDATE_SELF') && (
            <div className="form-group">
              <label className="form-label">Ánh xạ dữ liệu JSON (dùng {'{{field}}'}, {'{{TODAY}}'}, {'{{DATE_ADD(3)}}'}, {'{{AUTO_KEY}}'})</label>
              <textarea className="form-textarea" rows={4} style={{ fontFamily: 'var(--font-mono)', fontSize: 12 }} value={mappingsJson} onChange={(e) => setMappingsJson(e.target.value)} />
            </div>
          )}

          {errorMsg && (
            <div style={{ marginBottom: 10, padding: '8px 12px', borderRadius: 8, backgroundColor: '#fef2f2', border: '1px solid #fecaca', color: '#b91c1c', fontSize: 12.5 }}>
              {errorMsg}
            </div>
          )}

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            <button type="button" className="btn btn-outline" onClick={onClose}>Hủy</button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              <BoltRoundedIcon sx={{ fontSize: 16 }} /> {saving ? 'Đang lưu...' : (editing ? 'Cập nhật Quy tắc' : 'Kích hoạt Quy tắc')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
