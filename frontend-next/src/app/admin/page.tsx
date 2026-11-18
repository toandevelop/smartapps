'use client';

import React, { useState, useEffect, useCallback, useMemo } from 'react';
import Link from 'next/link';
import {
  PageHeader,
  EmptyState,
  ConfirmDialog,
  NotificationSnackbar,
  StatusBadge,
} from '@/app/components/ui';
import UserFormModal from '@/app/studio/UserFormModal';
import { AutomationModal } from '@/app/studio/StudioModals';
import RoleFormModal from './RoleFormModal';
import WorkflowFormModal from './WorkflowFormModal';
import { usePlatform } from '@/lib/app-context';
import { useAuth } from '@/lib/auth-context';
import {
  AppAutomation,
  AppAutomationLog,
  AppRoleMatrixItem,
  AppWorkflow,
  PermissionCatalog,
  SysModule,
  SysRole,
  SysTable,
  SysUser,
} from '@/lib/types';

import AdminPanelSettingsRoundedIcon from '@mui/icons-material/AdminPanelSettingsRounded';
import PeopleRoundedIcon from '@mui/icons-material/PeopleRounded';
import SecurityRoundedIcon from '@mui/icons-material/SecurityRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import LockOpenRoundedIcon from '@mui/icons-material/LockOpenRounded';
import VpnKeyRoundedIcon from '@mui/icons-material/VpnKeyRounded';
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import KeyRoundedIcon from '@mui/icons-material/KeyRounded';
import { useEscapeKey } from '@/lib/ui-hooks';

type AdminTab = 'users' | 'roles' | 'workflows' | 'automations';

const TRIGGER_LABELS: Record<string, string> = {
  RECORD_CREATED: 'Khi Tạo mới',
  RECORD_UPDATED: 'Khi Cập nhật',
  ANY_SAVE: 'Mỗi khi Lưu',
  RECORD_APPROVED: 'Khi Phê duyệt xong',
  RECORD_REJECTED: 'Khi Bị từ chối',
  SCHEDULED_DATE: 'Theo lịch / Ngày đến hạn',
};

const ACTION_LABELS: Record<string, string> = {
  CREATE_USER: 'Tạo tài khoản User (sys_users)',
  UPDATE_USER_STATUS: 'Khóa / Mở khóa User',
  CREATE_TASK: 'Giao Công việc (App Giao việc)',
  CREATE_RECORD: 'Tạo bản ghi đích',
  UPDATE_RECORD: 'Cập nhật bảng đích',
  UPDATE_SELF: 'Tự cập nhật bản ghi nguồn',
  SEND_NOTIFICATION: 'Gửi Thông báo',
  CALL_WEBHOOK: 'Gọi Webhook',
};

export default function AdminGovernancePage() {
  const { apps } = usePlatform();
  const { user: currentUser, hasPermission, refreshAuth } = useAuth();

  const canUsers = hasPermission('user.manage') || hasPermission('studio.manage');
  const canRoles = hasPermission('role.manage') || hasPermission('user.manage') || hasPermission('studio.manage');
  const canWorkflows = hasPermission('workflow.manage') || hasPermission('studio.manage');
  const canAutomations = hasPermission('automation.manage') || hasPermission('studio.manage');
  const hasAnyAdminPerm = canUsers || canRoles || canWorkflows || canAutomations;

  const [activeTab, setActiveTab] = useState<AdminTab>('users');

  // Shared metadata
  const [allTables, setAllTables] = useState<SysTable[]>([]);
  const [allModules, setAllModules] = useState<SysModule[]>([]);

  // Users & Roles
  const [users, setUsers] = useState<SysUser[]>([]);
  const [roles, setRoles] = useState<SysRole[]>([]);
  const [catalog, setCatalog] = useState<PermissionCatalog>({ systemPermissions: [], appPermissions: [] });
  const [userSearch, setUserSearch] = useState('');
  const [userRoleFilter, setUserRoleFilter] = useState('ALL');
  const [showUserModal, setShowUserModal] = useState(false);
  const [editingUser, setEditingUser] = useState<SysUser | null>(null);
  const [resetPwUser, setResetPwUser] = useState<SysUser | null>(null);
  const [resetPwVal, setResetPwVal] = useState('123456');

  // Role & App Matrix
  const [showRoleModal, setShowRoleModal] = useState(false);
  const [editingRole, setEditingRole] = useState<SysRole | null>(null);
  const [matrixAppId, setMatrixAppId] = useState('');
  const [matrixRows, setMatrixRows] = useState<AppRoleMatrixItem[]>([]);
  const [savingMatrix, setSavingMatrix] = useState(false);

  // Workflows
  const [workflows, setWorkflows] = useState<AppWorkflow[]>([]);
  const [showWorkflowModal, setShowWorkflowModal] = useState(false);
  const [editingWorkflow, setEditingWorkflow] = useState<AppWorkflow | null>(null);

  // Automations
  const [automations, setAutomations] = useState<AppAutomation[]>([]);
  const [autoLogs, setAutoLogs] = useState<AppAutomationLog[]>([]);
  const [showAutoModal, setShowAutoModal] = useState(false);
  const [editingAuto, setEditingAuto] = useState<AppAutomation | null>(null);
  const [dryRunResult, setDryRunResult] = useState<{ title: string; data: any } | null>(null);
  useEscapeKey(Boolean(resetPwUser), () => setResetPwUser(null));
  useEscapeKey(Boolean(dryRunResult), () => setDryRunResult(null));

  // Dialogs & snackbar
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });
  const [snack, setSnack] = useState<{ open: boolean; message: string; severity: 'success' | 'error' | 'info' }>({
    open: false,
    message: '',
    severity: 'success',
  });
  const toast = (message: string, severity: 'success' | 'error' | 'info' = 'success') =>
    setSnack({ open: true, message, severity });

  const loadMetadata = useCallback(async () => {
    const [tRes, mRes] = await Promise.all([
      fetch('/api/tables', { credentials: 'include' }).then((r) => r.json()),
      fetch('/api/modules', { credentials: 'include' }).then((r) => r.json()),
    ]);
    if (tRes.success) setAllTables(tRes.data || []);
    if (mRes.success) setAllModules(mRes.data || []);
  }, []);

  const loadUsersAndRoles = useCallback(async () => {
    const [uRes, rRes, cRes] = await Promise.all([
      fetch('/api/auth/users', { credentials: 'include' }).then((r) => r.json()).catch(() => ({ success: false })),
      fetch('/api/roles', { credentials: 'include' }).then((r) => r.json()).catch(() => ({ success: false })),
      fetch('/api/roles/catalog', { credentials: 'include' }).then((r) => r.json()).catch(() => ({ success: false })),
    ]);
    if (uRes.success) setUsers(uRes.data || []);
    if (rRes.success) setRoles(rRes.data || []);
    if (cRes.success && cRes.data) setCatalog(cRes.data);
  }, []);

  const loadWorkflows = useCallback(async () => {
    const res = await fetch('/api/workflows', { credentials: 'include' }).then((r) => r.json()).catch(() => ({ success: false }));
    if (res.success) setWorkflows(res.data || []);
  }, []);

  const loadAutomations = useCallback(async () => {
    const [aRes, lRes] = await Promise.all([
      fetch('/api/automations', { credentials: 'include' }).then((r) => r.json()).catch(() => ({ success: false })),
      fetch('/api/automations/logs?limit=25', { credentials: 'include' }).then((r) => r.json()).catch(() => ({ success: false })),
    ]);
    if (aRes.success) setAutomations(aRes.data || []);
    if (lRes.success) setAutoLogs(lRes.data || []);
  }, []);

  const loadAppMatrix = useCallback(async (appId: string) => {
    if (!appId) {
      setMatrixRows([]);
      return;
    }
    const res = await fetch(`/api/roles/by-app/${encodeURIComponent(appId)}`, { credentials: 'include' })
      .then((r) => r.json())
      .catch(() => ({ success: false }));
    if (res.success) setMatrixRows(res.data || []);
  }, []);

  useEffect(() => {
    if (!hasAnyAdminPerm) return;
    loadMetadata();
    loadUsersAndRoles();
    loadWorkflows();
    loadAutomations();
  }, [hasAnyAdminPerm, loadMetadata, loadUsersAndRoles, loadWorkflows, loadAutomations]);

  useEffect(() => {
    if (!matrixAppId && apps.length > 0) {
      setMatrixAppId(apps[0].id);
    }
  }, [apps, matrixAppId]);

  useEffect(() => {
    if (matrixAppId && canRoles) {
      loadAppMatrix(matrixAppId);
    }
  }, [matrixAppId, canRoles, loadAppMatrix]);

  const filteredUsers = useMemo(() => {
    return users.filter((u) => {
      const q = userSearch.trim().toLowerCase();
      const matchSearch =
        !q ||
        u.username.toLowerCase().includes(q) ||
        u.fullName.toLowerCase().includes(q) ||
        (u.department || '').toLowerCase().includes(q) ||
        (u.email || '').toLowerCase().includes(q);
      const userRoles = u.roleCodes?.length ? u.roleCodes : [u.roleCode];
      const matchRole = userRoleFilter === 'ALL' || userRoles.some((rc) => rc.toUpperCase() === userRoleFilter.toUpperCase());
      return matchSearch && matchRole;
    });
  }, [users, userSearch, userRoleFilter]);

  if (!hasAnyAdminPerm) {
    return (
      <div className="page-content">
        <EmptyState
          title="Khu vực Quản trị Hệ thống & Quy trình"
          description={`Tài khoản "${currentUser?.fullName || currentUser?.username}" không có quyền truy cập khu vực quản trị.`}
          icon={<LockRoundedIcon sx={{ fontSize: 28, color: '#d93025' }} />}
          action={<Link href="/" className="btn btn-primary">Về Cổng Ứng dụng</Link>}
        />
      </div>
    );
  }

  const toggleMatrixPerm = (roleId: string, permCode: string) => {
    setMatrixRows((prev) =>
      prev.map((row) => {
        if (row.roleId !== roleId || row.roleCode === 'ADMIN' || row.allAppsAccess) return row;
        const has = row.permissions.includes(permCode);
        const nextPerms = has ? row.permissions.filter((x) => x !== permCode) : [...row.permissions, permCode];
        if (!has && permCode !== 'record.view' && !nextPerms.includes('record.view')) {
          nextPerms.unshift('record.view');
        }
        return { ...row, hasAccess: nextPerms.length > 0, permissions: nextPerms };
      })
    );
  };

  const setMatrixRowScope = (roleId: string, rowScope: string) => {
    setMatrixRows((prev) => prev.map((row) => (row.roleId === roleId ? { ...row, rowScope } : row)));
  };

  const saveAppMatrix = async () => {
    if (!matrixAppId) return;
    setSavingMatrix(true);
    try {
      const res = await fetch(`/api/roles/by-app/${encodeURIComponent(matrixAppId)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          assignments: matrixRows.map((r) => ({
            roleId: r.roleId,
            hasAccess: r.hasAccess,
            permissions: r.permissions,
            rowScope: r.rowScope,
          })),
        }),
      }).then((r) => r.json());
      if (res.success) {
        await loadUsersAndRoles();
        await refreshAuth();
        toast('Đã lưu ma trận phân quyền App!');
      } else {
        toast(res.message || 'Không thể lưu ma trận', 'error');
      }
    } finally {
      setSavingMatrix(false);
    }
  };

  return (
    <div>
      <PageHeader
        title="Quản trị & Tự động hóa"
        subtitle="Quản lý Tài khoản (Users), Vai trò (Roles), Ma trận Phân quyền, Quy trình Phê duyệt (Workflow) & Automation"
        icon={<AdminPanelSettingsRoundedIcon sx={{ fontSize: 24 }} />}
        accentColor="#1a73e8"
        breadcrumbs={[{ label: 'Cổng Ứng dụng', href: '/' }, { label: 'Quản trị & Quy trình' }]}
        actions={
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {(canUsers || canRoles) && (
              <Link href="/settings?tab=sso" className="btn btn-outline btn-sm">
                <KeyRoundedIcon sx={{ fontSize: 16 }} /> Cấu hình SSO
              </Link>
            )}
            {activeTab === 'users' && canUsers && (
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => {
                  setEditingUser(null);
                  setShowUserModal(true);
                }}
              >
                <AddRoundedIcon sx={{ fontSize: 17 }} /> Thêm Người dùng
              </button>
            )}
            {activeTab === 'roles' && canRoles && (
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => {
                  setEditingRole(null);
                  setShowRoleModal(true);
                }}
              >
                <AddRoundedIcon sx={{ fontSize: 17 }} /> Tạo Vai trò Mới
              </button>
            )}
            {activeTab === 'workflows' && canWorkflows && (
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => {
                  setEditingWorkflow(null);
                  setShowWorkflowModal(true);
                }}
              >
                <AddRoundedIcon sx={{ fontSize: 17 }} /> Thiết kế Workflow
              </button>
            )}
            {activeTab === 'automations' && canAutomations && (
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => {
                  setEditingAuto(null);
                  setShowAutoModal(true);
                }}
              >
                <AddRoundedIcon sx={{ fontSize: 17 }} /> Tạo Quy tắc Tự động
              </button>
            )}
          </div>
        }
      />

      <div className="page-content">
        {/* Top Segmented Tabs + Quick Metrics */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
          <div className="segmented-tabs">
            {canUsers && (
              <button
                type="button"
                className={`segmented-tab ${activeTab === 'users' ? 'active' : ''}`}
                onClick={() => setActiveTab('users')}
              >
                <PeopleRoundedIcon sx={{ fontSize: 16 }} /> Người dùng ({users.length})
              </button>
            )}
            {canRoles && (
              <button
                type="button"
                className={`segmented-tab ${activeTab === 'roles' ? 'active' : ''}`}
                onClick={() => setActiveTab('roles')}
              >
                <SecurityRoundedIcon sx={{ fontSize: 16 }} /> Vai trò &amp; Phân quyền ({roles.length})
              </button>
            )}
            {canWorkflows && (
              <button
                type="button"
                className={`segmented-tab ${activeTab === 'workflows' ? 'active' : ''}`}
                onClick={() => setActiveTab('workflows')}
              >
                <AccountTreeRoundedIcon sx={{ fontSize: 16 }} /> Workflow ({workflows.length})
              </button>
            )}
            {canAutomations && (
              <button
                type="button"
                className={`segmented-tab ${activeTab === 'automations' ? 'active' : ''}`}
                onClick={() => setActiveTab('automations')}
              >
                <BoltRoundedIcon sx={{ fontSize: 16 }} /> Automation ({automations.length})
              </button>
            )}
          </div>
        </div>

        {/* TAB 1: USERS */}
        {activeTab === 'users' && (
          <div className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 14 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                <div style={{ position: 'relative', width: 280 }}>
                  <SearchRoundedIcon sx={{ fontSize: 17, color: 'var(--text-muted)', position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)' }} />
                  <input
                    className="form-input"
                    style={{ paddingLeft: 32, height: 34 }}
                    placeholder="Tìm tên đăng nhập, họ tên, phòng ban..."
                    value={userSearch}
                    onChange={(e) => setUserSearch(e.target.value)}
                  />
                </div>
                <select
                  className="form-select"
                  style={{ width: 200, height: 34 }}
                  value={userRoleFilter}
                  onChange={(e) => setUserRoleFilter(e.target.value)}
                >
                  <option value="ALL">Tất cả Vai trò</option>
                  {roles.map((r) => (
                    <option key={r.id} value={r.code}>{r.name} ({r.code})</option>
                  ))}
                </select>
              </div>
              <div style={{ display: 'flex', gap: 8 }}>
                <span className="badge badge-success">Đang hoạt động: {users.filter((u) => u.isActive).length}</span>
                <span className="badge badge-danger">Đã khóa: {users.filter((u) => !u.isActive).length}</span>
              </div>
            </div>

            <div style={{ overflowX: 'auto' }}>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Tài khoản</th>
                    <th>Họ và tên</th>
                    <th>Phòng ban</th>
                    <th>Vai trò được gán</th>
                    <th>Phạm vi Ứng dụng</th>
                    <th>Trạng thái</th>
                    <th style={{ width: 240 }}>Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredUsers.map((u) => {
                    const userRoleCodes = u.roleCodes?.length ? u.roleCodes : [u.roleCode];
                    return (
                      <tr key={u.id}>
                        <td><code>{u.username}</code></td>
                        <td>
                          <div style={{ fontWeight: 600 }}>{u.fullName}</div>
                          {u.email && <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>{u.email}</div>}
                          {u.ssoProvider && (
                            <span className="badge" style={{ marginTop: 2, fontSize: 10, backgroundColor: '#e8f0fe', color: '#1967d2' }}>
                              SSO: {u.ssoProvider}
                            </span>
                          )}
                        </td>
                        <td>{u.department}</td>
                        <td>
                          <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
                            {userRoleCodes.map((rc) => {
                              const roleObj = roles.find((r) => r.code.toUpperCase() === rc.toUpperCase());
                              const c = roleObj?.color || '#1a73e8';
                              return (
                                <span key={rc} className="badge" style={{ backgroundColor: `${c}1a`, color: c, fontWeight: 600 }}>
                                  {roleObj?.name || rc}
                                </span>
                              );
                            })}
                          </div>
                        </td>
                        <td>
                          {u.allowedApps.includes('*') ? (
                            <span className="badge badge-info">Tất cả Apps</span>
                          ) : u.allowedApps.length > 0 ? (
                            <span style={{ fontSize: 12 }}>{u.allowedApps.join(', ')}</span>
                          ) : (
                            <span style={{ fontSize: 12, color: '#c5221f' }}>Chưa cấp App</span>
                          )}
                        </td>
                        <td>
                          <StatusBadge status={u.isActive ? 'ACTIVE' : 'ERROR'} label={u.isActive ? 'Hoạt động' : 'Đã khóa'} size="sm" />
                        </td>
                        <td>
                          <div style={{ display: 'inline-flex', gap: 5, flexWrap: 'wrap' }}>
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              onClick={() => {
                                setEditingUser(u);
                                setShowUserModal(true);
                              }}
                            >
                              <EditRoundedIcon sx={{ fontSize: 13 }} /> Sửa
                            </button>
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              onClick={() => {
                                setResetPwUser(u);
                                setResetPwVal('123456');
                              }}
                              title="Đặt lại mật khẩu"
                            >
                              <VpnKeyRoundedIcon sx={{ fontSize: 13 }} /> Đổi MK
                            </button>
                            {u.id !== 'usr_admin' && u.id !== currentUser?.id && (
                              <>
                                <button
                                  type="button"
                                  className="btn btn-outline btn-xs"
                                  style={{ color: u.isActive ? '#ea8600' : '#1e8e3e' }}
                                  onClick={async () => {
                                    const res = await fetch(`/api/auth/users/${u.id}/status`, {
                                      method: 'PATCH',
                                      headers: { 'Content-Type': 'application/json' },
                                      credentials: 'include',
                                      body: JSON.stringify({ isActive: !u.isActive }),
                                    }).then((r) => r.json());
                                    if (res.success) {
                                      await loadUsersAndRoles();
                                      toast(res.message);
                                    } else {
                                      toast(res.message || 'Không thể thay đổi trạng thái', 'error');
                                    }
                                  }}
                                  title={u.isActive ? 'Khóa tài khoản' : 'Mở khóa tài khoản'}
                                >
                                  {u.isActive ? <LockRoundedIcon sx={{ fontSize: 13 }} /> : <LockOpenRoundedIcon sx={{ fontSize: 13 }} />}
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-outline btn-xs"
                                  style={{ color: '#d93025' }}
                                  aria-label={`Xóa tài khoản ${u.username}`}
                                  title={`Xóa tài khoản ${u.username}`}
                                  onClick={() => {
                                    setConfirm({
                                      open: true,
                                      title: `Xóa tài khoản ${u.username}`,
                                      message: `Xác nhận xóa vĩnh viễn tài khoản "${u.fullName}"?`,
                                      onConfirm: async () => {
                                        setConfirm((c) => ({ ...c, open: false }));
                                        const res = await fetch(`/api/auth/users/${u.id}`, {
                                          method: 'DELETE',
                                          credentials: 'include',
                                        }).then((r) => r.json());
                                        if (res.success) {
                                          await loadUsersAndRoles();
                                          toast('Đã xóa tài khoản.');
                                        } else {
                                          toast(res.message || 'Không thể xóa', 'error');
                                        }
                                      },
                                    });
                                  }}
                                >
                                  <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} />
                                </button>
                              </>
                            )}
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* TAB 2: ROLES & RBAC MATRIX */}
        {activeTab === 'roles' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            {/* Role Cards */}
            <div className="card" style={{ padding: 18 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
                <div>
                  <strong style={{ fontSize: 15 }}>Danh sách Vai trò Hệ thống &amp; Vai trò Tùy chỉnh</strong>
                  <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                    Tạo vai trò riêng theo cơ cấu phòng ban, cấp quyền quản trị và gán quyền truy cập từng App
                  </div>
                </div>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(290px, 1fr))', gap: 12 }}>
                {roles.map((r) => (
                  <div
                    key={r.id}
                    style={{
                      padding: 14,
                      borderRadius: 12,
                      border: '1px solid var(--border-subtle)',
                      backgroundColor: '#fcfcfd',
                      display: 'flex',
                      flexDirection: 'column',
                      justifyContent: 'space-between',
                      gap: 10,
                    }}
                  >
                    <div>
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8 }}>
                        <div>
                          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                            <span className="badge" style={{ backgroundColor: `${r.color}1a`, color: r.color, fontWeight: 700 }}>
                              {r.code}
                            </span>
                            {r.isSystem && <span className="badge" style={{ backgroundColor: '#e8eaed', color: '#3c4043' }}>Mặc định</span>}
                          </div>
                          <div style={{ fontSize: 14.5, fontWeight: 700, marginTop: 4 }}>{r.name}</div>
                        </div>
                        <span className="badge badge-info">{r.userCount} user</span>
                      </div>
                      <p style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4, lineHeight: 1.45 }}>
                        {r.description || 'Chưa có mô tả'}
                      </p>
                      <div style={{ fontSize: 11.5, color: 'var(--text-secondary)', marginTop: 8 }}>
                        <strong>Quyền App: </strong>
                        {r.allAppsAccess
                          ? 'Tất cả Ứng dụng'
                          : r.appPermissions.length > 0
                          ? r.appPermissions.map((ap) => ap.appCode).join(', ')
                          : 'Chưa cấp App'}
                      </div>
                      {r.systemPermissions.length > 0 && (
                        <div style={{ fontSize: 11.5, color: 'var(--text-secondary)', marginTop: 4 }}>
                          <strong>Hệ thống: </strong>{r.systemPermissions.length} quyền quản trị
                        </div>
                      )}
                    </div>

                    <div style={{ display: 'flex', gap: 6, borderTop: '1px solid var(--border-light)', paddingTop: 10 }}>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        onClick={() => {
                          setEditingRole(r);
                          setShowRoleModal(true);
                        }}
                      >
                        <EditRoundedIcon sx={{ fontSize: 13 }} /> Cấu hình Quyền
                      </button>
                      {!r.isSystem && r.code !== 'ADMIN' && (
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          style={{ color: '#d93025' }}
                          onClick={() => {
                            setConfirm({
                              open: true,
                              title: `Xóa vai trò ${r.code}`,
                              message: `Xác nhận xóa vai trò "${r.name}"?`,
                              onConfirm: async () => {
                                setConfirm((c) => ({ ...c, open: false }));
                                const res = await fetch(`/api/roles/${r.id}`, {
                                  method: 'DELETE',
                                  credentials: 'include',
                                }).then((x) => x.json());
                                if (res.success) {
                                  await loadUsersAndRoles();
                                  if (matrixAppId) await loadAppMatrix(matrixAppId);
                                  toast('Đã xóa vai trò!');
                                } else {
                                  toast(res.message || 'Không thể xóa vai trò', 'error');
                                }
                              },
                            });
                          }}
                        >
                          <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} /> Xóa
                        </button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Interactive App RBAC Matrix */}
            <div className="card" style={{ padding: 18 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10, marginBottom: 14 }}>
                <div>
                  <strong style={{ fontSize: 15 }}>Ma trận Phân quyền nhanh theo từng Ứng dụng</strong>
                  <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                    Chọn một Ứng dụng để tinh chỉnh trực tiếp quyền Xem / Tạo / Sửa / Xóa / Phê duyệt của mọi Vai trò
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                  <select
                    className="form-select"
                    style={{ width: 220, height: 34 }}
                    value={matrixAppId}
                    onChange={(e) => setMatrixAppId(e.target.value)}
                  >
                    {apps.map((a) => (
                      <option key={a.id} value={a.id}>{a.name} ({a.code})</option>
                    ))}
                  </select>
                  <button
                    type="button"
                    className="btn btn-primary btn-sm"
                    disabled={savingMatrix || !matrixAppId}
                    onClick={saveAppMatrix}
                  >
                    <SaveRoundedIcon sx={{ fontSize: 15 }} /> {savingMatrix ? 'Đang lưu...' : 'Lưu Ma trận'}
                  </button>
                </div>
              </div>

              <div style={{ overflowX: 'auto' }}>
                <table className="data-table" style={{ fontSize: 12.5 }}>
                  <thead>
                    <tr>
                      <th style={{ minWidth: 180 }}>Vai trò</th>
                      {catalog.appPermissions.map((ap) => (
                        <th key={ap.code} style={{ textAlign: 'center', minWidth: 85 }} title={ap.description}>
                          {ap.name}
                        </th>
                      ))}
                      <th style={{ minWidth: 125 }}>Phạm vi dòng</th>
                    </tr>
                  </thead>
                  <tbody>
                    {matrixRows.map((row) => {
                      const locked = row.roleCode === 'ADMIN' || row.allAppsAccess;
                      return (
                        <tr key={row.roleId}>
                          <td>
                            <span className="badge" style={{ backgroundColor: `${row.roleColor}1a`, color: row.roleColor, marginRight: 6, fontWeight: 700 }}>
                              {row.roleCode}
                            </span>
                            <strong>{row.roleName}</strong>
                            {row.allAppsAccess && (
                              <span style={{ display: 'block', fontSize: 10.5, color: 'var(--text-muted)' }}>
                                (Truy cập tất cả App)
                              </span>
                            )}
                          </td>
                          {catalog.appPermissions.map((ap) => (
                            <td key={ap.code} style={{ textAlign: 'center' }}>
                              <input
                                type="checkbox"
                                disabled={locked}
                                checked={locked ? row.roleCode === 'ADMIN' || row.permissions.includes(ap.code) : row.permissions.includes(ap.code)}
                                onChange={() => toggleMatrixPerm(row.roleId, ap.code)}
                                style={{ width: 15, height: 15, cursor: locked ? 'not-allowed' : 'pointer' }}
                              />
                            </td>
                          ))}
                          <td>
                            <select
                              className="form-select"
                              disabled={locked}
                              style={{ height: 28, fontSize: 11.5, padding: '2px 6px' }}
                              value={row.rowScope}
                              onChange={(e) => setMatrixRowScope(row.roleId, e.target.value)}
                            >
                              <option value="INHERIT">Theo Bảng</option>
                              <option value="ALL">Xem tất cả</option>
                              <option value="DEPARTMENT">Theo phòng ban</option>
                              <option value="OWNER_ONLY">Chỉ của tôi</option>
                            </select>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        )}

        {/* TAB 3: WORKFLOWS */}
        {activeTab === 'workflows' && (
          <div className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 14 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
              <div>
                <strong style={{ fontSize: 15 }}>Quy trình Phê duyệt Đa bước (Approval Workflows)</strong>
                <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                  Thiết lập luồng trình duyệt qua nhiều cấp vai trò, theo dõi SLA và tự động kích hoạt Automation khi duyệt xong
                </div>
              </div>
              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => {
                  setEditingWorkflow(null);
                  setShowWorkflowModal(true);
                }}
              >
                <AddRoundedIcon sx={{ fontSize: 16 }} /> Tạo Quy trình Mới
              </button>
            </div>

            {workflows.length === 0 ? (
              <EmptyState
                title="Chưa có quy trình Workflow nào"
                description="Hãy thiết kế quy trình phê duyệt nhiều cấp đầu tiên cho bảng dữ liệu doanh nghiệp."
              />
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {workflows.map((wf) => (
                  <div
                    key={wf.id}
                    style={{
                      padding: 16,
                      borderRadius: 14,
                      border: '1px solid var(--border-subtle)',
                      backgroundColor: '#fcfcfd',
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 12,
                    }}
                  >
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 10 }}>
                      <div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                          <span className="badge badge-info">{wf.appCode}</span>
                          <strong style={{ fontSize: 15 }}>{wf.name}</strong>
                          <StatusBadge status={wf.isActive ? 'ACTIVE' : 'PENDING'} label={wf.isActive ? 'Đang chạy' : 'Tạm dừng'} size="sm" />
                          {wf.pendingCount > 0 && (
                            <span className="badge badge-warning">{wf.pendingCount} hồ sơ đang chờ duyệt</span>
                          )}
                        </div>
                        <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4 }}>
                          Bảng áp dụng: <strong>{wf.tableDisplayName || wf.tableId}</strong>
                          {wf.conditionField && (
                            <> • Điều kiện: <code>{wf.conditionField} {wf.conditionOperator} {wf.conditionValue}</code></>
                          )}
                          {wf.description && <> • {wf.description}</>}
                        </div>
                      </div>

                      <div style={{ display: 'flex', gap: 6 }}>
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          onClick={() => {
                            setEditingWorkflow(wf);
                            setShowWorkflowModal(true);
                          }}
                        >
                          <EditRoundedIcon sx={{ fontSize: 13 }} /> Sửa bước
                        </button>
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          onClick={async () => {
                            await fetch(`/api/workflows/${wf.id}/toggle`, {
                              method: 'PATCH',
                              headers: { 'Content-Type': 'application/json' },
                              credentials: 'include',
                              body: JSON.stringify({ isActive: !wf.isActive }),
                            });
                            await loadWorkflows();
                          }}
                        >
                          {wf.isActive ? 'Tạm dừng' : 'Kích hoạt'}
                        </button>
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          style={{ color: '#d93025' }}
                          onClick={() => {
                            setConfirm({
                              open: true,
                              title: `Xóa quy trình ${wf.name}`,
                              message: 'Xác nhận xóa quy trình phê duyệt này?',
                              onConfirm: async () => {
                                setConfirm((c) => ({ ...c, open: false }));
                                await fetch(`/api/workflows/${wf.id}`, { method: 'DELETE', credentials: 'include' });
                                await loadWorkflows();
                                toast('Đã xóa quy trình!');
                              },
                            });
                          }}
                        >
                          <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} />
                        </button>
                      </div>
                    </div>

                    {/* Visual Steps Pipeline */}
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', padding: '10px 12px', borderRadius: 10, backgroundColor: 'var(--bg-surface)', border: '1px solid var(--border-light)' }}>
                      <span className="badge" style={{ backgroundColor: 'var(--bg-secondary)', color: 'var(--text-secondary)' }}>Trình duyệt</span>
                      {wf.steps.map((st) => (
                        <React.Fragment key={st.stepOrder}>
                          <ArrowForwardRoundedIcon sx={{ fontSize: 15, color: 'var(--text-muted)' }} />
                          <div style={{ padding: '5px 10px', borderRadius: 8, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--info-border)', fontSize: 12 }}>
                            <strong style={{ color: 'var(--accent-primary)' }}>Bước {st.stepOrder}: {st.name}</strong>
                            <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>
                              Duyệt: {[...st.approverRoles, ...st.approverUsers.map((u) => `@${u}`)].join(', ')} • SLA {st.slaHours}h
                            </div>
                          </div>
                        </React.Fragment>
                      ))}
                      <ArrowForwardRoundedIcon sx={{ fontSize: 15, color: 'var(--text-muted)' }} />
                      <span className="badge badge-success">Hoàn tất (APPROVED)</span>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}

        {/* TAB 4: AUTOMATIONS */}
        {activeTab === 'automations' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <div className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 12 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
                <div>
                  <strong style={{ fontSize: 15 }}>Quy tắc Tự động hóa Liên App &amp; Sự kiện Workflow</strong>
                  <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                    Tự động tạo/cập nhật dữ liệu đa App, tự cập nhật trường, gửi thông báo hoặc gọi Webhook khi dữ liệu thay đổi / phê duyệt
                  </div>
                </div>
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  onClick={() => {
                    setEditingAuto(null);
                    setShowAutoModal(true);
                  }}
                >
                  <AddRoundedIcon sx={{ fontSize: 16 }} /> Tạo Quy tắc Mới
                </button>
              </div>

              {automations.map((a) => {
                const srcTbl = allTables.find((t) => t.id === a.sourceTableId);
                const tgtTbl = allTables.find((t) => t.id === a.targetTableId);
                return (
                  <div
                    key={a.id}
                    style={{
                      padding: 14,
                      borderRadius: 12,
                      border: '1px solid var(--border-subtle)',
                      backgroundColor: 'var(--bg-primary)',
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      flexWrap: 'wrap',
                      gap: 12,
                    }}
                  >
                    <div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                        <strong style={{ fontSize: 14 }}>{a.name}</strong>
                        <StatusBadge status={a.isActive ? 'ACTIVE' : 'PENDING'} label={a.isActive ? 'Đang bật' : 'Tạm dừng'} size="sm" />
                        <span className="badge badge-info">Đã chạy {a.runCount} lần</span>
                      </div>
                      <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>
                        <span className="badge" style={{ backgroundColor: 'var(--bg-surface)', border: '1px solid var(--border-subtle)', marginRight: 6 }}>
                          {TRIGGER_LABELS[a.triggerEvent] || a.triggerEvent}
                        </span>
                        tại <strong>{srcTbl?.displayName || a.sourceTableId}</strong>
                        {a.conditionField && (
                          <> khi <code>{a.conditionField} {a.conditionOperator} {a.conditionValue || '(bất kỳ)'}</code></>
                        )}
                        {' → '}
                        <strong>{ACTION_LABELS[a.actionType] || a.actionType}</strong>
                        {tgtTbl && <> tại <strong>{tgtTbl.displayName}</strong></>}
                      </div>
                    </div>

                    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        onClick={async () => {
                          const res = await fetch(`/api/automations/${a.id}/test`, {
                            method: 'POST',
                            credentials: 'include',
                          }).then((r) => r.json());
                          if (res.success && res.data) {
                            setDryRunResult({ title: a.name, data: res.data });
                          }
                        }}
                      >
                        <PlayArrowRoundedIcon sx={{ fontSize: 14 }} /> Chạy thử
                      </button>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        onClick={() => {
                          setEditingAuto(a);
                          setShowAutoModal(true);
                        }}
                      >
                        <EditRoundedIcon sx={{ fontSize: 13 }} /> Sửa
                      </button>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        onClick={async () => {
                          await fetch(`/api/automations/${a.id}/toggle`, {
                            method: 'PATCH',
                            headers: { 'Content-Type': 'application/json' },
                            credentials: 'include',
                            body: JSON.stringify({ isActive: !a.isActive }),
                          });
                          await loadAutomations();
                        }}
                      >
                        {a.isActive ? 'Tạm dừng' : 'Kích hoạt'}
                      </button>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        style={{ color: '#d93025' }}
                        aria-label={`Xóa quy tắc ${a.name}`}
                        title={`Xóa quy tắc ${a.name}`}
                        onClick={() => {
                          setConfirm({
                            open: true,
                            title: `Xóa quy tắc ${a.name}`,
                            message: 'Xác nhận xóa quy tắc tự động hóa này?',
                            onConfirm: async () => {
                              setConfirm((c) => ({ ...c, open: false }));
                              await fetch(`/api/automations/${a.id}`, { method: 'DELETE', credentials: 'include' });
                              await loadAutomations();
                              toast('Đã xóa quy tắc!');
                            },
                          });
                        }}
                      >
                        <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} />
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>

            <div className="card" style={{ padding: 18 }}>
              <strong style={{ fontSize: 14.5, display: 'block', marginBottom: 10 }}>
                Lịch sử Thực thi Tự động ({autoLogs.length} gần nhất)
              </strong>
              {autoLogs.length === 0 ? (
                <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Chưa có lượt chạy tự động nào.</div>
              ) : (
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Quy tắc</th>
                      <th>Trạng thái</th>
                      <th>Chi tiết thực thi</th>
                      <th>Thời điểm</th>
                    </tr>
                  </thead>
                  <tbody>
                    {autoLogs.map((l) => (
                      <tr key={l.id}>
                        <td style={{ fontWeight: 600 }}>{l.automationName || l.automationId}</td>
                        <td>
                          <StatusBadge status={l.status === 'SUCCESS' ? 'ACTIVE' : 'ERROR'} label={l.status} size="sm" />
                        </td>
                        <td>{l.message}</td>
                        <td style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                          {new Date(l.createdAt).toLocaleString('vi-VN')}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>
        )}
      </div>

      {/* Modals */}
      <UserFormModal
        open={showUserModal}
        editing={editingUser}
        allApps={apps}
        allRoles={roles}
        onClose={() => setShowUserModal(false)}
        onSaved={async () => {
          setShowUserModal(false);
          await loadUsersAndRoles();
          await refreshAuth();
          toast(editingUser ? 'Đã cập nhật tài khoản!' : 'Đã tạo tài khoản mới!');
        }}
      />

      <RoleFormModal
        open={showRoleModal}
        editing={editingRole}
        catalog={catalog}
        allApps={apps}
        onClose={() => setShowRoleModal(false)}
        onSaved={async () => {
          setShowRoleModal(false);
          await loadUsersAndRoles();
          if (matrixAppId) await loadAppMatrix(matrixAppId);
          await refreshAuth();
          toast(editingRole ? 'Đã cập nhật vai trò & phân quyền!' : 'Đã tạo vai trò mới!');
        }}
      />

      <WorkflowFormModal
        open={showWorkflowModal}
        editing={editingWorkflow}
        allApps={apps}
        allTables={allTables}
        allRoles={roles}
        allUsers={users}
        onClose={() => setShowWorkflowModal(false)}
        onSaved={async () => {
          setShowWorkflowModal(false);
          await loadWorkflows();
          toast(editingWorkflow ? 'Đã cập nhật quy trình Workflow!' : 'Đã tạo quy trình Workflow mới!');
        }}
      />

      <AutomationModal
        open={showAutoModal}
        editing={editingAuto}
        allApps={apps}
        allTables={allTables}
        allModules={allModules}
        allRoles={roles}
        onClose={() => setShowAutoModal(false)}
        onSaved={async () => {
          setShowAutoModal(false);
          await loadAutomations();
          toast(editingAuto ? 'Đã cập nhật quy tắc tự động hóa!' : 'Đã tạo quy tắc tự động hóa!');
        }}
      />

      {resetPwUser && (
        <div className="modal-overlay" onClick={() => setResetPwUser(null)}>
          <div className="modal-dialog" style={{ maxWidth: 420 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16, fontWeight: 700, marginBottom: 6 }}>
              Đặt lại mật khẩu cho {resetPwUser.username}
            </h3>
            <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginBottom: 12 }}>
              Nhập mật khẩu mới cho tài khoản <strong>{resetPwUser.fullName}</strong>:
            </p>
            <form
              onSubmit={async (e) => {
                e.preventDefault();
                const res = await fetch(`/api/auth/users/${resetPwUser.id}/reset-password`, {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  credentials: 'include',
                  body: JSON.stringify({ newPassword: resetPwVal.trim() || '123456' }),
                }).then((r) => r.json());
                setResetPwUser(null);
                toast(res.message || 'Đã đặt lại mật khẩu!', res.success ? 'success' : 'error');
              }}
              style={{ display: 'flex', flexDirection: 'column', gap: 12 }}
            >
              <input
                className="form-input"
                value={resetPwVal}
                onChange={(e) => setResetPwVal(e.target.value)}
                minLength={6}
                required
                autoFocus
              />
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                <button type="button" className="btn btn-outline btn-sm" onClick={() => setResetPwUser(null)}>Hủy</button>
                <button type="submit" className="btn btn-primary btn-sm">Cập nhật mật khẩu</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {dryRunResult && (
        <div className="modal-overlay" onClick={() => setDryRunResult(null)}>
          <div className="modal-dialog" style={{ maxWidth: 520 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16, fontWeight: 700, marginBottom: 8 }}>Kết quả Chạy thử: {dryRunResult.title}</h3>
            <div style={{ marginBottom: 10 }}>
              <StatusBadge
                status={dryRunResult.data.matched ? 'ACTIVE' : 'PENDING'}
                label={dryRunResult.data.matched ? 'Khớp điều kiện' : 'Không khớp'}
              />
            </div>
            <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 10 }}>{dryRunResult.data.reason}</p>
            {dryRunResult.data.previewPayload && (
              <pre style={{ padding: 12, borderRadius: 10, background: '#f8fafc', border: '1px solid var(--border-light)', fontSize: 12, overflowX: 'auto' }}>
                {JSON.stringify(dryRunResult.data.previewPayload, null, 2)}
              </pre>
            )}
            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 12 }}>
              <button type="button" className="btn btn-primary btn-sm" onClick={() => setDryRunResult(null)}>Đóng</button>
            </div>
          </div>
        </div>
      )}

      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((c) => ({ ...c, open: false }))}
      />

      <NotificationSnackbar
        open={snack.open}
        message={snack.message}
        severity={snack.severity}
        onClose={() => setSnack((s) => ({ ...s, open: false }))}
      />
    </div>
  );
}
