'use client';

import React, { useState, useEffect, useMemo } from 'react';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import SecurityRoundedIcon from '@mui/icons-material/SecurityRounded';
import { SysApp, SysOrgUnit, SysRole, SysUser } from '@/lib/types';
import { useEscapeKey } from '@/lib/ui-hooks';
import { flattenOrgUnits } from '@/lib/org-units';

export default function UserFormModal({
  open,
  editing,
  allApps,
  allRoles: propRoles,
  allOrgUnits: propOrgUnits,
  onClose,
  onSaved,
}: {
  open: boolean;
  editing: SysUser | null;
  allApps: SysApp[];
  allRoles?: SysRole[];
  allOrgUnits?: SysOrgUnit[];
  onClose: () => void;
  onSaved: () => void;
}) {
  useEscapeKey(open, onClose);
  if (!open) return null;
  return (
    <UserFormModalBody
      key={editing?.id || '__new__'}
      editing={editing}
      allApps={allApps}
      propRoles={propRoles}
      propOrgUnits={propOrgUnits}
      onClose={onClose}
      onSaved={onSaved}
    />
  );
}

function UserFormModalBody({
  editing,
  allApps,
  propRoles,
  propOrgUnits,
  onClose,
  onSaved,
}: {
  editing: SysUser | null;
  allApps: SysApp[];
  propRoles?: SysRole[];
  propOrgUnits?: SysOrgUnit[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [fetchedRoles, setFetchedRoles] = useState<SysRole[]>([]);
  const [fetchedOrgUnits, setFetchedOrgUnits] = useState<SysOrgUnit[]>([]);
  const allRoles = useMemo(() => (propRoles && propRoles.length > 0 ? propRoles : fetchedRoles), [propRoles, fetchedRoles]);
  const allOrgUnits = useMemo(
    () => (propOrgUnits && propOrgUnits.length > 0 ? propOrgUnits : fetchedOrgUnits),
    [propOrgUnits, fetchedOrgUnits]
  );
  const orderedOrgUnits = useMemo(() => flattenOrgUnits(allOrgUnits), [allOrgUnits]);
  const [username, setUsername] = useState(editing?.username || '');
  const [fullName, setFullName] = useState(editing?.fullName || '');
  const [email, setEmail] = useState(editing?.email || '');
  const [employeeCode, setEmployeeCode] = useState(editing?.employeeCode || '');
  const [department, setDepartment] = useState(editing?.department || 'Toàn công ty');
  const [orgUnitIdOverride, setOrgUnitIdOverride] = useState<string | null>(editing?.orgUnitId ?? null);
  const [roleCodesOverride, setRoleCodesOverride] = useState<string[] | null>(() => {
    if (!editing) return null;
    return editing.roleCodes?.length
      ? editing.roleCodes
      : editing.roleCode
      ? [editing.roleCode]
      : ['STAFF'];
  });
  const [password, setPassword] = useState(editing ? '' : '123456');
  const [isActive, setIsActive] = useState(editing ? editing.isActive !== false : true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (propRoles && propRoles.length > 0) return;
    let cancelled = false;
    fetch('/api/roles', { credentials: 'include' })
      .then((r) => r.json())
      .then((j) => {
        if (!cancelled && j.success && Array.isArray(j.data)) setFetchedRoles(j.data);
      })
      .catch(() => null);
    return () => {
      cancelled = true;
    };
  }, [propRoles]);

  useEffect(() => {
    if (propOrgUnits && propOrgUnits.length > 0) return;
    let cancelled = false;
    fetch('/api/org-units', { credentials: 'include' })
      .then((r) => r.json())
      .then((j) => {
        if (!cancelled && j.success && Array.isArray(j.data)) setFetchedOrgUnits(j.data);
      })
      .catch(() => null);
    return () => {
      cancelled = true;
    };
  }, [propOrgUnits]);

  const defaultRole = allRoles.find((r) => r.code === 'STAFF')?.code || allRoles[0]?.code || 'STAFF';
  const roleCodes = useMemo(() => roleCodesOverride ?? [defaultRole], [roleCodesOverride, defaultRole]);
  const setRoleCodes = (updater: React.SetStateAction<string[]>) => {
    setRoleCodesOverride((prev) => {
      const cur = prev ?? [defaultRole];
      return typeof updater === 'function' ? updater(cur) : updater;
    });
  };
  const matchedOrgUnitId = useMemo(() => {
    if (!editing || editing.orgUnitId || allOrgUnits.length === 0) return '';
    return allOrgUnits.find((u) => u.name.toLowerCase() === (editing.department || '').trim().toLowerCase())?.id || '';
  }, [editing, allOrgUnits]);
  const orgUnitId = orgUnitIdOverride ?? matchedOrgUnitId;
  const setOrgUnitId = (nextId: string) => setOrgUnitIdOverride(nextId);

  const effectivePreview = useMemo(() => {
    const selected = allRoles.filter((r) =>
      roleCodes.some((rc) => rc.toUpperCase() === r.code.toUpperCase())
    );
    const isAdmin = selected.some((r) => r.code.toUpperCase() === 'ADMIN');
    const allAppsAccess = isAdmin || selected.some((r) => r.allAppsAccess);
    const sysPerms = Array.from(new Set(selected.flatMap((r) => r.systemPermissions || [])));

    const appMap: Record<string, Set<string>> = {};
    for (const r of selected) {
      if (r.allAppsAccess) {
        for (const app of allApps) {
          if (!appMap[app.code]) appMap[app.code] = new Set();
          for (const p of r.defaultAppPermissions || []) appMap[app.code].add(p);
        }
      }
      for (const ap of r.appPermissions || []) {
        if (!ap.permissions?.length) continue;
        if (!appMap[ap.appCode]) appMap[ap.appCode] = new Set();
        for (const p of ap.permissions) appMap[ap.appCode].add(p);
      }
    }

    return {
      isAdmin,
      allAppsAccess,
      sysPerms,
      appEntries: Object.entries(appMap).map(([code, set]) => ({
        appCode: code,
        permissions: Array.from(set),
      })),
    };
  }, [allRoles, roleCodes, allApps]);

  const toggleRole = (code: string) => {
    setRoleCodes((prev) => {
      if (prev.includes(code)) {
        const next = prev.filter((c) => c !== code);
        return next.length > 0 ? next : prev; // Keep at least 1 role
      }
      return [...prev, code];
    });
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!username.trim() || !fullName.trim()) return;
    setSaving(true);
    setError('');
    try {
      const res = await fetch('/api/auth/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          id: editing?.id,
          username: username.trim(),
          fullName: fullName.trim(),
          email: email.trim() || null,
          employeeCode: employeeCode.trim().toUpperCase() || null,
          department: department.trim() || 'Toàn công ty',
          orgUnitId: orgUnitId || null,
          roleCode: roleCodes[0] || 'STAFF',
          roleCodes,
          password: password || null,
          isActive,
        }),
      }).then((r) => r.json());
      if (res.success) {
        onSaved();
      } else {
        setError(res.message || 'Không thể lưu tài khoản.');
      }
    } catch {
      setError('Lỗi kết nối máy chủ.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 680, maxHeight: '92vh', overflowY: 'auto' }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
          <div>
            <h3 style={{ fontSize: 17, fontWeight: 700, margin: 0 }}>
              {editing ? `Sửa tài khoản toàn hệ thống: ${editing.username}` : 'Thêm Nhân sự / Tài khoản Toàn hệ thống'}
            </h3>
            <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
              User dùng chung cho toàn bộ hệ thống; quyền truy cập từng App được kế thừa từ Vai trò (Role).
            </div>
          </div>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        {error && (
          <div style={{ padding: '8px 12px', borderRadius: 8, backgroundColor: '#fce8e6', color: '#c5221f', fontSize: 12.5, fontWeight: 600, marginBottom: 12 }}>
            {error}
          </div>
        )}

        <form onSubmit={submit}>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Tên đăng nhập *</label>
              <input
                className="form-input"
                disabled={Boolean(editing)}
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                required
              />
            </div>
            <div className="form-group">
              <label className="form-label">Họ và tên *</label>
              <input className="form-input" value={fullName} onChange={(e) => setFullName(e.target.value)} required />
            </div>
            <div className="form-group">
              <label className="form-label">Đơn vị / Phòng ban (Cơ cấu Tổ chức)</label>
              <select
                className="form-select"
                value={orgUnitId}
                onChange={(e) => {
                  const nextId = e.target.value;
                  setOrgUnitId(nextId);
                  const found = allOrgUnits.find((u) => u.id === nextId);
                  if (found) setDepartment(found.name);
                }}
              >
                <option value="">-- Nhập thủ công / Toàn công ty --</option>
                {orderedOrgUnits.map(({ unit, depth }) => (
                  <option key={unit.id} value={unit.id}>
                    {`${'— '.repeat(depth)}${unit.name} (${unit.code})`}
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Tên Phòng ban hiển thị</label>
              <input
                className="form-input"
                value={department}
                onChange={(e) => setDepartment(e.target.value)}
                placeholder="VD: Phòng Kế toán"
              />
            </div>
            <div className="form-group">
              <label className="form-label">Email</label>
              <input type="email" className="form-input" value={email} onChange={(e) => setEmail(e.target.value)} />
            </div>
            <div className="form-group">
              <label className="form-label">Mã Nhân viên liên kết (VD: NV003)</label>
              <input
                className="form-input"
                value={employeeCode}
                onChange={(e) => setEmployeeCode(e.target.value.toUpperCase())}
                placeholder="Dùng để tự động lọc dữ liệu cá nhân đa App"
              />
            </div>
            <div className="form-group">
              <label className="form-label">
                {editing ? 'Mật khẩu mới (để trống nếu giữ nguyên)' : 'Mật khẩu khởi tạo'}
              </label>
              <input type="password" autoComplete="new-password" className="form-input" value={password} onChange={(e) => setPassword(e.target.value)} />
            </div>
            <div className="form-group" style={{ gridColumn: '1 / -1', display: 'flex', alignItems: 'center' }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, cursor: 'pointer' }}>
                <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
                Tài khoản đang hoạt động (Active)
              </label>
            </div>
          </div>

          <div className="form-group">
            <label className="form-label">Gán Vai trò Hệ thống (có thể chọn nhiều Role / Custom Roles)</label>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(210px, 1fr))', gap: 8, padding: 10, background: 'var(--bg-secondary)', borderRadius: 10, border: '1px solid var(--border-light)' }}>
              {allRoles.map((role) => {
                const checked = roleCodes.some((c) => c.toUpperCase() === role.code.toUpperCase());
                return (
                  <label
                    key={role.id}
                    style={{
                      display: 'flex',
                      alignItems: 'flex-start',
                      gap: 8,
                      padding: '8px 10px',
                      borderRadius: 8,
                      backgroundColor: checked ? 'var(--bg-surface)' : 'transparent',
                      border: `1.5px solid ${checked ? role.color || '#1a73e8' : 'var(--border-light)'}`,
                      cursor: 'pointer',
                    }}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggleRole(role.code)}
                      style={{ marginTop: 2 }}
                    />
                    <div style={{ minWidth: 0 }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                        <span
                          className="badge"
                          style={{ backgroundColor: `${role.color}1a`, color: role.color, fontWeight: 700, fontSize: 11 }}
                        >
                          {role.code}
                        </span>
                        {!role.isActive && <span className="badge badge-danger">Tạm khóa</span>}
                      </div>
                      <div style={{ fontSize: 12, fontWeight: 600, marginTop: 2 }}>{role.name}</div>
                      <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
                        {role.allAppsAccess
                          ? 'Tất cả Apps'
                          : `${role.appPermissions?.length || 0} Apps được cấp`}
                      </div>
                    </div>
                  </label>
                );
              })}
            </div>
          </div>

          <div style={{ padding: 12, borderRadius: 10, backgroundColor: '#f0f6ff', border: '1px solid #d2e3fc', marginBottom: 14 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5, fontWeight: 700, color: '#174ea6', marginBottom: 6 }}>
              <SecurityRoundedIcon sx={{ fontSize: 16 }} />
              <span>Quyền kế thừa tự động từ Vai trò đã chọn</span>
            </div>
            {effectivePreview.isAdmin ? (
              <div style={{ fontSize: 12, color: '#174ea6' }}>
                Tài khoản có vai trò <strong>ADMIN</strong> — toàn quyền quản trị hệ thống và tất cả Ứng dụng.
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 12 }}>
                <div>
                  <strong>Quyền hệ thống: </strong>
                  {effectivePreview.sysPerms.length > 0 ? (
                    effectivePreview.sysPerms.map((p) => (
                      <code key={p} style={{ marginRight: 6, background: 'var(--bg-surface)', padding: '1px 5px', borderRadius: 4 }}>
                        {p}
                      </code>
                    ))
                  ) : (
                    <span style={{ color: 'var(--text-muted)' }}>Không có (chỉ dùng Apps)</span>
                  )}
                </div>
                <div>
                  <strong>Quyền theo từng App: </strong>
                  {effectivePreview.appEntries.length > 0 ? (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 4, marginTop: 4 }}>
                      {effectivePreview.appEntries.map((ae) => (
                        <div key={ae.appCode} style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                          <span className="badge badge-info">{ae.appCode}</span>
                          <span style={{ color: 'var(--text-secondary)', fontSize: 11.5 }}>
                            {ae.permissions.join(', ')}
                          </span>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <span style={{ color: '#c5221f' }}>Chưa được cấp quyền App nào (hãy cấu hình ở tab Vai trò &amp; Phân quyền).</span>
                  )}
                </div>
              </div>
            )}
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            <button type="button" className="btn btn-outline" onClick={onClose}>
              Hủy
            </button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              <SaveRoundedIcon sx={{ fontSize: 16 }} /> {saving ? 'Đang lưu...' : 'Lưu Tài khoản'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
