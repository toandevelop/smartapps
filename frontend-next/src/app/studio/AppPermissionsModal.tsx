'use client';

import React, { useEffect, useState } from 'react';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import ShieldRoundedIcon from '@mui/icons-material/ShieldRounded';
import TableViewRoundedIcon from '@mui/icons-material/TableViewRounded';
import { AppRoleMatrixItem, PermissionCatalog, RoleTablePermissionItem, SysApp } from '@/lib/types';
import { useEscapeKey } from '@/lib/ui-hooks';

export default function AppPermissionsModal({
  open,
  app,
  catalog: propCatalog,
  onClose,
  onSaved,
}: {
  open: boolean;
  app: SysApp | null;
  catalog?: PermissionCatalog;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [subTab, setSubTab] = useState<'app' | 'tables'>('app');
  const [fetchedCatalog, setFetchedCatalog] = useState<PermissionCatalog>({
    systemPermissions: [],
    appPermissions: [],
  });
  const catalog = propCatalog && propCatalog.appPermissions.length > 0 ? propCatalog : fetchedCatalog;

  const [rows, setRows] = useState<AppRoleMatrixItem[]>([]);
  const [tableRows, setTableRows] = useState<RoleTablePermissionItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  useEscapeKey(open && Boolean(app), onClose);

  useEffect(() => {
    if (!open || !app) return;
    setError('');
    setLoading(true);

    const reqs: Promise<any>[] = [
      fetch(`/api/roles/by-app/${encodeURIComponent(app.id)}`, { credentials: 'include' }).then((r) => r.json()),
      fetch(`/api/roles/by-app/${encodeURIComponent(app.id)}/tables`, { credentials: 'include' }).then((r) => r.json()),
    ];
    if (!propCatalog || propCatalog.appPermissions.length === 0) {
      reqs.push(fetch('/api/roles/catalog', { credentials: 'include' }).then((r) => r.json()));
    }

    Promise.all(reqs)
      .then(([appRes, tblRes, catRes]) => {
        if (appRes.success && Array.isArray(appRes.data)) setRows(appRes.data);
        else setError(appRes.message || 'Không tải được ma trận phân quyền App.');

        if (tblRes.success && Array.isArray(tblRes.data)) setTableRows(tblRes.data);
        if (catRes?.success && catRes.data) setFetchedCatalog(catRes.data);
      })
      .catch(() => setError('Lỗi kết nối máy chủ.'))
      .finally(() => setLoading(false));
  }, [open, app, propCatalog]);

  if (!open || !app) return null;

  const updateRow = (roleId: string, updater: (r: AppRoleMatrixItem) => AppRoleMatrixItem) => {
    setRows((prev) => prev.map((r) => (r.roleId === roleId ? updater(r) : r)));
  };

  const updateTableRow = (
    roleId: string,
    tableId: string,
    updater: (r: RoleTablePermissionItem) => RoleTablePermissionItem
  ) => {
    setTableRows((prev) =>
      prev.map((r) =>
        r.roleId === roleId && r.tableId === tableId ? updater({ ...r, hasExplicitRule: true }) : r
      )
    );
  };

  const save = async () => {
    setSaving(true);
    setError('');
    try {
      if (subTab === 'app') {
        const res = await fetch(`/api/roles/by-app/${encodeURIComponent(app.id)}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({
            assignments: rows.map((r) => ({
              roleId: r.roleId,
              hasAccess: r.hasAccess,
              permissions: r.permissions,
              rowScope: r.rowScope,
            })),
          }),
        }).then((r) => r.json());
        if (res.success) {
          onSaved();
        } else {
          setError(res.message || 'Không thể lưu phân quyền App.');
        }
      } else {
        const res = await fetch(`/api/roles/by-app/${encodeURIComponent(app.id)}/tables`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({
            rules: tableRows
              .filter((r) => r.roleCode.toUpperCase() !== 'ADMIN')
              .map((r) => ({
                roleId: r.roleId,
                tableId: r.tableId,
                useAppDefault: !r.hasExplicitRule,
                canView: r.canView,
                canCreate: r.canCreate,
                canEdit: r.canEdit,
                canDelete: r.canDelete,
                canExport: r.canExport,
                canApprove: r.canApprove,
                rowScope: r.rowScope,
              })),
          }),
        }).then((r) => r.json());
        if (res.success) {
          onSaved();
        } else {
          setError(res.message || 'Không thể lưu phân quyền bảng.');
        }
      }
    } catch {
      setError('Lỗi kết nối máy chủ.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-dialog"
        style={{ maxWidth: 1060, maxHeight: '92vh', overflowY: 'auto' }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <div
              style={{
                width: 36,
                height: 36,
                borderRadius: 10,
                backgroundColor: app.bgColor || '#e8f0fe',
                color: app.color || '#1a73e8',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <ShieldRoundedIcon sx={{ fontSize: 20 }} />
            </div>
            <div>
              <h3 style={{ fontSize: 17, fontWeight: 700, margin: 0 }}>
                Phân quyền trong App: {app.name} ({app.code})
              </h3>
              <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                Thiết lập quyền Quản trị App, quyền Sử dụng và phân quyền chi tiết cho từng Bảng dữ liệu.
              </div>
            </div>
          </div>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        <div style={{ display: 'flex', gap: 8, marginBottom: 14 }}>
          <button
            type="button"
            className={`btn btn-sm ${subTab === 'app' ? 'btn-primary' : 'btn-outline'}`}
            onClick={() => setSubTab('app')}
          >
            <ShieldRoundedIcon sx={{ fontSize: 15 }} />
            1. Phân quyền theo Vai trò (App)
          </button>
          <button
            type="button"
            className={`btn btn-sm ${subTab === 'tables' ? 'btn-primary' : 'btn-outline'}`}
            onClick={() => setSubTab('tables')}
          >
            <TableViewRoundedIcon sx={{ fontSize: 15 }} />
            2. Phân quyền chi tiết từng Bảng (Quản trị vs Sử dụng)
          </button>
        </div>

        {error && (
          <div style={{ padding: '8px 12px', borderRadius: 8, backgroundColor: '#fce8e6', color: '#c5221f', fontSize: 12.5, fontWeight: 600, marginBottom: 12 }}>
            {error}
          </div>
        )}

        {loading ? (
          <div style={{ padding: 32, textAlign: 'center', color: 'var(--text-muted)' }}>Đang tải ma trận phân quyền...</div>
        ) : subTab === 'app' ? (
          <div style={{ overflowX: 'auto' }}>
            <table className="data-table">
              <thead>
                <tr>
                  <th>Vai trò (Role)</th>
                  <th style={{ textAlign: 'center' }}>Truy cập App</th>
                  {catalog.appPermissions.map((ap) => (
                    <th key={ap.code} style={{ textAlign: 'center', fontSize: 11.5 }} title={ap.description}>
                      {ap.name}
                    </th>
                  ))}
                  <th>Phạm vi dữ liệu</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => {
                  const locked = r.isSystem || r.allAppsAccess;
                  return (
                    <tr key={r.roleId}>
                      <td>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                          <span
                            className="badge"
                            style={{ backgroundColor: `${r.roleColor}1a`, color: r.roleColor, fontWeight: 700 }}
                          >
                            {r.roleCode}
                          </span>
                        </div>
                        <div style={{ fontSize: 11.5, color: 'var(--text-secondary)', marginTop: 2 }}>{r.roleName}</div>
                        {r.allAppsAccess && (
                          <div style={{ fontSize: 10.5, color: 'var(--text-muted)' }}>Quyền toàn hệ thống</div>
                        )}
                      </td>
                      <td style={{ textAlign: 'center' }}>
                        <input
                          type="checkbox"
                          disabled={locked}
                          checked={r.hasAccess}
                          onChange={(e) =>
                            updateRow(r.roleId, (cur) => ({
                              ...cur,
                              hasAccess: e.target.checked,
                              permissions:
                                e.target.checked && cur.permissions.length === 0 ? ['record.view'] : cur.permissions,
                            }))
                          }
                        />
                      </td>
                      {catalog.appPermissions.map((ap) => {
                        const checked = r.hasAccess && r.permissions.includes(ap.code);
                        return (
                          <td key={ap.code} style={{ textAlign: 'center' }}>
                            <input
                              type="checkbox"
                              disabled={locked || !r.hasAccess}
                              checked={checked}
                              onChange={(e) =>
                                updateRow(r.roleId, (cur) => ({
                                  ...cur,
                                  permissions: e.target.checked
                                    ? Array.from(new Set([...cur.permissions, ap.code]))
                                    : cur.permissions.filter((p) => p !== ap.code),
                                }))
                              }
                            />
                          </td>
                        );
                      })}
                      <td>
                        <select
                          className="form-select"
                          disabled={locked || !r.hasAccess}
                          style={{ height: 30, fontSize: 11.5, padding: '2px 6px', minWidth: 120 }}
                          value={r.rowScope}
                          onChange={(e) =>
                            updateRow(r.roleId, (cur) => ({ ...cur, rowScope: e.target.value }))
                          }
                        >
                          <option value="INHERIT">Theo bảng</option>
                          <option value="ALL">Tất cả (ALL)</option>
                          <option value="DEPARTMENT">Theo phòng ban</option>
                          <option value="OWNER_ONLY">Chỉ của mình</option>
                        </select>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            <div style={{ fontSize: 12.5, color: 'var(--text-secondary)', padding: '8px 12px', backgroundColor: '#f8fafc', borderRadius: 8 }}>
              Ví dụ với <strong>STAFF</strong> ở App HRM: chỉ xem <em>Hồ sơ Nhân viên</em> của mình (tắt Thêm/Sửa/Xóa), cho phép <strong>Thêm</strong> ở <em>Đơn xin Nghỉ phép</em>, và chỉ <strong>Xem</strong> ở <em>Lịch công tác</em> của mình.
            </div>
            <div style={{ overflowX: 'auto' }}>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Vai trò</th>
                    <th>Bảng dữ liệu</th>
                    <th style={{ textAlign: 'center' }}>Theo App</th>
                    <th style={{ textAlign: 'center' }}>Xem</th>
                    <th style={{ textAlign: 'center' }}>Thêm</th>
                    <th style={{ textAlign: 'center' }}>Sửa</th>
                    <th style={{ textAlign: 'center' }}>Xóa</th>
                    <th style={{ textAlign: 'center' }}>Xuất</th>
                    <th style={{ textAlign: 'center' }}>Duyệt</th>
                    <th>Phạm vi dữ liệu</th>
                  </tr>
                </thead>
                <tbody>
                  {tableRows.map((tr) => {
                    const locked = tr.isSystem || tr.roleCode.toUpperCase() === 'ADMIN';
                    return (
                      <tr key={`${tr.roleId}_${tr.tableId}`}>
                        <td>
                          <span
                            className="badge"
                            style={{ backgroundColor: `${tr.roleColor}1a`, color: tr.roleColor, fontWeight: 700 }}
                          >
                            {tr.roleCode}
                          </span>
                        </td>
                        <td style={{ fontWeight: 600 }}>{tr.tableDisplayName}</td>
                        <td style={{ textAlign: 'center' }}>
                          <input
                            type="checkbox"
                            disabled={locked}
                            checked={!tr.hasExplicitRule}
                            title="Bỏ chọn để tùy chỉnh riêng cho bảng này"
                            onChange={(e) =>
                              setTableRows((prev) =>
                                prev.map((x) =>
                                  x.roleId === tr.roleId && x.tableId === tr.tableId
                                    ? { ...x, hasExplicitRule: !e.target.checked }
                                    : x
                                )
                              )
                            }
                          />
                        </td>
                        {(
                          [
                            ['canView', tr.canView],
                            ['canCreate', tr.canCreate],
                            ['canEdit', tr.canEdit],
                            ['canDelete', tr.canDelete],
                            ['canExport', tr.canExport],
                            ['canApprove', tr.canApprove],
                          ] as Array<[keyof RoleTablePermissionItem, boolean]>
                        ).map(([field, val]) => (
                          <td key={field} style={{ textAlign: 'center' }}>
                            <input
                              type="checkbox"
                              disabled={locked}
                              checked={val}
                              onChange={(e) =>
                                updateTableRow(tr.roleId, tr.tableId, (cur) => ({
                                  ...cur,
                                  [field]: e.target.checked,
                                }))
                              }
                            />
                          </td>
                        ))}
                        <td>
                          <select
                            className="form-select"
                            disabled={locked}
                            style={{ height: 30, fontSize: 11.5, padding: '2px 6px', minWidth: 125 }}
                            value={tr.rowScope}
                            onChange={(e) =>
                              updateTableRow(tr.roleId, tr.tableId, (cur) => ({
                                ...cur,
                                rowScope: e.target.value,
                              }))
                            }
                          >
                            <option value="INHERIT">Theo App/Bảng</option>
                            <option value="ALL">Tất cả bản ghi</option>
                            <option value="DEPARTMENT">Theo phòng ban</option>
                            <option value="OWNER_ONLY">Chỉ của mình</option>
                          </select>
                        </td>
                      </tr>
                    );
                  })}
                  {tableRows.length === 0 && (
                    <tr>
                      <td colSpan={10} style={{ textAlign: 'center', color: 'var(--text-muted)', padding: 18 }}>
                        Chưa có vai trò nào được cấp quyền vào App này. Hãy bật ở tab &ldquo;Phân quyền theo Vai trò (App)&rdquo; trước.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 16 }}>
          <button type="button" className="btn btn-outline" onClick={onClose}>
            Đóng
          </button>
          <button type="button" className="btn btn-primary" disabled={saving || loading} onClick={save}>
            <SaveRoundedIcon sx={{ fontSize: 16 }} />{' '}
            {saving ? 'Đang lưu...' : subTab === 'app' ? 'Lưu Phân quyền App' : 'Lưu Phân quyền từng Bảng'}
          </button>
        </div>
      </div>
    </div>
  );
}
