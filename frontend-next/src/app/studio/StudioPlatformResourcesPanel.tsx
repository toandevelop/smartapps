'use client';

import React, { useState, useMemo } from 'react';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import CategoryRoundedIcon from '@mui/icons-material/CategoryRounded';
import ShieldRoundedIcon from '@mui/icons-material/ShieldRounded';
import ManageAccountsRoundedIcon from '@mui/icons-material/ManageAccountsRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import FileDownloadRoundedIcon from '@mui/icons-material/FileDownloadRounded';
import FileUploadRoundedIcon from '@mui/icons-material/FileUploadRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import KeyRoundedIcon from '@mui/icons-material/KeyRounded';
import { clearCategoryCache } from '@/lib/category-service';
import {
  CategoryExportPreviewModal,
  CategoryFormModal,
  CategoryImportPreviewModal,
  CategoryItemFormModal,
} from './CategoryExcelModals';
import {
  AppAuditLog,
  SysApp,
  SysCategory,
  SysCategoryItem,
  SysRole,
  SysUser,
} from '@/lib/types';

export type PlatformSubTab = 'categories' | 'roles' | 'users' | 'audit';

interface StudioPlatformResourcesPanelProps {
  activeSubTab: PlatformSubTab;
  onChangeSubTab: (tab: PlatformSubTab) => void;
  canStudio: boolean;
  canManageRoles: boolean;
  canManageUsers: boolean;
  canViewAudit: boolean;
  apps: SysApp[];
  roles: SysRole[];
  users: SysUser[];
  categories: SysCategory[];
  selectedCat: SysCategory | null;
  catItems: SysCategoryItem[];
  globalAuditLogs: AppAuditLog[];
  auditAppFilter: string;
  currentUserId?: string;
  onSelectCategory: (cat: SysCategory) => void;
  onCreateRole: () => void;
  onEditRole: (role: SysRole) => void;
  onDeleteRole: (role: SysRole) => void;
  onOpenAppPerms: (app: SysApp) => void;
  onCreateUser: () => void;
  onEditUser: (u: SysUser) => void;
  onResetUserPw: (u: SysUser) => void;
  onDeleteUser: (u: SysUser) => void;
  onChangeAuditAppFilter: (appId: string) => void;
  onRefreshCategory?: () => Promise<void> | void;
  toast?: (msg: string, type?: 'success' | 'error') => void;
}

export default function StudioPlatformResourcesPanel({
  activeSubTab,
  onChangeSubTab,
  canStudio,
  canManageRoles,
  canManageUsers,
  canViewAudit,
  apps,
  roles,
  users,
  categories,
  selectedCat,
  catItems,
  globalAuditLogs,
  auditAppFilter,
  currentUserId,
  onSelectCategory,
  onCreateRole,
  onEditRole,
  onDeleteRole,
  onOpenAppPerms,
  onCreateUser,
  onEditUser,
  onResetUserPw,
  onDeleteUser,
  onChangeAuditAppFilter,
  onRefreshCategory,
  toast,
}: StudioPlatformResourcesPanelProps) {
  const [userQuery, setUserQuery] = useState('');
  const [showExportCatModal, setShowExportCatModal] = useState(false);
  const [showImportCatModal, setShowImportCatModal] = useState(false);
  const [showCatModal, setShowCatModal] = useState(false);
  const [editingCategory, setEditingCategory] = useState<SysCategory | null>(null);
  const [showItemModal, setShowItemModal] = useState(false);
  const [editingItem, setEditingItem] = useState<SysCategoryItem | null>(null);
  const [catSearch, setCatSearch] = useState('');
  const [itemSearch, setItemSearch] = useState('');

  const filteredCategories = useMemo(() => {
    const q = catSearch.trim().toLowerCase();
    if (!q) return categories;
    return categories.filter(
      (c) => c.categoryCode.toLowerCase().includes(q) || c.categoryName.toLowerCase().includes(q)
    );
  }, [categories, catSearch]);

  const filteredCatItems = useMemo(() => {
    const q = itemSearch.trim().toLowerCase();
    if (!q) return catItems;
    return catItems.filter(
      (it) => it.itemCode.toLowerCase().includes(q) || it.itemName.toLowerCase().includes(q)
    );
  }, [catItems, itemSearch]);

  const filteredUsers = useMemo(() => {
    const q = userQuery.trim().toLowerCase();
    if (!q) return users;
    return users.filter(
      (u) =>
        u.username.toLowerCase().includes(q) ||
        u.fullName.toLowerCase().includes(q) ||
        (u.email || '').toLowerCase().includes(q) ||
        (u.employeeCode || '').toLowerCase().includes(q) ||
        (u.department || '').toLowerCase().includes(q)
    );
  }, [users, userQuery]);

  return (
    <div className="card" style={{ padding: 20, display: 'flex', flexDirection: 'column', gap: 18 }}>
      {/* Standardized Segmented Bar */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 12 }}>
        <div className="segmented-tabs subtle">
          {canStudio && (
            <button
              type="button"
              className={`segmented-tab ${activeSubTab === 'categories' ? 'active' : ''}`}
              onClick={() => onChangeSubTab('categories')}
            >
              <CategoryRoundedIcon sx={{ fontSize: 16 }} />
              Danh mục Dùng chung ({categories.length})
            </button>
          )}
          {canManageRoles && (
            <button
              type="button"
              className={`segmented-tab ${activeSubTab === 'roles' ? 'active' : ''}`}
              onClick={() => onChangeSubTab('roles')}
            >
              <ShieldRoundedIcon sx={{ fontSize: 16 }} />
              Vai trò &amp; Phân quyền ({roles.length})
            </button>
          )}
          {canManageUsers && (
            <button
              type="button"
              className={`segmented-tab ${activeSubTab === 'users' ? 'active' : ''}`}
              onClick={() => onChangeSubTab('users')}
            >
              <ManageAccountsRoundedIcon sx={{ fontSize: 16 }} />
              Người dùng ({users.length})
            </button>
          )}
          {canViewAudit && (
            <button
              type="button"
              className={`segmented-tab ${activeSubTab === 'audit' ? 'active' : ''}`}
              onClick={() => onChangeSubTab('audit')}
            >
              <HistoryRoundedIcon sx={{ fontSize: 16 }} />
              Nhật ký Hệ thống
            </button>
          )}
        </div>

        {activeSubTab === 'roles' && canManageRoles && (
          <button type="button" className="btn btn-primary btn-sm" onClick={onCreateRole}>
            <AddRoundedIcon sx={{ fontSize: 16 }} />
            Tạo Custom Role Mới
          </button>
        )}
        {activeSubTab === 'users' && canManageUsers && (
          <button type="button" className="btn btn-primary btn-sm" onClick={onCreateUser}>
            <AddRoundedIcon sx={{ fontSize: 16 }} />
            Thêm Tài khoản Mới
          </button>
        )}
      </div>

      {/* 1. Categories */}
      {activeSubTab === 'categories' && (
        <div className="studio-cat-grid">
          {/* Left Column: Category List */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
              <span style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-secondary)' }}>
                Danh mục ({filteredCategories.length}/{categories.length})
              </span>
              <button
                type="button"
                className="btn btn-primary btn-xs"
                onClick={() => {
                  setEditingCategory(null);
                  setShowCatModal(true);
                }}
              >
                <AddRoundedIcon sx={{ fontSize: 14 }} />
                Tạo mới
              </button>
            </div>

            <div style={{ position: 'relative' }}>
              <SearchRoundedIcon
                sx={{ fontSize: 16, position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--text-muted)' }}
              />
              <input
                className="form-input"
                style={{ paddingLeft: 32, fontSize: 12.5 }}
                placeholder="Tìm mã hoặc tên danh mục..."
                value={catSearch}
                onChange={(e) => setCatSearch(e.target.value)}
              />
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 6, maxHeight: 520, overflowY: 'auto' }}>
              {filteredCategories.map((c) => {
                const isActive = selectedCat?.categoryCode === c.categoryCode;
                return (
                  <div
                    key={c.categoryCode}
                    onClick={() => {
                      setItemSearch('');
                      onSelectCategory(c);
                    }}
                    style={{
                      textAlign: 'left',
                      padding: '10px 12px',
                      borderRadius: 10,
                      border: `1px solid ${isActive ? 'var(--accent-primary)' : 'var(--border-light)'}`,
                      backgroundColor: isActive ? 'var(--accent-primary-container)' : 'var(--bg-surface)',
                      cursor: 'pointer',
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 6,
                    }}
                  >
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 6 }}>
                      <div style={{ minWidth: 0 }}>
                        <div style={{ fontSize: 12.5, fontWeight: 700, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                          {c.categoryName}
                        </div>
                        <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>
                          <code>{c.categoryCode}</code> • {c.itemCount} mục
                        </div>
                      </div>
                    </div>

                    {isActive && (
                      <div style={{ display: 'flex', gap: 6 }} onClick={(e) => e.stopPropagation()}>
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          onClick={() => {
                            setEditingCategory(c);
                            setShowCatModal(true);
                          }}
                        >
                          <EditRoundedIcon sx={{ fontSize: 13 }} /> Sửa
                        </button>
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          style={{ color: '#d93025' }}
                          onClick={async () => {
                            if (!window.confirm(`Xóa danh mục "${c.categoryName}" (${c.categoryCode}) và toàn bộ ${c.itemCount} mục?`)) return;
                            const res = await fetch(`/api/categories/${encodeURIComponent(c.categoryCode)}`, {
                              method: 'DELETE',
                              credentials: 'include',
                            }).then((r) => r.json());
                            if (res?.success) {
                              clearCategoryCache(c.categoryCode);
                              await onRefreshCategory?.();
                              toast?.(`Đã xóa danh mục ${c.categoryCode}`);
                            } else {
                              toast?.(res?.message || 'Không xóa được danh mục.', 'error');
                            }
                          }}
                        >
                          <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} /> Xóa
                        </button>
                      </div>
                    )}
                  </div>
                );
              })}
              {filteredCategories.length === 0 && (
                <div style={{ fontSize: 12, color: 'var(--text-muted)', padding: 12, textAlign: 'center' }}>
                  Không tìm thấy danh mục phù hợp.
                </div>
              )}
            </div>
          </div>

          {/* Right Column: Category Items */}
          <div>
            {selectedCat ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10, paddingBottom: 12, borderBottom: '1px solid var(--border-light)' }}>
                  <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                      <h4 style={{ fontSize: 16, fontWeight: 700, margin: 0 }}>
                        {selectedCat.categoryName}
                      </h4>
                      <span className="badge badge-info">{catItems.length} giá trị</span>
                    </div>
                    <code style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>{selectedCat.categoryCode}</code>
                  </div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                    <button
                      type="button"
                      className="btn btn-outline btn-sm"
                      onClick={() => setShowImportCatModal(true)}
                    >
                      <FileUploadRoundedIcon sx={{ fontSize: 15 }} /> Import Excel
                    </button>
                    <button
                      type="button"
                      className="btn btn-outline btn-sm"
                      onClick={() => setShowExportCatModal(true)}
                    >
                      <FileDownloadRoundedIcon sx={{ fontSize: 15 }} /> Export Excel
                    </button>
                    <button
                      type="button"
                      className="btn btn-primary btn-sm"
                      onClick={() => {
                        setEditingItem(null);
                        setShowItemModal(true);
                      }}
                    >
                      <AddRoundedIcon sx={{ fontSize: 16 }} /> Thêm giá trị
                    </button>
                  </div>
                </div>

                {/* Filter items */}
                {catItems.length > 5 && (
                  <div style={{ position: 'relative', maxWidth: 320 }}>
                    <SearchRoundedIcon
                      sx={{ fontSize: 16, position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--text-muted)' }}
                    />
                    <input
                      className="form-input"
                      style={{ paddingLeft: 32, fontSize: 12.5 }}
                      placeholder="Lọc giá trị trong danh mục..."
                      value={itemSearch}
                      onChange={(e) => setItemSearch(e.target.value)}
                    />
                  </div>
                )}

                <div className="table-responsive">
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Mã giá trị</th>
                        <th>Tên hiển thị &amp; Nhãn</th>
                        <th style={{ width: 80, textAlign: 'center' }}>Thứ tự</th>
                        <th style={{ width: 150, textAlign: 'right' }}>Thao tác</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredCatItems.map((it) => {
                        const badgeColor = it.color || '#1a73e8';
                        return (
                          <tr key={it.id}>
                            <td><code>{it.itemCode}</code></td>
                            <td>
                              <span
                                style={{
                                  display: 'inline-flex',
                                  alignItems: 'center',
                                  gap: 6,
                                  padding: '3px 10px',
                                  borderRadius: 999,
                                  fontSize: 12,
                                  fontWeight: 600,
                                  backgroundColor: `${badgeColor}18`,
                                  color: badgeColor,
                                  border: `1px solid ${badgeColor}40`,
                                }}
                              >
                                <span style={{ width: 7, height: 7, borderRadius: '50%', backgroundColor: badgeColor }} />
                                {it.itemName}
                              </span>
                            </td>
                            <td style={{ textAlign: 'center' }}>{it.orderIndex}</td>
                            <td style={{ textAlign: 'right' }}>
                              <div style={{ display: 'inline-flex', gap: 6 }}>
                                <button
                                  type="button"
                                  className="btn btn-outline btn-xs"
                                  onClick={() => {
                                    setEditingItem(it);
                                    setShowItemModal(true);
                                  }}
                                >
                                  <EditRoundedIcon sx={{ fontSize: 13 }} /> Sửa
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-outline btn-xs"
                                  style={{ color: '#d93025' }}
                                  onClick={async () => {
                                    const res = await fetch(`/api/categories/items/${encodeURIComponent(it.id)}`, {
                                      method: 'DELETE',
                                      credentials: 'include',
                                    }).then((r) => r.json());
                                    if (res?.success) {
                                      clearCategoryCache(selectedCat.categoryCode);
                                      await onRefreshCategory?.();
                                      toast?.(`Đã xóa "${it.itemName}"`);
                                    } else {
                                      toast?.(res?.message || 'Không xóa được mục này.', 'error');
                                    }
                                  }}
                                >
                                  <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} /> Xóa
                                </button>
                              </div>
                            </td>
                          </tr>
                        );
                      })}
                      {filteredCatItems.length === 0 && (
                        <tr>
                          <td colSpan={4} style={{ textAlign: 'center', color: 'var(--text-muted)', padding: 20 }}>
                            {catItems.length === 0 ? 'Danh mục chưa có giá trị nào.' : 'Không có giá trị khớp từ khóa tìm kiếm.'}
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            ) : (
              <div style={{ padding: 28, textAlign: 'center', color: 'var(--text-muted)' }}>
                Chọn một danh mục ở cột trái để quản lý giá trị.
              </div>
            )}
          </div>
        </div>
      )}

      {/* 2. Roles & App Matrix */}
      {activeSubTab === 'roles' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
          <div style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
            Vai trò dùng chung cho toàn bộ hệ sinh thái; hỗ trợ Custom Roles và phân quyền chi tiết đến từng App &amp; từng Bảng.
          </div>

          <div className="table-responsive">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Vai trò (Role)</th>
                  <th>Loại</th>
                  <th>Quyền Hệ thống</th>
                  <th>Phân quyền theo từng App</th>
                  <th style={{ textAlign: 'center' }}>Users</th>
                  <th>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {roles.map((role) => (
                  <tr key={role.id}>
                    <td>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                        <span
                          className="badge"
                          style={{ backgroundColor: `${role.color}1a`, color: role.color, fontWeight: 700 }}
                        >
                          {role.code}
                        </span>
                        {!role.isActive && <span className="badge badge-danger">Tạm khóa</span>}
                      </div>
                      <div style={{ fontSize: 12.5, fontWeight: 600, marginTop: 3 }}>{role.name}</div>
                      {role.description && (
                        <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>{role.description}</div>
                      )}
                    </td>
                    <td>
                      <span className={`badge ${role.isSystem ? 'badge-warning' : 'badge-info'}`}>
                        {role.isSystem ? 'Hệ thống' : 'Custom Role'}
                      </span>
                    </td>
                    <td>
                      {role.code === 'ADMIN' ? (
                        <span className="badge badge-danger">Toàn quyền Admin</span>
                      ) : role.systemPermissions?.length > 0 ? (
                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                          {role.systemPermissions.map((sp) => (
                            <code key={sp} style={{ fontSize: 11 }}>
                              {sp}
                            </code>
                          ))}
                        </div>
                      ) : (
                        <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>Chỉ dùng App</span>
                      )}
                    </td>
                    <td>
                      {role.allAppsAccess ? (
                        <span className="badge badge-success">
                          Tất cả Apps ({role.defaultAppPermissions?.length || 0} quyền)
                        </span>
                      ) : role.appPermissions?.length > 0 ? (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                          {role.appPermissions.map((ap) => (
                            <div key={ap.appId} style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                              <span
                                className="badge"
                                style={{
                                  backgroundColor: `${ap.appColor || '#1a73e8'}1a`,
                                  color: ap.appColor || '#1a73e8',
                                  fontWeight: 700,
                                }}
                              >
                                {ap.appCode}
                              </span>
                              <span style={{ fontSize: 11.5, color: 'var(--text-secondary)' }}>
                                {ap.permissions.length} quyền ({ap.permissions.slice(0, 3).join(', ')}
                                {ap.permissions.length > 3 ? `, +${ap.permissions.length - 3}` : ''})
                              </span>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <span style={{ fontSize: 12, color: '#d93025' }}>Chưa phân quyền App nào</span>
                      )}
                    </td>
                    <td style={{ textAlign: 'center', fontWeight: 700 }}>{role.userCount}</td>
                    <td>
                      <div style={{ display: 'inline-flex', gap: 6 }}>
                        <button type="button" className="btn btn-outline btn-xs" onClick={() => onEditRole(role)}>
                          <EditRoundedIcon sx={{ fontSize: 13 }} /> Cấu hình
                        </button>
                        {!role.isSystem && (
                          <button
                            type="button"
                            className="btn btn-outline btn-xs"
                            style={{ color: '#d93025' }}
                            onClick={() => onDeleteRole(role)}
                          >
                            <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} /> Xóa
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Role x App Quick Matrix */}
          <div style={{ padding: 16, borderRadius: 12, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-light)' }}>
            <div style={{ marginBottom: 10 }}>
              <strong style={{ fontSize: 13.5 }}>Ma trận Phân quyền nhanh theo từng Ứng dụng (Role × App)</strong>
              <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                Nhấn vào tên App hoặc ô quyền để chỉnh quyền chi tiết của từng Role trên App và từng Bảng trong App đó.
              </div>
            </div>
            <div className="table-responsive">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Vai trò</th>
                    {apps.map((app) => (
                      <th key={app.id} style={{ textAlign: 'center' }}>
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          style={{ borderColor: app.color, color: app.color }}
                          onClick={() => onOpenAppPerms(app)}
                          title={`Phân quyền vào App ${app.code}`}
                        >
                          <ShieldRoundedIcon sx={{ fontSize: 13 }} /> {app.code}
                        </button>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {roles.map((role) => (
                    <tr key={role.id}>
                      <td>
                        <span className="badge" style={{ backgroundColor: `${role.color}1a`, color: role.color, fontWeight: 700 }}>
                          {role.code}
                        </span>
                      </td>
                      {apps.map((app) => {
                        const rap = role.appPermissions?.find((x) => x.appId === app.id);
                        const count = role.allAppsAccess
                          ? role.defaultAppPermissions?.length || 0
                          : rap?.permissions?.length || 0;
                        return (
                          <td key={app.id} style={{ textAlign: 'center' }}>
                            <button
                              type="button"
                              onClick={() => onOpenAppPerms(app)}
                              className={`badge ${count > 0 ? 'badge-success' : ''}`}
                              style={{
                                border: 'none',
                                cursor: 'pointer',
                                backgroundColor: count > 0 ? undefined : 'var(--bg-secondary)',
                                color: count > 0 ? undefined : 'var(--text-muted)',
                              }}
                            >
                              {count > 0 ? `${count} quyền` : 'Không'}
                            </button>
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* 3. Users */}
      {activeSubTab === 'users' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
            <div style={{ position: 'relative', width: 320, maxWidth: '100%' }}>
              <SearchRoundedIcon sx={{ fontSize: 17, color: 'var(--text-muted)', position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)' }} />
              <input
                className="form-input"
                style={{ paddingLeft: 32, height: 34 }}
                placeholder="Tìm username, họ tên, mã NV, email..."
                value={userQuery}
                onChange={(e) => setUserQuery(e.target.value)}
              />
            </div>
            <span style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
              Hiển thị {filteredUsers.length} / {users.length} tài khoản dùng chung toàn hệ thống
            </span>
          </div>

          <div className="table-responsive">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Tài khoản &amp; Mã NV</th>
                  <th>Họ tên &amp; Email</th>
                  <th>Phòng ban</th>
                  <th>Vai trò được gán</th>
                  <th>Quyền theo App (Kế thừa)</th>
                  <th>Trạng thái</th>
                  <th>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {filteredUsers.map((u) => {
                  const rCodes = u.roleCodes?.length ? u.roleCodes : [u.roleCode];
                  const appEntries = u.appPermissions ? Object.entries(u.appPermissions) : [];
                  return (
                    <tr key={u.id}>
                      <td>
                        <code>{u.username}</code>
                        {u.employeeCode && (
                          <div style={{ fontSize: 11, color: '#00897b', fontWeight: 700, marginTop: 3 }}>
                            NV: {u.employeeCode}
                          </div>
                        )}
                      </td>
                      <td>
                        <div style={{ fontWeight: 600 }}>{u.fullName}</div>
                        {u.email && <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>{u.email}</div>}
                        {u.ssoProvider && (
                          <span className="badge badge-info" style={{ marginTop: 3, fontSize: 10 }}>
                            SSO: {u.ssoProvider}
                          </span>
                        )}
                      </td>
                      <td>{u.department}</td>
                      <td>
                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                          {rCodes.map((rc) => {
                            const roleObj = roles.find((r) => r.code.toUpperCase() === rc.toUpperCase());
                            const color = roleObj?.color || '#1a73e8';
                            return (
                              <span
                                key={rc}
                                className="badge"
                                style={{ backgroundColor: `${color}1a`, color, fontWeight: 700 }}
                              >
                                {rc}
                              </span>
                            );
                          })}
                        </div>
                      </td>
                      <td>
                        {u.isAdmin ? (
                          <span className="badge badge-danger">Tất cả Apps (*)</span>
                        ) : appEntries.length > 0 ? (
                          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                            {appEntries.map(([ac, perms]) => (
                              <span key={ac} className="badge badge-info" title={perms.join(', ')}>
                                {ac}: {perms.length}
                              </span>
                            ))}
                          </div>
                        ) : (
                          <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                            {u.allowedApps?.length ? u.allowedApps.join(', ') : 'Chưa cấp App'}
                          </span>
                        )}
                      </td>
                      <td>
                        <span className={`badge ${u.isActive ? 'badge-success' : 'badge-danger'}`}>
                          {u.isActive ? 'Hoạt động' : 'Đã khóa'}
                        </span>
                      </td>
                      <td>
                        <div style={{ display: 'inline-flex', gap: 6, flexWrap: 'wrap' }}>
                          <button type="button" className="btn btn-outline btn-xs" onClick={() => onEditUser(u)}>
                            <EditRoundedIcon sx={{ fontSize: 13 }} /> Sửa
                          </button>
                          <button type="button" className="btn btn-outline btn-xs" onClick={() => onResetUserPw(u)}>
                            <KeyRoundedIcon sx={{ fontSize: 13 }} /> Reset MK
                          </button>
                          {u.id !== 'usr_admin' && u.id !== currentUserId && (
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              style={{ color: '#d93025' }}
                              onClick={() => onDeleteUser(u)}
                            >
                              <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} /> Xóa
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
                {filteredUsers.length === 0 && (
                  <tr>
                    <td colSpan={7} style={{ textAlign: 'center', color: 'var(--text-muted)', padding: 20 }}>
                      Không tìm thấy tài khoản phù hợp.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* 4. Audit Logs */}
      {activeSubTab === 'audit' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ display: 'flex', gap: 10, alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap' }}>
            <span style={{ fontSize: 13, fontWeight: 600 }}>Nhật ký kiểm toán dữ liệu toàn hệ thống</span>
            <select
              className="form-select"
              style={{ maxWidth: 260 }}
              value={auditAppFilter}
              onChange={(e) => onChangeAuditAppFilter(e.target.value)}
            >
              <option value="">Tất cả Ứng dụng</option>
              {apps.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.code} — {a.name}
                </option>
              ))}
            </select>
          </div>
          <div className="table-responsive">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Thời gian</th>
                  <th>Hành động</th>
                  <th>Người thực hiện</th>
                  <th>Bản ghi</th>
                  <th>Tóm tắt</th>
                </tr>
              </thead>
              <tbody>
                {globalAuditLogs.map((log) => (
                  <tr key={log.id}>
                    <td style={{ fontSize: 12, color: 'var(--text-muted)', whiteSpace: 'nowrap' }}>
                      {new Date(log.createdAt).toLocaleString('vi-VN')}
                    </td>
                    <td><span className="badge badge-info">{log.action}</span></td>
                    <td><code>{log.actor}</code></td>
                    <td><code>{log.recordId}</code></td>
                    <td>{log.summary}</td>
                  </tr>
                ))}
                {globalAuditLogs.length === 0 && (
                  <tr>
                    <td colSpan={5} style={{ textAlign: 'center', color: 'var(--text-muted)', padding: 20 }}>
                      Chưa có nhật ký thay đổi nào.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      <CategoryFormModal
        open={showCatModal}
        editing={editingCategory}
        onClose={() => setShowCatModal(false)}
        onSaved={async () => {
          await onRefreshCategory?.();
        }}
        toast={toast}
      />
      <CategoryItemFormModal
        open={showItemModal}
        category={selectedCat}
        editing={editingItem}
        defaultOrder={catItems.length + 1}
        onClose={() => setShowItemModal(false)}
        onSaved={async () => {
          await onRefreshCategory?.();
        }}
        toast={toast}
      />
      <CategoryExportPreviewModal
        open={showExportCatModal}
        category={selectedCat}
        items={catItems}
        categoriesCount={categories.length}
        onClose={() => setShowExportCatModal(false)}
        toast={toast}
      />
      <CategoryImportPreviewModal
        open={showImportCatModal}
        category={selectedCat}
        existingItems={catItems}
        onClose={() => setShowImportCatModal(false)}
        onImported={async () => {
          if (onRefreshCategory) await onRefreshCategory();
        }}
        toast={toast}
      />
    </div>
  );
}
