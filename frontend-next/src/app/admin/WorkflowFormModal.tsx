'use client';

import React, { useState, useEffect } from 'react';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import ArrowUpwardRoundedIcon from '@mui/icons-material/ArrowUpwardRounded';
import ArrowDownwardRoundedIcon from '@mui/icons-material/ArrowDownwardRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded';
import { AppWorkflow, SysApp, SysRole, SysTable, SysUser, WorkflowStep } from '@/lib/types';
import { useEscapeKey } from '@/lib/ui-hooks';

export default function WorkflowFormModal({
  open,
  editing,
  allApps,
  allTables,
  allRoles,
  allUsers,
  onClose,
  onSaved,
}: {
  open: boolean;
  editing: AppWorkflow | null;
  allApps: SysApp[];
  allTables: SysTable[];
  allRoles: SysRole[];
  allUsers: SysUser[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [appId, setAppId] = useState('');
  const [tableId, setTableId] = useState('');
  const [conditionField, setConditionField] = useState('');
  const [conditionOperator, setConditionOperator] = useState('EQUALS');
  const [conditionValue, setConditionValue] = useState('');
  const [steps, setSteps] = useState<WorkflowStep[]>([]);
  const [isActive, setIsActive] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  useEscapeKey(open, onClose);

  useEffect(() => {
    if (!open) return;
    setError('');
    if (editing) {
      setName(editing.name);
      setDescription(editing.description || '');
      setAppId(editing.appId);
      setTableId(editing.tableId);
      setConditionField(editing.conditionField || '');
      setConditionOperator(editing.conditionOperator || 'EQUALS');
      setConditionValue(editing.conditionValue || '');
      setSteps(
        editing.steps?.length
          ? editing.steps.map((s, idx) => ({ ...s, stepOrder: idx + 1 }))
          : [{ stepOrder: 1, name: 'Quản lý rà soát', approverRoles: ['HR_MANAGER'], approverUsers: [], slaHours: 24, requireCommentOnReject: true }]
      );
      setIsActive(editing.isActive !== false);
    } else {
      const defApp = allApps[0]?.id || '';
      const defTbl = allTables.find((t) => t.appId === defApp)?.id || '';
      setName('');
      setDescription('');
      setAppId(defApp);
      setTableId(defTbl);
      setConditionField('');
      setConditionOperator('EQUALS');
      setConditionValue('');
      setSteps([
        { stepOrder: 1, name: 'Bước 1: Trưởng bộ phận xác nhận', approverRoles: ['HR_MANAGER'], approverUsers: [], slaHours: 24, requireCommentOnReject: true },
        { stepOrder: 2, name: 'Bước 2: Ban Quản trị phê duyệt', approverRoles: ['ADMIN'], approverUsers: [], slaHours: 48, requireCommentOnReject: true },
      ]);
      setIsActive(true);
    }
  }, [open, editing, allApps, allTables]);

  if (!open) return null;

  const filteredTables = allTables.filter((t) => !appId || t.appId === appId);

  const addStep = () => {
    setSteps((prev) => [
      ...prev,
      {
        stepOrder: prev.length + 1,
        name: `Bước ${prev.length + 1}: Phê duyệt cấp tiếp theo`,
        approverRoles: ['ADMIN'],
        approverUsers: [],
        slaHours: 24,
        requireCommentOnReject: true,
      },
    ]);
  };

  const updateStep = (idx: number, patch: Partial<WorkflowStep>) => {
    setSteps((prev) => prev.map((s, i) => (i === idx ? { ...s, ...patch } : s)));
  };

  const toggleStepRole = (idx: number, roleCode: string) => {
    const cur = steps[idx]?.approverRoles || [];
    const next = cur.includes(roleCode) ? cur.filter((x) => x !== roleCode) : [...cur, roleCode];
    updateStep(idx, { approverRoles: next });
  };

  const toggleStepUser = (idx: number, username: string) => {
    const cur = steps[idx]?.approverUsers || [];
    const next = cur.includes(username) ? cur.filter((x) => x !== username) : [...cur, username];
    updateStep(idx, { approverUsers: next });
  };

  const moveStep = (idx: number, dir: -1 | 1) => {
    const target = idx + dir;
    if (target < 0 || target >= steps.length) return;
    setSteps((prev) => {
      const copy = [...prev];
      const tmp = copy[idx];
      copy[idx] = copy[target];
      copy[target] = tmp;
      return copy.map((s, i) => ({ ...s, stepOrder: i + 1 }));
    });
  };

  const removeStep = (idx: number) => {
    if (steps.length <= 1) return;
    setSteps((prev) => prev.filter((_, i) => i !== idx).map((s, i) => ({ ...s, stepOrder: i + 1 })));
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim() || !appId || !tableId) {
      setError('Vui lòng nhập Tên quy trình và chọn Ứng dụng + Bảng dữ liệu.');
      return;
    }
    if (steps.some((s) => !s.name.trim() || (s.approverRoles.length === 0 && s.approverUsers.length === 0))) {
      setError('Mỗi bước phải có tên và ít nhất 1 Vai trò hoặc Tài khoản phê duyệt.');
      return;
    }
    setSaving(true);
    setError('');
    try {
      const res = await fetch('/api/workflows', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          id: editing?.id,
          name: name.trim(),
          description: description.trim() || null,
          appId,
          tableId,
          triggerType: 'MANUAL_SUBMIT',
          conditionField: conditionField.trim() || null,
          conditionOperator,
          conditionValue: conditionValue.trim() || null,
          steps: steps.map((s, i) => ({ ...s, stepOrder: i + 1 })),
          isActive,
        }),
      }).then((r) => r.json());
      if (res.success) {
        onSaved();
      } else {
        setError(res.message || 'Không thể lưu quy trình Workflow.');
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 820, maxHeight: '90vh', overflowY: 'auto' }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <div style={{ width: 36, height: 36, borderRadius: 10, backgroundColor: '#e8f0fe', color: '#1a73e8', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <AccountTreeRoundedIcon sx={{ fontSize: 20 }} />
            </div>
            <div>
              <h3 style={{ fontSize: 17, fontWeight: 700 }}>
                {editing ? `Chỉnh sửa Workflow: ${editing.name}` : 'Thiết kế Quy trình Phê duyệt Đa bước'}
              </h3>
              <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                Tự động luân chuyển hồ sơ qua nhiều cấp duyệt và kích hoạt Automation khi hoàn tất
              </div>
            </div>
          </div>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        {/* Visual Live Flow Diagram */}
        <div style={{ padding: '10px 14px', borderRadius: 12, backgroundColor: '#f8fafc', border: '1px solid var(--border-light)', marginBottom: 14, display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <span className="badge" style={{ backgroundColor: '#e8eaed', color: '#3c4043' }}>Nhân sự trình duyệt</span>
          {steps.map((st, idx) => (
            <React.Fragment key={idx}>
              <ArrowForwardRoundedIcon sx={{ fontSize: 16, color: '#94a3b8' }} />
              <div style={{ padding: '4px 10px', borderRadius: 8, backgroundColor: '#ffffff', border: '1px solid #bfdbfe', fontSize: 12 }}>
                <strong style={{ color: '#1a73e8' }}>B{idx + 1}:</strong> {st.name || `Bước ${idx + 1}`}
                <span style={{ color: 'var(--text-muted)', marginLeft: 6, fontSize: 11 }}>
                  ({[...st.approverRoles, ...st.approverUsers].join(', ') || '?'})
                </span>
              </div>
            </React.Fragment>
          ))}
          <ArrowForwardRoundedIcon sx={{ fontSize: 16, color: '#94a3b8' }} />
          <span className="badge badge-success">Hoàn tất &amp; Automation</span>
        </div>

        {error && (
          <div style={{ padding: '8px 12px', borderRadius: 8, backgroundColor: '#fce8e6', color: '#c5221f', fontSize: 12.5, fontWeight: 600, marginBottom: 12 }}>
            {error}
          </div>
        )}

        <form onSubmit={submit}>
          <div style={{ display: 'grid', gridTemplateColumns: '1.5fr 1fr 1fr', gap: 10 }}>
            <div className="form-group">
              <label className="form-label">Tên Quy trình *</label>
              <input className="form-input" value={name} onChange={(e) => setName(e.target.value)} placeholder="VD: Quy trình Duyệt Đề xuất Mua sắm" required />
            </div>
            <div className="form-group">
              <label className="form-label">Ứng dụng áp dụng *</label>
              <select
                className="form-select"
                value={appId}
                onChange={(e) => {
                  setAppId(e.target.value);
                  const first = allTables.find((t) => t.appId === e.target.value);
                  setTableId(first?.id || '');
                }}
              >
                <option value="">-- Chọn App --</option>
                {allApps.map((a) => (
                  <option key={a.id} value={a.id}>{a.name} ({a.code})</option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Bảng dữ liệu *</label>
              <select className="form-select" value={tableId} onChange={(e) => setTableId(e.target.value)}>
                <option value="">-- Chọn Bảng --</option>
                {filteredTables.map((t) => (
                  <option key={t.id} value={t.id}>{t.displayName}</option>
                ))}
              </select>
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1.4fr 1fr 0.8fr 1fr', gap: 10 }}>
            <div className="form-group">
              <label className="form-label">Mô tả</label>
              <input className="form-input" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Giải thích ngắn gọn mục đích quy trình" />
            </div>
            <div className="form-group">
              <label className="form-label">Điều kiện kích hoạt (tùy chọn)</label>
              <input className="form-input" value={conditionField} onChange={(e) => setConditionField(e.target.value)} placeholder="Mã trường (VD: so_tien)" />
            </div>
            <div className="form-group">
              <label className="form-label">Toán tử</label>
              <select className="form-select" value={conditionOperator} onChange={(e) => setConditionOperator(e.target.value)}>
                <option value="EQUALS">Bằng (=)</option>
                <option value="NOT_EQUALS">Khác (≠)</option>
                <option value="GREATER_THAN">Lớn hơn (&gt;)</option>
                <option value="LESS_THAN">Nhỏ hơn (&lt;)</option>
                <option value="CONTAINS">Chứa</option>
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Giá trị so sánh</label>
              <input className="form-input" value={conditionValue} onChange={(e) => setConditionValue(e.target.value)} placeholder="VD: 10000000" />
            </div>
          </div>

          {/* Steps Builder */}
          <div className="form-group">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
              <label className="form-label" style={{ margin: 0 }}>Các bước phê duyệt tuần tự ({steps.length} bước)</label>
              <button type="button" className="btn btn-outline btn-xs" onClick={addStep}>
                <AddRoundedIcon sx={{ fontSize: 15 }} /> Thêm bước duyệt
              </button>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              {steps.map((step, idx) => (
                <div
                  key={idx}
                  style={{
                    padding: 12,
                    borderRadius: 12,
                    border: '1px solid var(--border-subtle)',
                    backgroundColor: '#ffffff',
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: 8 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flex: 1 }}>
                      <span className="badge badge-info">Bước {idx + 1}</span>
                      <input
                        className="form-input"
                        style={{ height: 32, fontSize: 13 }}
                        value={step.name}
                        onChange={(e) => updateStep(idx, { name: e.target.value })}
                        placeholder="Tên bước phê duyệt..."
                        required
                      />
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                      <label style={{ fontSize: 11.5, color: 'var(--text-muted)', whiteSpace: 'nowrap' }}>SLA (giờ):</label>
                      <input
                        type="number"
                        min={1}
                        className="form-input"
                        style={{ width: 68, height: 32, fontSize: 12.5 }}
                        value={step.slaHours}
                        onChange={(e) => updateStep(idx, { slaHours: Math.max(1, Number(e.target.value) || 24) })}
                      />
                      <button type="button" className="btn btn-outline btn-xs" onClick={() => moveStep(idx, -1)} disabled={idx === 0} title="Lên trước">
                        <ArrowUpwardRoundedIcon sx={{ fontSize: 14 }} />
                      </button>
                      <button type="button" className="btn btn-outline btn-xs" onClick={() => moveStep(idx, 1)} disabled={idx === steps.length - 1} title="Xuống sau">
                        <ArrowDownwardRoundedIcon sx={{ fontSize: 14 }} />
                      </button>
                      <button type="button" className="btn btn-outline btn-xs" style={{ color: '#d93025' }} onClick={() => removeStep(idx)} disabled={steps.length <= 1} title="Xóa bước">
                        <DeleteOutlineRoundedIcon sx={{ fontSize: 14 }} />
                      </button>
                    </div>
                  </div>

                  <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                      <span style={{ fontSize: 11.5, fontWeight: 600, color: 'var(--text-muted)' }}>Vai trò được duyệt:</span>
                      {allRoles.map((r) => {
                        const checked = step.approverRoles.includes(r.code);
                        return (
                          <button
                            key={r.id}
                            type="button"
                            onClick={() => toggleStepRole(idx, r.code)}
                            style={{
                              padding: '3px 10px',
                              borderRadius: 999,
                              fontSize: 11.5,
                              fontWeight: 600,
                              cursor: 'pointer',
                              border: `1px solid ${checked ? r.color : 'var(--border-subtle)'}`,
                              backgroundColor: checked ? `${r.color}18` : '#f8f9fa',
                              color: checked ? r.color : 'var(--text-secondary)',
                            }}
                          >
                            {checked ? '✓ ' : ''}{r.name} ({r.code})
                          </button>
                        );
                      })}
                    </div>

                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                      <span style={{ fontSize: 11.5, fontWeight: 600, color: 'var(--text-muted)' }}>Hoặc người duyệt chỉ định:</span>
                      {allUsers.filter((u) => u.isActive).map((u) => {
                        const checked = step.approverUsers.includes(u.username);
                        return (
                          <button
                            key={u.id}
                            type="button"
                            onClick={() => toggleStepUser(idx, u.username)}
                            style={{
                              padding: '2px 8px',
                              borderRadius: 999,
                              fontSize: 11,
                              cursor: 'pointer',
                              border: `1px solid ${checked ? '#1a73e8' : 'var(--border-light)'}`,
                              backgroundColor: checked ? '#e8f0fe' : '#ffffff',
                              color: checked ? '#174ea6' : 'var(--text-muted)',
                            }}
                          >
                            {checked ? '✓ ' : ''}@{u.username}
                          </button>
                        );
                      })}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 14 }}>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
              <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
              Kích hoạt quy trình ngay sau khi lưu
            </label>
            <div style={{ display: 'flex', gap: 8 }}>
              <button type="button" className="btn btn-outline" onClick={onClose}>Hủy</button>
              <button type="submit" className="btn btn-primary" disabled={saving}>
                <SaveRoundedIcon sx={{ fontSize: 16 }} /> {saving ? 'Đang lưu...' : 'Lưu Quy trình'}
              </button>
            </div>
          </div>
        </form>
      </div>
    </div>
  );
}
