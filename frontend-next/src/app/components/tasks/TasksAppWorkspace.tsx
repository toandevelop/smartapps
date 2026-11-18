'use client';

import React, { useState, useEffect, useCallback, useMemo } from 'react';
import Link from 'next/link';
import AssignmentTurnedInRoundedIcon from '@mui/icons-material/AssignmentTurnedInRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import ViewKanbanRoundedIcon from '@mui/icons-material/ViewKanbanRounded';
import ViewListRoundedIcon from '@mui/icons-material/ViewListRounded';
import ForumRoundedIcon from '@mui/icons-material/ForumRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import CalendarTodayRoundedIcon from '@mui/icons-material/CalendarTodayRounded';
import ShieldRoundedIcon from '@mui/icons-material/ShieldRounded';
import PersonRoundedIcon from '@mui/icons-material/PersonRounded';
import AdminPanelSettingsRoundedIcon from '@mui/icons-material/AdminPanelSettingsRounded';
import DesignServicesRoundedIcon from '@mui/icons-material/DesignServicesRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import {
  PageHeader,
  StatCard,
  NotificationSnackbar,
  ClusterIcon,
  EmptyState,
  AppActivityDrawer,
} from '@/app/components/ui';
import AssignTaskModal, { AssignTaskPayload } from '@/app/components/chat/AssignTaskModal';
import { TASK_PRIORITY_META, TASK_STATUS_META } from '@/app/components/chat/TaskCardInChat';
import AppPermissionsModal from '@/app/studio/AppPermissionsModal';
import { useAuth } from '@/lib/auth-context';
import { usePlatform } from '@/lib/app-context';
import { useSettings } from '@/lib/settings-context';
import { AppTask, ChatChannel, ChatUserSummary, SysApp } from '@/lib/types';

const KANBAN_COLUMNS = [
  { key: 'TODO', label: 'Cần làm', color: '#5f6368' },
  { key: 'IN_PROGRESS', label: 'Đang thực hiện', color: '#1a73e8' },
  { key: 'REVIEW', label: 'Chờ kiểm tra / duyệt', color: '#ea8600' },
  { key: 'DONE', label: 'Hoàn thành', color: '#1e8e3e' },
];

const TASKS_APP_CODE = 'TASKS';
const TASKS_TABLE_ID = 'tbl_tasks_congviec';

export default function TasksAppWorkspace() {
  const {
    user,
    canAccessApp,
    isAppAdmin,
    hasAppPermission,
    hasPermission,
    canPerformTableAction,
    refreshAuth,
  } = useAuth();
  const { apps, refreshApps } = usePlatform();
  const { refreshUnreadSummary } = useSettings();

  const [appMeta, setAppMeta] = useState<SysApp | null>(null);
  const [tasks, setTasks] = useState<AppTask[]>([]);
  const [users, setUsers] = useState<ChatUserSummary[]>([]);
  const [channels, setChannels] = useState<ChatChannel[]>([]);
  const [loading, setLoading] = useState(true);

  const [scope, setScope] = useState<'all' | 'assigned_to_me' | 'assigned_by_me'>('all');
  const [assigneeFilter, setAssigneeFilter] = useState('ALL');
  const [priorityFilter, setPriorityFilter] = useState('ALL');
  const [search, setSearch] = useState('');
  const [viewMode, setViewMode] = useState<'kanban' | 'list'>('kanban');
  const [appViewMode, setAppViewMode] = useState<'admin' | 'personal'>('admin');

  const [modalOpen, setModalOpen] = useState(false);
  const [showPermModal, setShowPermModal] = useState(false);
  const [showActivityDrawer, setShowActivityDrawer] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [snack, setSnack] = useState<{ open: boolean; message: string; severity: 'success' | 'error' }>({
    open: false,
    message: '',
    severity: 'success',
  });

  const resolvedApp = useMemo(() => {
    if (appMeta) return appMeta;
    return apps.find((a) => a.code.toUpperCase() === TASKS_APP_CODE) || null;
  }, [appMeta, apps]);

  const userCanAccess = canAccessApp(TASKS_APP_CODE);
  const userIsAppAdmin = isAppAdmin(TASKS_APP_CODE);
  const canViewAllTasks = userIsAppAdmin || hasAppPermission(TASKS_APP_CODE, 'record.view_all');
  const canCreate = canPerformTableAction(TASKS_APP_CODE, TASKS_TABLE_ID, 'create');
  const canEdit = canPerformTableAction(TASKS_APP_CODE, TASKS_TABLE_ID, 'edit');
  const canDelete = canPerformTableAction(TASKS_APP_CODE, TASKS_TABLE_ID, 'delete');
  const canStudio = hasPermission('studio.manage');

  const effectivePersonalMode = !canViewAllTasks || appViewMode === 'personal';

  useEffect(() => {
    setAppViewMode(canViewAllTasks ? 'admin' : 'personal');
  }, [canViewAllTasks, user?.username]);

  const loadData = useCallback(async () => {
    if (!user || !userCanAccess) {
      setLoading(false);
      return;
    }
    setLoading(true);
    try {
      const qs = new URLSearchParams({ scope });
      if (effectivePersonalMode) qs.set('personalMode', 'true');
      if (priorityFilter !== 'ALL') qs.set('priority', priorityFilter);
      if (search.trim()) qs.set('search', search.trim());

      const [tRes, uRes, cRes, appRes] = await Promise.all([
        fetch(`/api/tasks?${qs.toString()}`, { credentials: 'include' }),
        fetch('/api/chat/users', { credentials: 'include' }),
        fetch('/api/chat/channels', { credentials: 'include' }),
        fetch(`/api/apps/${TASKS_APP_CODE}`, { credentials: 'include' }),
      ]);
      if (tRes.ok) {
        const j = await tRes.json();
        if (j.success) setTasks(j.data || []);
      }
      if (uRes.ok) {
        const j = await uRes.json();
        if (j.success) setUsers(j.data || []);
      }
      if (cRes.ok) {
        const j = await cRes.json();
        if (j.success) setChannels(j.data || []);
      }
      if (appRes.ok) {
        const j = await appRes.json();
        if (j.success && j.data?.app) setAppMeta(j.data.app);
      }
    } finally {
      setLoading(false);
    }
  }, [user, userCanAccess, scope, effectivePersonalMode, priorityFilter, search]);

  useEffect(() => {
    if (user) loadData();
  }, [user, loadData]);

  useEffect(() => {
    const onRt = (e: Event) => {
      const detail = (e as CustomEvent<any>).detail;
      if (detail?.type === 'task_updated') loadData();
    };
    window.addEventListener('smartapps_realtime', onRt);
    return () => window.removeEventListener('smartapps_realtime', onRt);
  }, [loadData]);

  const handleStatusChange = async (task: AppTask, nextStatus: string) => {
    if (!canEdit) {
      setSnack({ open: true, message: 'Bạn không có quyền cập nhật trạng thái công việc.', severity: 'error' });
      return;
    }
    const res = await fetch(`/api/tasks/${encodeURIComponent(task.id)}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({
        status: nextStatus,
        progress: nextStatus === 'DONE' ? 100 : nextStatus === 'IN_PROGRESS' ? Math.max(30, task.progress) : task.progress,
      }),
    }).then((r) => r.json());
    if (res.success && res.data) {
      setTasks((prev) => prev.map((t) => (t.id === task.id ? res.data : t)));
      refreshUnreadSummary();
    } else {
      setSnack({ open: true, message: res.message || 'Không thể cập nhật công việc', severity: 'error' });
    }
  };

  const handleDeleteTask = async (task: AppTask) => {
    const res = await fetch(`/api/tasks/${encodeURIComponent(task.id)}`, {
      method: 'DELETE',
      credentials: 'include',
    }).then((r) => r.json());
    if (res.success) {
      setTasks((prev) => prev.filter((t) => t.id !== task.id));
      refreshUnreadSummary();
      refreshApps();
      setSnack({ open: true, message: `Đã xóa công việc ${task.taskCode}`, severity: 'success' });
    } else {
      setSnack({ open: true, message: res.message || 'Không có quyền xóa', severity: 'error' });
    }
  };

  const handleCreateTask = async (payload: AssignTaskPayload) => {
    setSubmitting(true);
    try {
      const res = await fetch('/api/tasks', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(payload),
      }).then((r) => r.json());
      if (res.success) {
        setModalOpen(false);
        await loadData();
        refreshUnreadSummary();
        refreshApps();
        setSnack({
          open: true,
          message: `Đã giao việc ${res.data.taskCode} và đồng bộ hệ thống!`,
          severity: 'success',
        });
      } else {
        setSnack({ open: true, message: res.message || 'Lỗi tạo công việc', severity: 'error' });
      }
    } finally {
      setSubmitting(false);
    }
  };

  if (user && !userCanAccess) {
    return (
      <div className="page-content">
        <EmptyState
          title="Không có quyền truy cập App Giao việc & Tiến độ"
          description="Vai trò hiện tại của bạn chưa được cấp quyền truy cập App TASKS. Liên hệ Quản trị viên để được cấp quyền."
          action={
            <Link href="/" className="btn btn-primary">
              Về Cổng Ứng dụng
            </Link>
          }
        />
      </div>
    );
  }

  const displayedTasks =
    assigneeFilter === 'ALL' || effectivePersonalMode
      ? tasks
      : tasks.filter((t) => t.assigneeUsername.toLowerCase() === assigneeFilter.toLowerCase());

  const todoCount = displayedTasks.filter((t) => t.status === 'TODO').length;
  const inProgressCount = displayedTasks.filter((t) => t.status === 'IN_PROGRESS').length;
  const reviewCount = displayedTasks.filter((t) => t.status === 'REVIEW').length;
  const doneCount = displayedTasks.filter((t) => t.status === 'DONE').length;

  const appColor = resolvedApp?.color || '#ea8600';

  return (
    <div>
      <PageHeader
        title={resolvedApp?.name || 'Giao việc & Tiến độ'}
        subtitle={
          resolvedApp
            ? `${resolvedApp.department} • Phiên bản ${resolvedApp.version} • ${resolvedApp.description || ''}`
            : 'Theo dõi tiến độ công việc được giao trực tiếp từ Chat nội bộ và các Ứng dụng doanh nghiệp'
        }
        icon={<ClusterIcon icon={resolvedApp?.icon || 'FactCheck'} fontSize={24} />}
        accentColor={appColor}
        badge={<span className="badge badge-info">{resolvedApp?.code || TASKS_APP_CODE}</span>}
        breadcrumbs={[{ label: 'Cổng Ứng dụng', href: '/' }, { label: resolvedApp?.name || 'Giao việc & Tiến độ' }]}
        actions={
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <button
              type="button"
              className="btn btn-outline btn-sm btn-icon"
              onClick={() => setShowActivityDrawer(true)}
              title="Lịch sử hoạt động & chỉnh sửa (Giao việc & Tiến độ)"
              aria-label="Lịch sử hoạt động và chỉnh sửa"
            >
              <HistoryRoundedIcon sx={{ fontSize: 18, color: appColor }} />
            </button>
            {userIsAppAdmin && (
              <button
                type="button"
                className="btn btn-outline btn-sm"
                onClick={() => setShowPermModal(true)}
                title="Phân quyền vai trò & bảng trong App Giao việc"
              >
                <ShieldRoundedIcon sx={{ fontSize: 16, color: '#00897b' }} />
                Phân quyền App
              </button>
            )}
            {canStudio && (
              <Link href={resolvedApp ? `/studio?appId=${encodeURIComponent(resolvedApp.id)}` : '/studio'} className="btn btn-outline btn-sm">
                <DesignServicesRoundedIcon sx={{ fontSize: 16 }} />
                Cấu trúc App
              </Link>
            )}
            <Link href="/chat" className="btn btn-outline btn-sm">
              <ForumRoundedIcon sx={{ fontSize: 16 }} />
              Mở Chat Nội bộ
            </Link>
            {canCreate && (
              <button
                type="button"
                className="btn btn-primary btn-sm"
                style={{ backgroundColor: appColor }}
                onClick={() => setModalOpen(true)}
              >
                <AddRoundedIcon sx={{ fontSize: 17 }} />
                Giao việc mới
              </button>
            )}
          </div>
        }
      />

      <div className="page-content">
        {/* App Admin/Manager vs Personal Self-Service Mode Bar */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 10 }}>
          {canViewAllTasks ? (
            <div className="segmented-tabs subtle" style={{ padding: 3 }}>
              <button
                type="button"
                className={`segmented-tab ${appViewMode === 'admin' ? 'active' : ''}`}
                style={{
                  height: 26,
                  fontSize: 11.5,
                  ...(appViewMode === 'admin' ? { backgroundColor: '#1a73e8', color: '#ffffff' } : {}),
                }}
                onClick={() => setAppViewMode('admin')}
                title="Xem toàn bộ công việc và tiến độ giao việc toàn công ty"
              >
                <AdminPanelSettingsRoundedIcon sx={{ fontSize: 14 }} />
                {userIsAppAdmin ? 'Chế độ Quản trị' : 'Chế độ Quản lý (Toàn bộ)'}
              </button>
              <button
                type="button"
                className={`segmented-tab ${appViewMode === 'personal' ? 'active' : ''}`}
                style={{
                  height: 26,
                  fontSize: 11.5,
                  ...(appViewMode === 'personal' ? { backgroundColor: '#00897b', color: '#ffffff' } : {}),
                }}
                onClick={() => setAppViewMode('personal')}
                title="Chỉ xem công việc được giao cho tôi hoặc việc tôi đã giao"
              >
                <PersonRoundedIcon sx={{ fontSize: 14 }} />
                Chế độ Cá nhân
              </button>
            </div>
          ) : (
            <span
              className="badge"
              style={{ backgroundColor: '#e0f2f1', color: '#00695c', fontWeight: 600, padding: '6px 12px' }}
              title="Bạn đang dùng App ở chế độ Nhân viên (chỉ xem và cập nhật công việc của chính mình)"
            >
              <PersonRoundedIcon sx={{ fontSize: 14 }} />
              Chế độ Sử dụng (Cá nhân{user?.employeeCode ? ` • ${user.employeeCode}` : ''})
            </span>
          )}

          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
            {effectivePersonalMode
              ? 'Đang lọc theo phạm vi cá nhân (chỉ hiển thị công việc liên quan đến bạn)'
              : 'Đang hiển thị toàn bộ công việc của các phòng ban'}
          </div>
        </div>

        <div className="stats-grid">
          <StatCard label="Cần thực hiện" value={todoCount} subtext="Công việc mới tiếp nhận" icon={<AssignmentTurnedInRoundedIcon />} color="blue" />
          <StatCard label="Đang triển khai" value={inProgressCount} subtext="Đang xử lý & cập nhật" icon={<AssignmentTurnedInRoundedIcon />} color="purple" />
          <StatCard label="Chờ duyệt / Kiểm tra" value={reviewCount} subtext="Đã gửi người giao kiểm tra" icon={<AssignmentTurnedInRoundedIcon />} color="yellow" />
          <StatCard label="Đã hoàn thành" value={doneCount} subtext="Đồng bộ 100% với thẻ Chat" icon={<AssignmentTurnedInRoundedIcon />} color="green" />
        </div>

        {/* Filter & View Bar */}
        <div
          className="card"
          style={{
            padding: '12px 16px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 12,
            flexWrap: 'wrap',
          }}
        >
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {(
              [
                ['all', effectivePersonalMode ? 'Tất cả việc của tôi' : 'Tất cả công việc'],
                ['assigned_to_me', 'Việc được giao cho tôi'],
                ['assigned_by_me', 'Việc tôi đã giao'],
              ] as const
            ).map(([k, lbl]) => (
              <button
                key={k}
                type="button"
                className={`btn btn-xs ${scope === k ? 'btn-primary' : 'btn-outline'}`}
                onClick={() => setScope(k)}
              >
                {lbl}
              </button>
            ))}
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <input
              className="form-input"
              style={{ width: 210, height: 32, fontSize: 12.5 }}
              placeholder="Tìm mã TASK hoặc tiêu đề..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            {!effectivePersonalMode && users.length > 0 && (
              <select
                className="form-select"
                style={{ width: 170, height: 32, fontSize: 12.5 }}
                value={assigneeFilter}
                onChange={(e) => setAssigneeFilter(e.target.value)}
              >
                <option value="ALL">Mọi nhân sự</option>
                {users.map((u) => (
                  <option key={u.username} value={u.username}>
                    {u.fullName || u.username}
                  </option>
                ))}
              </select>
            )}
            <select
              className="form-select"
              style={{ width: 140, height: 32, fontSize: 12.5 }}
              value={priorityFilter}
              onChange={(e) => setPriorityFilter(e.target.value)}
            >
              <option value="ALL">Mọi ưu tiên</option>
              <option value="URGENT">Khẩn cấp</option>
              <option value="HIGH">Cao</option>
              <option value="MEDIUM">Trung bình</option>
              <option value="LOW">Thấp</option>
            </select>
            <div style={{ display: 'flex', gap: 4 }}>
              <button
                type="button"
                className={`btn btn-xs ${viewMode === 'kanban' ? 'btn-primary' : 'btn-outline'}`}
                onClick={() => setViewMode('kanban')}
              >
                <ViewKanbanRoundedIcon sx={{ fontSize: 15 }} />
                Kanban
              </button>
              <button
                type="button"
                className={`btn btn-xs ${viewMode === 'list' ? 'btn-primary' : 'btn-outline'}`}
                onClick={() => setViewMode('list')}
              >
                <ViewListRoundedIcon sx={{ fontSize: 15 }} />
                Bảng
              </button>
            </div>
          </div>
        </div>

        {loading ? (
          <div className="card" style={{ textAlign: 'center', padding: 40, color: 'var(--text-muted)' }}>
            Đang tải danh sách công việc...
          </div>
        ) : viewMode === 'kanban' ? (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 14 }}>
            {KANBAN_COLUMNS.map((col) => {
              const colTasks = displayedTasks.filter((t) => t.status === col.key);
              return (
                <div
                  key={col.key}
                  className="card"
                  style={{ padding: 14, borderTop: `4px solid ${col.color}`, display: 'flex', flexDirection: 'column', gap: 10 }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <strong style={{ fontSize: 13.5 }}>{col.label}</strong>
                    <span className="badge" style={{ backgroundColor: `${col.color}18`, color: col.color }}>
                      {colTasks.length}
                    </span>
                  </div>

                  {colTasks.length === 0 ? (
                    <div style={{ padding: 18, textAlign: 'center', fontSize: 12, color: 'var(--text-muted)' }}>
                      Không có công việc
                    </div>
                  ) : (
                    colTasks.map((t) => {
                      const pri = TASK_PRIORITY_META[t.priority] || TASK_PRIORITY_META.MEDIUM;
                      const allowDeleteRow =
                        canDelete && (!effectivePersonalMode || t.assignerUsername === user?.username);
                      return (
                        <div
                          key={t.id}
                          style={{
                            padding: 12,
                            borderRadius: 12,
                            border: '1px solid var(--border-light)',
                            backgroundColor: 'var(--bg-secondary)',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: 6,
                          }}
                        >
                          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6 }}>
                            <code style={{ fontSize: 11, fontWeight: 700, color: 'var(--accent-primary)' }}>{t.taskCode}</code>
                            <span className="badge" style={{ backgroundColor: pri.bg, color: pri.color, fontSize: 10, padding: '1px 7px' }}>
                              {pri.label}
                            </span>
                          </div>
                          <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-primary)' }}>{t.title}</div>
                          <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
                            Nhận: <strong>{t.assigneeFullName || `@${t.assigneeUsername}`}</strong>
                            {t.assignerUsername && (
                              <>
                                {' • '}Giao: <span>{t.assignerFullName || `@${t.assignerUsername}`}</span>
                              </>
                            )}
                          </div>
                          {t.dueDate && (
                            <div style={{ fontSize: 11, color: 'var(--text-muted)', display: 'flex', alignItems: 'center', gap: 4 }}>
                              <CalendarTodayRoundedIcon sx={{ fontSize: 12 }} />
                              Hạn: {new Date(t.dueDate).toLocaleDateString('vi-VN')}
                            </div>
                          )}
                          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                            <div style={{ flex: 1, height: 5, borderRadius: 999, backgroundColor: 'var(--border-subtle)', overflow: 'hidden' }}>
                              <div style={{ width: `${t.progress}%`, height: '100%', backgroundColor: col.color }} />
                            </div>
                            <span style={{ fontSize: 10.5, fontWeight: 700 }}>{t.progress}%</span>
                          </div>

                          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6, marginTop: 4 }}>
                            <select
                              className="form-select"
                              style={{ height: 26, fontSize: 11, padding: '0 6px', flex: 1 }}
                              disabled={!canEdit}
                              value={t.status}
                              onChange={(e) => handleStatusChange(t, e.target.value)}
                            >
                              <option value="TODO">Cần làm</option>
                              <option value="IN_PROGRESS">Đang làm</option>
                              <option value="REVIEW">Chờ duyệt</option>
                              <option value="DONE">Hoàn thành</option>
                            </select>
                            {t.channelId && (
                              <Link
                                href={`/chat?channel=${t.channelId}`}
                                className="btn btn-outline btn-xs"
                                title={`Mở trong kênh chat ${t.channelName || ''}`}
                              >
                                <ForumRoundedIcon sx={{ fontSize: 13 }} />
                              </Link>
                            )}
                            {allowDeleteRow && (
                              <button
                                type="button"
                                className="btn btn-outline btn-xs"
                                style={{ color: '#d93025' }}
                                aria-label={`Xóa công việc ${t.taskCode}`}
                                title={`Xóa công việc ${t.taskCode}`}
                                onClick={() => handleDeleteTask(t)}
                              >
                                <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} />
                              </button>
                            )}
                          </div>
                        </div>
                      );
                    })
                  )}
                </div>
              );
            })}
          </div>
        ) : (
          <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
            <div className="table-responsive">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Mã</th>
                    <th>Công việc</th>
                    <th>Ưu tiên</th>
                    <th>Người nhận</th>
                    <th>Người giao</th>
                    <th>Hạn</th>
                    <th>Tiến độ</th>
                    <th>Trạng thái</th>
                    <th>Tích hợp Chat</th>
                  </tr>
                </thead>
                <tbody>
                  {displayedTasks.length === 0 ? (
                    <tr>
                      <td colSpan={9} style={{ textAlign: 'center', padding: 28, color: 'var(--text-muted)' }}>
                        Chưa có công việc nào.
                      </td>
                    </tr>
                  ) : (
                    displayedTasks.map((t) => {
                      const pri = TASK_PRIORITY_META[t.priority] || TASK_PRIORITY_META.MEDIUM;
                      const st = TASK_STATUS_META[t.status] || TASK_STATUS_META.TODO;
                      return (
                        <tr key={t.id}>
                          <td><code>{t.taskCode}</code></td>
                          <td>
                            <div style={{ fontWeight: 700 }}>{t.title}</div>
                            {t.description && <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>{t.description}</div>}
                          </td>
                          <td>
                            <span className="badge" style={{ backgroundColor: pri.bg, color: pri.color }}>{pri.label}</span>
                          </td>
                          <td>{t.assigneeFullName || t.assigneeUsername}</td>
                          <td>{t.assignerFullName || t.assignerUsername}</td>
                          <td>{t.dueDate ? new Date(t.dueDate).toLocaleDateString('vi-VN') : '—'}</td>
                          <td><strong>{t.progress}%</strong></td>
                          <td>
                            <select
                              className="form-select"
                              style={{ height: 28, fontSize: 11.5, backgroundColor: st.bg, color: st.color }}
                              disabled={!canEdit}
                              value={t.status}
                              onChange={(e) => handleStatusChange(t, e.target.value)}
                            >
                              <option value="TODO">Cần làm</option>
                              <option value="IN_PROGRESS">Đang làm</option>
                              <option value="REVIEW">Chờ duyệt</option>
                              <option value="DONE">Hoàn thành</option>
                            </select>
                          </td>
                          <td>
                            <div style={{ display: 'flex', gap: 6 }}>
                              {t.channelId ? (
                                <Link href={`/chat?channel=${t.channelId}`} className="btn btn-outline btn-xs">
                                  <ForumRoundedIcon sx={{ fontSize: 13 }} />
                                  {t.channelName || 'Mở Chat'}
                                </Link>
                              ) : (
                                <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>—</span>
                              )}
                            </div>
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

      <AssignTaskModal
        open={modalOpen}
        users={users}
        channels={channels}
        submitting={submitting}
        onClose={() => setModalOpen(false)}
        onSubmit={handleCreateTask}
      />

      <AppActivityDrawer
        open={showActivityDrawer}
        appId={resolvedApp?.id || 'app_tasks'}
        appName={resolvedApp?.name || 'Giao việc & Tiến độ'}
        appColor={appColor}
        defaultTableId={TASKS_TABLE_ID}
        tables={[{ id: TASKS_TABLE_ID, displayName: 'Công việc & Giao việc' }]}
        onClose={() => setShowActivityDrawer(false)}
      />

      {resolvedApp && (
        <AppPermissionsModal
          open={showPermModal}
          app={resolvedApp}
          onClose={() => setShowPermModal(false)}
          onSaved={async () => {
            setShowPermModal(false);
            await refreshAuth();
            await refreshApps();
            await loadData();
            setSnack({ open: true, message: 'Đã cập nhật phân quyền App Giao việc & Tiến độ!', severity: 'success' });
          }}
        />
      )}

      <NotificationSnackbar
        open={snack.open}
        message={snack.message}
        severity={snack.severity}
        onClose={() => setSnack((s) => ({ ...s, open: false }))}
      />
    </div>
  );
}
