'use client';

import React, { useState } from 'react';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import SecurityRoundedIcon from '@mui/icons-material/SecurityRounded';
import { PermissionCatalog, SysApp, SysRole } from '@/lib/types';
import { useEscapeKey } from '@/lib/ui-hooks';

const ROLE_COLORS = ['#1a73e8', '#1e8e3e', '#00897b', '#7b1fa2', '#ea8600', '#d93025', '#5f6368'];

interface RoleFormModalProps {
  open: boolean;
  editing: SysRole | null;
  catalog: PermissionCatalog;
  allApps: SysApp[];
  onClose: () => void;
  onSaved: () => void;
}

export default function RoleFormModal(props: RoleFormModalProps) {
  useEscapeKey(props.open, props.onClose);
  if (!props.open) return null;
  return <RoleFormModalBody key={props.editing?.id || '__new__'} {...props} />;
}

function RoleFormModalBody({
  editing,
  catalog,
  allApps,
  onClose,
  onSaved,
}: RoleFormModalProps) {
  const [code, setCode] = useState(() => editing?.code || '');
  const [name, setName] = useState(() => editing?.name || '');
  const [description, setDescription] = useState(() => editing?.description || '');
  const [color, setColor] = useState(() => editing?.color || '#1a73e8');
  const [isActive, setIsActive] = useState(() => editing ? editing.isActive !== false : true);
  const [allAppsAccess, setAllAppsAccess] = useState(() => Boolean(editing?.allAppsAccess));
  const [defaultAppPermissions, setDefaultAppPermissions] = useState<string[]>(() =>
    editing ? (editing.defaultAppPermissions || []) : ['record.view', 'record.create']
  );
  const [systemPermissions, setSystemPermissions] = useState<string[]>(() =>
    editing ? (editing.systemPermissions || []) : []
  );
  const [appMatrix, setAppMatrix] = useState<Record<string, { permissions: string[]; rowScope: string }>>(() => {
    const map: Record<string, { permissions: string[]; rowScope: string }> = {};
    (editing?.appPermissions || []).forEach((ap) => {
      map[ap.appId] = { permissions: ap.permissions || [], rowScope: ap.rowScope || 'INHERIT' };
    });
    return map;
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const isAdminRole = editing?.code?.toUpperCase() === 'ADMIN';

  const toggleSysPerm = (permCode: string) => {
    setSystemPermissions((prev) =>
      prev.includes(permCode) ? prev.filter((x) => x !== permCode) : [...prev, permCode]
    );
  };

  const toggleDefAppPerm = (permCode: string) => {
    setDefaultAppPermissions((prev) =>
      prev.includes(permCode) ? prev.filter((x) => x !== permCode) : [...prev, permCode]
    );
  };

  const toggleAppPerm = (appId: string, permCode: string) => {
    setAppMatrix((prev) => {
      const cur = prev[appId] || { permissions: [], rowScope: 'INHERIT' };
      const has = cur.permissions.includes(permCode);
      const nextPerms = has ? cur.permissions.filter((x) => x !== permCode) : [...cur.permissions, permCode];
      if (!has && permCode !== 'record.view' && !nextPerms.includes('record.view')) {
        nextPerms.unshift('record.view');
      }
      return { ...prev, [appId]: { ...cur, permissions: nextPerms } };
    });
  };

  const toggleAllPermsForApp = (appId: string, grant: boolean) => {
    setAppMatrix((prev) => ({
      ...prev,
      [appId]: {
        permissions: grant ? catalog.appPermissions.map((p) => p.code) : [],
        rowScope: prev[appId]?.rowScope || 'INHERIT',
      },
    }));
  };

  const setAppRowScope = (appId: string, rowScope: string) => {
    setAppMatrix((prev) => ({
      ...prev,
      [appId]: { permissions: prev[appId]?.permissions || ['record.view'], rowScope },
    }));
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!code.trim() || !name.trim()) return;
    setSaving(true);
    setError('');
    try {
      const appPermissions = Object.entries(appMatrix)
        .filter(([, val]) => val.permissions.length > 0)
        .map(([appId, val]) => ({
          appId,
          permissions: val.permissions,
          rowScope: val.rowScope,
        }));

      const res = await fetch('/api/roles', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          id: editing?.id,
          code: code.trim().toUpperCase().replace(/\s+/g, '_'),
          name: name.trim(),
          description: description.trim() || null,
          color,
          isActive,
          allAppsAccess,
          defaultAppPermissions,
          systemPermissions,
          appPermissions,
        }),
      }).then((r) => r.json());

      if (res.success) {
        onSaved();
      } else {
        setError(res.message || 'Không thể lưu vai trò.');
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
            <div style={{ width: 36, height: 36, borderRadius: 10, backgroundColor: `${color}18`, color, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <SecurityRoundedIcon sx={{ fontSize: 20 }} />
            </div>
            <div>
              <h3 style={{ fontSize: 17, fontWeight: 700 }}>
                {editing ? `Cấu hình Vai trò: ${editing.name}` : 'Tạo Vai trò & Phân quyền Mới'}
              </h3>
              <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                Thiết lập quyền quản trị hệ thống và quyền thao tác trên từng Ứng dụng
              </div>
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
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1.5fr auto', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Mã Vai trò *</label>
              <input
                className="form-input"
                disabled={Boolean(editing)}
                value={code}
                onChange={(e) => setCode(e.target.value.toUpperCase().replace(/[^A-Z0-9_]/g, ''))}
                placeholder="VD: FINANCE_MGR"
                required
              />
            </div>
            <div className="form-group">
              <label className="form-label">Tên Vai trò hiển thị *</label>
              <input
                className="form-input"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="VD: Trưởng phòng Tài chính"
                required
              />
            </div>
            <div className="form-group">
              <label className="form-label">Màu nhận diện</label>
              <div style={{ display: 'flex', gap: 5, alignItems: 'center', height: 38 }}>
                {ROLE_COLORS.map((c) => (
                  <button
                    key={c}
                    type="button"
                    onClick={() => setColor(c)}
                    style={{
                      width: 22,
                      height: 22,
                      borderRadius: '50%',
                      backgroundColor: c,
                      border: color === c ? '2.5px solid #202124' : '2px solid transparent',
                      cursor: 'pointer',
                    }}
                  />
                ))}
              </div>
            </div>
          </div>

          <div className="form-group">
            <label className="form-label">Mô tả phạm vi trách nhiệm</label>
            <input
              className="form-input"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Mô tả ngắn gọn nhiệm vụ của vai trò này..."
            />
          </div>

          {/* System Permissions */}
          <div className="form-group">
            <label className="form-label">Quyền Quản trị Hệ thống</label>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 8, padding: 12, background: 'var(--bg-secondary)', borderRadius: 10, border: '1px solid var(--border-light)' }}>
              {catalog.systemPermissions.map((sp) => {
                const checked = isAdminRole || systemPermissions.includes(sp.code);
                return (
                  <label
                    key={sp.code}
                    style={{
                      display: 'flex',
                      alignItems: 'flex-start',
                      gap: 8,
                      padding: '8px 10px',
                      borderRadius: 8,
                      backgroundColor: checked ? 'var(--bg-surface)' : 'transparent',
                      border: `1px solid ${checked ? 'var(--accent-primary)' : 'var(--border-light)'}`,
                      cursor: isAdminRole ? 'not-allowed' : 'pointer',
                    }}
                  >
                    <input
                      type="checkbox"
                      disabled={isAdminRole}
                      checked={checked}
                      onChange={() => toggleSysPerm(sp.code)}
                      style={{ marginTop: 2 }}
                    />
                    <div>
                      <div style={{ fontSize: 12.5, fontWeight: 600 }}>{sp.name}</div>
                      <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{sp.description}</div>
                    </div>
                  </label>
                );
              })}
            </div>
          </div>

          {/* All Apps vs Granular App Matrix */}
          <div className="form-group">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8, flexWrap: 'wrap', gap: 8 }}>
              <label className="form-label" style={{ margin: 0 }}>Phân quyền theo từng Ứng dụng</label>
              {!isAdminRole && (
                <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12.5, fontWeight: 600, cursor: 'pointer', color: '#1a73e8' }}>
                  <input
                    type="checkbox"
                    checked={allAppsAccess}
                    onChange={(e) => setAllAppsAccess(e.target.checked)}
                  />
                  Cho phép truy cập tất cả Ứng dụng (bao gồm App mới)
                </label>
              )}
            </div>

            {isAdminRole ? (
              <div style={{ padding: 12, borderRadius: 10, backgroundColor: 'var(--danger-bg)', color: 'var(--danger-text)', fontSize: 12.5 }}>
                Vai trò <strong>ADMIN</strong> mặc định có toàn quyền trên mọi Ứng dụng và không thể bị giới hạn.
              </div>
            ) : allAppsAccess ? (
              <div style={{ padding: 12, borderRadius: 10, backgroundColor: 'var(--info-bg)', border: '1px solid var(--info-border)' }}>
                <div style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--info-text)', marginBottom: 8 }}>
                  Chọn bộ quyền mặc định áp dụng cho tất cả các Ứng dụng:
                </div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
                  {catalog.appPermissions.map((ap) => {
                    const checked = defaultAppPermissions.includes(ap.code);
                    return (
                      <label
                        key={ap.code}
                        style={{
                          display: 'inline-flex',
                          alignItems: 'center',
                          gap: 6,
                          padding: '5px 10px',
                          borderRadius: 999,
                          backgroundColor: checked ? 'var(--bg-surface)' : 'var(--bg-secondary)',
                          border: `1px solid ${checked ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
                          fontSize: 12,
                          cursor: 'pointer',
                        }}
                      >
                        <input type="checkbox" checked={checked} onChange={() => toggleDefAppPerm(ap.code)} />
                        {ap.name}
                      </label>
                    );
                  })}
                </div>
              </div>
            ) : (
              <div style={{ border: '1px solid var(--border-subtle)', borderRadius: 10, overflowX: 'auto' }}>
                <table className="data-table" style={{ fontSize: 12 }}>
                  <thead>
                    <tr>
                      <th style={{ minWidth: 165 }}>Ứng dụng</th>
                      {catalog.appPermissions.map((ap) => (
                        <th key={ap.code} style={{ textAlign: 'center', minWidth: 82 }} title={ap.description}>
                          {ap.name}
                        </th>
                      ))}
                      <th style={{ minWidth: 110 }}>Phạm vi dòng</th>
                      <th style={{ textAlign: 'center' }}>Nhanh</th>
                    </tr>
                  </thead>
                  <tbody>
                    {allApps.map((app) => {
                      const entry = appMatrix[app.id] || { permissions: [], rowScope: 'INHERIT' };
                      const hasAll = catalog.appPermissions.every((p) => entry.permissions.includes(p.code));
                      return (
                        <tr key={app.id}>
                          <td>
                            <span className="badge" style={{ backgroundColor: `${app.color}18`, color: app.color, marginRight: 6 }}>
                              {app.code}
                            </span>
                            <strong>{app.name}</strong>
                          </td>
                          {catalog.appPermissions.map((ap) => (
                            <td key={ap.code} style={{ textAlign: 'center' }}>
                              <input
                                type="checkbox"
                                checked={entry.permissions.includes(ap.code)}
                                onChange={() => toggleAppPerm(app.id, ap.code)}
                                style={{ width: 15, height: 15, cursor: 'pointer', accentColor: app.color }}
                              />
                            </td>
                          ))}
                          <td>
                            <select
                              className="form-select"
                              style={{ height: 28, fontSize: 11.5, padding: '2px 6px' }}
                              value={entry.rowScope}
                              onChange={(e) => setAppRowScope(app.id, e.target.value)}
                            >
                              <option value="INHERIT">Theo Bảng</option>
                              <option value="ALL">Xem tất cả</option>
                              <option value="DEPARTMENT">Theo phòng ban</option>
                              <option value="OWNER_ONLY">Chỉ của tôi</option>
                            </select>
                          </td>
                          <td style={{ textAlign: 'center' }}>
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              onClick={() => toggleAllPermsForApp(app.id, !hasAll)}
                            >
                              {hasAll ? 'Bỏ hết' : 'Chọn hết'}
                            </button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 14 }}>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
              <input type="checkbox" disabled={isAdminRole} checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
              Vai trò đang kích hoạt
            </label>
            <div style={{ display: 'flex', gap: 8 }}>
              <button type="button" className="btn btn-outline" onClick={onClose}>
                Hủy
              </button>
              <button type="submit" className="btn btn-primary" disabled={saving}>
                <SaveRoundedIcon sx={{ fontSize: 16 }} /> {saving ? 'Đang lưu...' : 'Lưu Vai trò'}
              </button>
            </div>
          </div>
        </form>
      </div>
    </div>
  );
}
