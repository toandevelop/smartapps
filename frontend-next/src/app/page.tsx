'use client';

import React, { useState, useEffect } from 'react';
import Link from 'next/link';
import {
  PageHeader,
  StatCard,
  ClusterIcon,
  NotificationSnackbar,
} from './components/ui';
import { AppModal, AppFormState, ExcelToAppModal, TemplateInstallModal } from './studio/StudioModals';
import { usePlatform } from '@/lib/app-context';
import { useAuth } from '@/lib/auth-context';
import { useSettings } from '@/lib/settings-context';
import { isTauriDesktop, openAppDesktopWindow } from '@/lib/tauri-bridge';
import { AppWidget, PendingApprovalItem } from '@/lib/types';

import AppsRoundedIcon from '@mui/icons-material/AppsRounded';
import DesignServicesRoundedIcon from '@mui/icons-material/DesignServicesRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import LayersRoundedIcon from '@mui/icons-material/LayersRounded';
import TableViewRoundedIcon from '@mui/icons-material/TableViewRounded';
import StorageRoundedIcon from '@mui/icons-material/StorageRounded';
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded';
import CloudUploadRoundedIcon from '@mui/icons-material/CloudUploadRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import DesktopWindowsRoundedIcon from '@mui/icons-material/DesktopWindowsRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import { useEscapeKey } from '@/lib/ui-hooks';

export default function HomePortalPage() {
  const { apps, loadingApps, refreshApps } = usePlatform();
  const { canAccessApp, hasPermission, isAppAdmin } = useAuth();
  const { unreadSummary } = useSettings();
  const visibleApps = apps.filter((a) => canAccessApp(a.code));
  const [appSearch, setAppSearch] = useState('');
  const [deptFilter, setDeptFilter] = useState('ALL');
  const canStudio = hasPermission('studio.manage');
  const [showAppModal, setShowAppModal] = useState(false);
  const [showExcelModal, setShowExcelModal] = useState(false);
  const [showTemplateModal, setShowTemplateModal] = useState(false);
  const [inDesktop, setInDesktop] = useState(false);
  const [saving, setSaving] = useState(false);
  const [pendingApprovals, setPendingApprovals] = useState<PendingApprovalItem[]>([]);
  const [portalKpis, setPortalKpis] = useState<AppWidget[]>([]);
  const [rejectTarget, setRejectTarget] = useState<PendingApprovalItem | null>(null);
  const [rejectReason, setRejectReason] = useState('');
  useEscapeKey(Boolean(rejectTarget), () => setRejectTarget(null));
  const [snack, setSnack] = useState<{ open: boolean; message: string; severity: 'success' | 'error' }>({
    open: false,
    message: '',
    severity: 'success',
  });

  const loadExecutiveData = React.useCallback(async () => {
    try {
      const [pendRes, kpiRes] = await Promise.all([
        fetch('/api/records/pending-approvals', { credentials: 'include' }),
        fetch('/api/widgets/portal', { credentials: 'include' }),
      ]);
      if (pendRes.ok) {
        const j = await pendRes.json();
        if (j.success) setPendingApprovals(j.data || []);
      }
      if (kpiRes.ok) {
        const j = await kpiRes.json();
        if (j.success) setPortalKpis(j.data || []);
      }
    } catch {
      // ignore network error
    }
  }, []);

  useEffect(() => {
    setInDesktop(isTauriDesktop());
    loadExecutiveData();
    const onRt = () => loadExecutiveData();
    window.addEventListener('smartapps_realtime', onRt);
    return () => window.removeEventListener('smartapps_realtime', onRt);
  }, [loadExecutiveData]);

  const submitPortalDecision = async (item: PendingApprovalItem, approve: boolean, reason?: string) => {
    const res = await fetch(`/api/records/${item.recordId}/approve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ approve, reason }),
    }).then((r) => r.json());
    if (res.success) {
      setSnack({ open: true, message: approve ? 'Đã phê duyệt hồ sơ!' : 'Đã từ chối hồ sơ!', severity: 'success' });
      await loadExecutiveData();
    } else {
      setSnack({ open: true, message: res.message || 'Không đủ quyền duyệt', severity: 'error' });
    }
  };

  const handlePortalApprove = async (item: PendingApprovalItem, approve: boolean) => {
    if (!approve) {
      setRejectTarget(item);
      setRejectReason('');
      return;
    }
    await submitPortalDecision(item, true);
  };

  const totalModules = visibleApps.reduce((s, a) => s + a.moduleCount, 0);
  const totalTables = visibleApps.reduce((s, a) => s + a.tableCount, 0);
  const totalRecords = visibleApps.reduce((s, a) => s + a.recordCount, 0);

  const createApp = async (form: AppFormState) => {
    setSaving(true);
    try {
      const res = await fetch('/api/apps', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(form),
      }).then((r) => r.json());
      if (res.success) {
        await refreshApps();
        setShowAppModal(false);
        setSnack({ open: true, message: `Đã tạo App "${res.data.name}"! Hãy vào Studio để thêm bảng động.`, severity: 'success' });
      } else {
        setSnack({ open: true, message: res.message || 'Lỗi tạo App', severity: 'error' });
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <div>
      <PageHeader
        title="Cổng Ứng dụng Công ty (SmartApps Portal)"
        subtitle="Multi-App dữ liệu động liên kết chéo • Tự động hóa quy trình • Hỗ trợ Web & Rust Desktop App"
        icon={<AppsRoundedIcon sx={{ fontSize: 24 }} />}
        actions={
          canStudio ? (
            <>
              <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowTemplateModal(true)}>
                <AutoAwesomeRoundedIcon sx={{ fontSize: 16 }} />
                Cài App Mẫu
              </button>
              <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowExcelModal(true)}>
                <CloudUploadRoundedIcon sx={{ fontSize: 16 }} />
                Sinh App từ Excel
              </button>
              <Link href="/studio" className="btn btn-outline btn-sm">
                <DesignServicesRoundedIcon sx={{ fontSize: 16 }} />
                App Builder Studio
              </Link>
              <button type="button" className="btn btn-primary btn-sm" onClick={() => setShowAppModal(true)}>
                <AddRoundedIcon sx={{ fontSize: 17 }} />
                Tạo App Mới
              </button>
            </>
          ) : undefined
        }
      />

      <div className="page-content">
        <div className="stats-grid">
          <StatCard label="Ứng dụng được cấp" value={visibleApps.length} subtext="Liên kết dữ liệu chung toàn công ty" icon={<AppsRoundedIcon />} color="blue" />
          <StatCard label="Tổng Phân hệ" value={totalModules} subtext="Cấu hình động không cần sửa code" icon={<LayersRoundedIcon />} color="purple" />
          <StatCard label="Tổng Bảng động" value={totalTables} subtext="Bảng, Kanban, Lịch & Thống kê" icon={<TableViewRoundedIcon />} color="green" />
          <StatCard
            label="Tổng Bản ghi JSONB"
            value={totalRecords}
            subtext={inDesktop ? 'Đang chạy trên Rust Tauri Desktop' : 'C# .NET 9 + PostgreSQL 16'}
            icon={<StorageRoundedIcon />}
            color="yellow"
          />
        </div>

        {/* Executive Portal: Pending Approvals + Cross-App KPIs */}
        {(pendingApprovals.length > 0 || portalKpis.length > 0) && (
          <div style={{ display: 'grid', gridTemplateColumns: pendingApprovals.length > 0 && portalKpis.length > 0 ? '1.3fr 1fr' : '1fr', gap: 16 }}>
            {pendingApprovals.length > 0 && (
              <div className="card" style={{ padding: 18, borderLeft: '4px solid #ea8600' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
                  <div>
                    <strong style={{ fontSize: 14.5 }}>Việc cần tôi phê duyệt ({pendingApprovals.length})</strong>
                    <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                      Tổng hợp các phiếu đang chờ duyệt trên các Ứng dụng thuộc quyền của bạn
                    </div>
                  </div>
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8, maxHeight: 260, overflowY: 'auto' }}>
                  {pendingApprovals.map((item) => (
                    <div
                      key={item.recordId}
                      style={{
                        padding: '10px 12px',
                        borderRadius: 10,
                        backgroundColor: 'var(--bg-primary)',
                        border: '1px solid var(--border-light)',
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        gap: 10,
                      }}
                    >
                      <div style={{ minWidth: 0 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                          <span className="badge" style={{ backgroundColor: `${item.appColor}18`, color: item.appColor }}>
                            {item.appCode}
                          </span>
                          <strong style={{ fontSize: 13 }}>{item.recordKey || item.recordId}</strong>
                          <span style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>• {item.tableDisplayName}</span>
                          {item.currentStepName && (
                            <span className="badge badge-warning">
                              {item.totalSteps && item.totalSteps > 1
                                ? `Bước ${item.currentStepOrder || 1}/${item.totalSteps}: ${item.currentStepName}`
                                : item.currentStepName}
                            </span>
                          )}
                        </div>
                        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
                          {item.summaryText || `Người gửi: ${item.createdBy}`}
                        </div>
                      </div>
                      <div style={{ display: 'flex', gap: 6, flexShrink: 0 }}>
                        <button type="button" className="btn btn-outline btn-xs" style={{ color: '#1e8e3e' }} onClick={() => handlePortalApprove(item, true)}>
                          Duyệt
                        </button>
                        <button type="button" className="btn btn-outline btn-xs" style={{ color: '#d93025' }} onClick={() => handlePortalApprove(item, false)}>
                          Từ chối
                        </button>
                        <Link href={`/apps/${item.appCode}`} className="btn btn-outline btn-xs">
                          Xem
                        </Link>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {portalKpis.length > 0 && (
              <div className="card" style={{ padding: 18 }}>
                <div style={{ marginBottom: 12 }}>
                  <strong style={{ fontSize: 14.5 }}>Chỉ số Điều hành Đa Ứng dụng</strong>
                  <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                    KPI thời gian thực trích từ Dashboard của các App doanh nghiệp
                  </div>
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(175px, 1fr))', gap: 10 }}>
                  {portalKpis.map((w) => (
                    <Link
                      key={w.id}
                      href={w.appCode ? `/apps/${w.appCode}?tab=dashboard` : '/'}
                      style={{
                        padding: '10px 12px',
                        borderRadius: 12,
                        backgroundColor: 'var(--bg-primary)',
                        borderTop: `3px solid ${w.color}`,
                        textDecoration: 'none',
                        color: 'inherit',
                      }}
                    >
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: 11, color: 'var(--text-muted)' }}>
                        <span>{w.title}</span>
                        {w.appCode && <span className="badge badge-info">{w.appCode}</span>}
                      </div>
                      <div style={{ fontSize: 19, fontWeight: 800, marginTop: 4 }}>{w.formattedValue}</div>
                    </Link>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}

        <div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14, flexWrap: 'wrap', gap: 10 }}>
            <div>
              <h2 style={{ fontSize: 16.5, fontWeight: 700, color: 'var(--text-primary)' }}>
                Danh sách Ứng dụng Doanh nghiệp ({visibleApps.length})
              </h2>
              <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                Dùng chung User, Roles &amp; liên kết dữ liệu xuyên suốt toàn công ty
              </div>
            </div>

            <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <div style={{ position: 'relative', width: 220 }}>
                <SearchRoundedIcon sx={{ fontSize: 16, color: 'var(--text-muted)', position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', pointerEvents: 'none' }} />
                <input
                  id="portal-app-search"
                  name="appSearch"
                  aria-label="Tìm ứng dụng"
                  className="form-input"
                  style={{ height: 34, paddingLeft: 30, paddingRight: appSearch ? 28 : 10, fontSize: 12.5 }}
                  placeholder="Tìm ứng dụng..."
                  value={appSearch}
                  onChange={(e) => setAppSearch(e.target.value)}
                />
                {appSearch && (
                  <button
                    type="button"
                    aria-label="Xóa tìm kiếm ứng dụng"
                    onClick={() => setAppSearch('')}
                    style={{
                      position: 'absolute',
                      right: 6,
                      top: '50%',
                      transform: 'translateY(-50%)',
                      border: 'none',
                      background: 'transparent',
                      color: 'var(--text-muted)',
                      cursor: 'pointer',
                      display: 'inline-flex',
                      padding: 2,
                    }}
                  >
                    <CloseRoundedIcon sx={{ fontSize: 15 }} />
                  </button>
                )}
              </div>
              <select
                id="portal-dept-filter"
                name="deptFilter"
                aria-label="Lọc theo phòng ban"
                className="form-select"
                style={{ width: 190, height: 34, fontSize: 12.5 }}
                value={deptFilter}
                onChange={(e) => setDeptFilter(e.target.value)}
              >
                <option value="ALL">Mọi khối / phòng ban</option>
                {Array.from(new Set(visibleApps.map((a) => a.department).filter(Boolean))).map((d) => (
                  <option key={d} value={d}>{d}</option>
                ))}
              </select>
            </div>
          </div>

          {loadingApps ? (
            <div className="card" style={{ textAlign: 'center', padding: 48, color: 'var(--text-muted)' }}>
              Đang kết nối C# .NET 9 Core API & PostgreSQL...
            </div>
          ) : (
            <div className="app-launcher-grid">
              {visibleApps
                .filter((a) => {
                  const matchDept = deptFilter === 'ALL' || a.department === deptFilter;
                  const q = appSearch.trim().toLowerCase();
                  const matchSearch = !q || a.name.toLowerCase().includes(q) || a.code.toLowerCase().includes(q) || (a.department || '').toLowerCase().includes(q);
                  return matchDept && matchSearch;
                })
                .map((app) => {
                  const appAdminMode = isAppAdmin(app.code);
                  const isTasksApp = app.code.toUpperCase() === 'TASKS';
                  return (
                <div
                  key={app.id}
                  className="google-card"
                  style={{
                    borderTop: `4px solid ${app.color || '#1a73e8'}`,
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'space-between',
                    gap: 16,
                  }}
                >
                  <div>
                    <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 12, marginBottom: 12 }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                        <div
                          style={{
                            width: 46,
                            height: 46,
                            borderRadius: 14,
                            backgroundColor: app.bgColor || '#e8f0fe',
                            color: app.color || '#1a73e8',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                          }}
                        >
                          <ClusterIcon icon={app.icon} fontSize={24} />
                        </div>
                        <div>
                          <div style={{ fontSize: 15.5, fontWeight: 700, color: 'var(--text-primary)' }}>{app.name}</div>
                          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>{app.department}</div>
                        </div>
                      </div>
                      <span
                        className="badge"
                        style={{ backgroundColor: app.bgColor, color: app.color }}
                      >
                        {app.code}
                      </span>
                    </div>

                    <p style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5, minHeight: 40 }}>
                      {app.description || 'Ứng dụng dữ liệu động được thiết kế trên SmartApps Platform.'}
                    </p>
                  </div>

                  <div>
                    <div
                      style={{
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        flexWrap: 'wrap',
                        gap: 8,
                        fontSize: 12,
                        color: 'var(--text-muted)',
                        padding: '10px 12px',
                        borderRadius: 10,
                        backgroundColor: 'var(--bg-primary)',
                        marginBottom: 12,
                      }}
                    >
                      <div style={{ display: 'flex', gap: 8 }}>
                        <span><strong>{app.moduleCount}</strong> phân hệ</span>
                        <span>•</span>
                        <span><strong>{app.tableCount}</strong> bảng</span>
                        <span>•</span>
                        <span><strong>{app.recordCount}</strong> dòng</span>
                      </div>
                      <div style={{ display: 'flex', gap: 6 }}>
                        {isTasksApp && unreadSummary.pendingTaskCount > 0 && (
                          <span className="badge badge-warning" style={{ fontSize: 10.5 }}>
                            {unreadSummary.pendingTaskCount} việc chờ
                          </span>
                        )}
                        <span
                          className="badge"
                          style={{
                            fontSize: 10.5,
                            backgroundColor: appAdminMode ? '#e8f0fe' : '#e0f2f1',
                            color: appAdminMode ? '#1967d2' : '#00695c',
                          }}
                        >
                          {appAdminMode ? 'Quản trị App' : 'Cá nhân'}
                        </span>
                      </div>
                    </div>

                    <div style={{ display: 'flex', gap: 8 }}>
                      <Link
                        href={`/apps/${app.code}`}
                        className="btn btn-primary btn-sm"
                        style={{ flex: 1, backgroundColor: app.color || '#1a73e8' }}
                      >
                        Mở Ứng dụng
                        <ArrowForwardRoundedIcon sx={{ fontSize: 16 }} />
                      </Link>
                      {!isTasksApp && (
                        <Link
                          href={`/apps/${app.code}?tab=dashboard`}
                          className="btn btn-outline btn-sm"
                          title="Mở App Dashboard & Widget tùy chỉnh"
                        >
                          Dashboard
                        </Link>
                      )}
                      {inDesktop && (
                        <button
                          type="button"
                          className="btn btn-outline btn-sm"
                          title="Mở thành cửa sổ Desktop riêng (Rust Tauri)"
                          aria-label={`Mở ${app.name} trên cửa sổ Desktop riêng`}
                          onClick={() => openAppDesktopWindow(app.code, app.name)}
                        >
                          <DesktopWindowsRoundedIcon sx={{ fontSize: 16 }} />
                        </button>
                      )}
                      {canStudio && (
                        <Link href={`/studio?appId=${encodeURIComponent(app.id)}`} className="btn btn-outline btn-sm">
                          Thiết kế
                        </Link>
                      )}
                    </div>
                  </div>
                </div>
                  );
                })}
            </div>
          )}
        </div>
      </div>

      <AppModal
        open={showAppModal}
        editing={null}
        saving={saving}
        onClose={() => setShowAppModal(false)}
        onSave={createApp}
        initial={{
          id: '',
          code: '',
          name: '',
          description: '',
          department: 'Toàn công ty',
          icon: 'Handshake',
          color: '#1a73e8',
          bgColor: '#e8f0fe',
          version: '1.0.0',
          orderIndex: apps.length + 1,
        }}
      />

      <ExcelToAppModal
        open={showExcelModal}
        onClose={() => setShowExcelModal(false)}
        onCreated={async (app) => {
          setShowExcelModal(false);
          await refreshApps();
          setSnack({ open: true, message: `Đã tạo App "${app.name}" từ file Excel!`, severity: 'success' });
        }}
      />

      <TemplateInstallModal
        open={showTemplateModal}
        onClose={() => setShowTemplateModal(false)}
        onInstalled={async (app) => {
          setShowTemplateModal(false);
          await refreshApps();
          await loadExecutiveData();
          setSnack({ open: true, message: `Đã cài đặt App mẫu "${app.name}"!`, severity: 'success' });
        }}
        onInstalledMany={async (installedApps) => {
          setShowTemplateModal(false);
          await refreshApps();
          await loadExecutiveData();
          setSnack({
            open: true,
            message: `Đã cài đặt thành công ${installedApps.length} App mẫu doanh nghiệp!`,
            severity: 'success',
          });
        }}
      />

      {rejectTarget && (
        <div className="modal-overlay" onClick={() => setRejectTarget(null)}>
          <div className="modal-dialog" style={{ maxWidth: 440 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16, fontWeight: 700, marginBottom: 6 }}>Từ chối phê duyệt hồ sơ</h3>
            <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginBottom: 12 }}>
              Hồ sơ <strong>{rejectTarget.recordKey || rejectTarget.tableDisplayName}</strong> ({rejectTarget.appName}). Nhập lý do từ chối (tùy chọn):
            </p>
            <textarea
              className="form-input"
              rows={3}
              placeholder="Ví dụ: Cần bổ sung chứng từ hoặc xác nhận lại thông tin..."
              value={rejectReason}
              onChange={(e) => setRejectReason(e.target.value)}
              autoFocus
            />
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 14 }}>
              <button type="button" className="btn btn-outline btn-sm" onClick={() => setRejectTarget(null)}>
                Hủy
              </button>
              <button
                type="button"
                className="btn btn-sm"
                style={{ backgroundColor: '#d93025', color: '#ffffff', border: 'none' }}
                onClick={async () => {
                  const target = rejectTarget;
                  const reason = rejectReason.trim() || undefined;
                  setRejectTarget(null);
                  await submitPortalDecision(target, false, reason);
                }}
              >
                Xác nhận từ chối
              </button>
            </div>
          </div>
        </div>
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
