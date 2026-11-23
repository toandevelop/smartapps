'use client';

import React, { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { useParams, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import dynamic from 'next/dynamic';
import {
  PageHeader,
  ClusterIcon,
  DynamicDataTable,
  ConfirmDialog,
  NotificationSnackbar,
  EmptyState,
  KanbanBoardView,
  CalendarView,
  AnalyticsPanel,
  AppDashboardView,
  StructuredFilterBuilder,
  FloatingBulkActionBar,
  RuntimeSplitView,
  GalleryView,
  TimelineGanttView,
} from '@/app/components/ui';
import TasksAppWorkspace from '@/app/components/tasks/TasksAppWorkspace';
import { usePlatform } from '@/lib/app-context';
import { useAuth } from '@/lib/auth-context';
import { batchFetchCategories, getCategoryItemName, getCategoryItems } from '@/lib/category-service';
import { batchFetchLookups, getLookupOptions, resolveLookupLabel } from '@/lib/lookup-service';
import {
  openAppDesktopWindow,
  saveOfflineSnapshot,
  loadOfflineSnapshot,
  queueOfflineMutation,
} from '@/lib/tauri-bridge';
import { useIsTauriDesktop } from '@/lib/ui-hooks';
import type { ChildRelationMutation } from '@/lib/types';
import DeleteSweepRoundedIcon from '@mui/icons-material/DeleteSweepRounded';
import RestoreFromTrashRoundedIcon from '@mui/icons-material/RestoreFromTrashRounded';
import BookmarkAddRoundedIcon from '@mui/icons-material/BookmarkAddRounded';
import FilterListRoundedIcon from '@mui/icons-material/FilterListRounded';
import KeyboardArrowDownRoundedIcon from '@mui/icons-material/KeyboardArrowDownRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import { AppRecord, AppRuntimeTree, StructuredQueryFilter, SysColumn, SysModule, SysTable, SysSavedView } from '@/lib/types';
import {
  buildEffectiveFiltersPayload,
  describeCondition,
  EMPTY_STRUCTURED_FILTER,
  normalizeSavedViewFilters,
} from '@/lib/query-filter';

import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DesignServicesRoundedIcon from '@mui/icons-material/DesignServicesRounded';
import CloudDownloadRoundedIcon from '@mui/icons-material/CloudDownloadRounded';
import CloudUploadRoundedIcon from '@mui/icons-material/CloudUploadRounded';
import FactCheckRoundedIcon from '@mui/icons-material/FactCheckRounded';
import TableViewRoundedIcon from '@mui/icons-material/TableViewRounded';
import VerticalSplitRoundedIcon from '@mui/icons-material/VerticalSplitRounded';
import PhotoLibraryRoundedIcon from '@mui/icons-material/PhotoLibraryRounded';
import TimelineRoundedIcon from '@mui/icons-material/TimelineRounded';
import ViewKanbanRoundedIcon from '@mui/icons-material/ViewKanbanRounded';
import CalendarMonthRoundedIcon from '@mui/icons-material/CalendarMonthRounded';
import BarChartRoundedIcon from '@mui/icons-material/BarChartRounded';
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded';
import SaveAltRoundedIcon from '@mui/icons-material/SaveAltRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import DashboardRoundedIcon from '@mui/icons-material/DashboardRounded';
import ShieldRoundedIcon from '@mui/icons-material/ShieldRounded';
import PersonRoundedIcon from '@mui/icons-material/PersonRounded';
import AdminPanelSettingsRoundedIcon from '@mui/icons-material/AdminPanelSettingsRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import PrintRoundedIcon from '@mui/icons-material/PrintRounded';
import DynamicFieldInput from '@/app/components/ui/DynamicFieldInput';
import { useEscapeKey } from '@/lib/ui-hooks';

const ExcelImportDialog = dynamic(() => import('@/app/components/import/ExcelImportDialog'), { ssr: false });
const Record360Drawer = dynamic(() => import('@/app/components/ui/Record360Drawer'), { ssr: false });
const AppActivityDrawer = dynamic(() => import('@/app/components/ui/AppActivityDrawer'), { ssr: false });
const PrintableRecordModal = dynamic(() => import('@/app/components/ui/PrintableRecordModal'), { ssr: false });
const RecordEditorModal = dynamic(
  () => import('@/app/studio/StudioModals').then((m) => m.RecordEditorModal),
  { ssr: false }
);
const AppPermissionsModal = dynamic(() => import('@/app/studio/AppPermissionsModal'), { ssr: false });

export default function DynamicAppRuntimePage() {
  const params = useParams();
  const appCode = String(params?.appCode || '');
  if (appCode.trim().toUpperCase() === 'TASKS') {
    return <TasksAppWorkspace />;
  }
  return <GenericDynamicAppView appCode={appCode} />;
}

function GenericDynamicAppView({ appCode }: { appCode: string }) {
  const searchParams = useSearchParams();
  const { refreshApps } = usePlatform();
  const {
    canAccessApp,
    hasPermission,
    hasAppPermission,
    isAppAdmin,
    canPerformTableAction,
    user,
    refreshAuth,
  } = useAuth();

  const [appSection, setAppSection] = useState<'dashboard' | 'data'>(() =>
    searchParams?.get('tab') === 'dashboard' ? 'dashboard' : 'data'
  );
  const [appViewModes, setAppViewModes] = useState<Record<string, 'admin' | 'personal'>>({});
  const [showAppPermModal, setShowAppPermModal] = useState(false);
  const [showActivityDrawer, setShowActivityDrawer] = useState(false);
  const [tree, setTree] = useState<AppRuntimeTree | null>(null);
  const [loadingTree, setLoadingTree] = useState(true);
  const [activeModId, setActiveModId] = useState<string>('');
  const [activeTableId, setActiveTableId] = useState<string>('');
  const [viewMode, setViewMode] = useState<'table' | 'split' | 'gallery' | 'timeline' | 'kanban' | 'calendar' | 'analytics'>('table');

  const [records, setRecords] = useState<AppRecord[]>([]);
  const [total, setTotal] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('ALL');
  const [loadingRecords, setLoadingRecords] = useState(false);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);

  const [showRecordModal, setShowRecordModal] = useState(false);
  const [editingRecord, setEditingRecord] = useState<AppRecord | null>(null);
  const [drawerRecord, setDrawerRecord] = useState<AppRecord | null>(null);
  const [printRecord, setPrintRecord] = useState<AppRecord | null>(null);
  const [saving, setSaving] = useState(false);
  const [validating, setValidating] = useState(false);
  const [exporting, setExporting] = useState(false);
  const inDesktop = useIsTauriDesktop();
  const [showTrash, setShowTrash] = useState(false);
  const [trashRecords, setTrashRecords] = useState<AppRecord[]>([]);
  const [showImportModal, setShowImportModal] = useState(false);
  const [showUtilityMenu, setShowUtilityMenu] = useState(false);
  const [showExportMenu, setShowExportMenu] = useState(false);
  const [showAdvancedFilter, setShowAdvancedFilter] = useState(false);
  const [showSaveViewModal, setShowSaveViewModal] = useState(false);
  const [saveViewName, setSaveViewName] = useState('');
  const [showBulkEdit, setShowBulkEdit] = useState(false);
  const [bulkField, setBulkField] = useState('');
  const [bulkValue, setBulkValue] = useState<string>('');
  const [bulkSaving, setBulkSaving] = useState(false);
  const [showPrintList, setShowPrintList] = useState(false);
  const [rejectTarget, setRejectTarget] = useState<AppRecord | null>(null);
  const [rejectReason, setRejectReason] = useState('');
  useEscapeKey(showTrash, () => setShowTrash(false));
  useEscapeKey(showExportMenu, () => setShowExportMenu(false));
  useEscapeKey(showSaveViewModal, () => setShowSaveViewModal(false));
  useEscapeKey(showBulkEdit, () => !bulkSaving && setShowBulkEdit(false));
  useEscapeKey(showPrintList, () => setShowPrintList(false));
  useEscapeKey(Boolean(rejectTarget), () => setRejectTarget(null));
  const [refFilterVersion, setRefFilterVersion] = useState(0);
  const [savedViews, setSavedViews] = useState<SysSavedView[]>([]);
  const [columnFilters, setColumnFilters] = useState<Record<string, string>>({});
  const [structuredFilter, setStructuredFilter] = useState<StructuredQueryFilter>(EMPTY_STRUCTURED_FILTER);
  const [filterBuilderKey, setFilterBuilderKey] = useState(0);
  const [draftOverride, setDraftOverride] = useState<boolean | null>(null);
  const previewDraft =
    draftOverride ??
    (searchParams?.get('previewDraft') === 'true' || searchParams?.get('previewDraft') === '1');
  const togglePreviewDraft = (next?: boolean) =>
    setDraftOverride((prev) => (next !== undefined ? next : !(prev ?? previewDraft)));
  const [conflictState, setConflictState] = useState<{
    recordId: string;
    recordKey?: string | null;
    field: string;
    fieldLabel: string;
    attemptedValue: unknown;
    serverMessage: string;
  } | null>(null);
  const [resolvingConflict, setResolvingConflict] = useState(false);
  const [sortField, setSortField] = useState('');
  const [sortDir, setSortDir] = useState<'ASC' | 'DESC'>('DESC');
  const [filterCol, setFilterCol] = useState('');
  const [filterVal, setFilterVal] = useState('');

  const utilityRef = useRef<HTMLDivElement>(null);
  const exportMenuRef = useRef<HTMLDivElement>(null);

  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });
  const [snack, setSnack] = useState<{
    open: boolean;
    message: string;
    severity: 'success' | 'error' | 'info';
    actionLabel?: string;
    onAction?: () => void;
  }>({
    open: false,
    message: '',
    severity: 'info',
  });
  const toast = (
    message: string,
    severity: 'success' | 'error' | 'info' = 'success',
    options?: { actionLabel?: string; onAction?: () => void }
  ) => setSnack({ open: true, message, severity, actionLabel: options?.actionLabel, onAction: options?.onAction });

  useEffect(() => {
    const onDocDown = (e: MouseEvent) => {
      if (utilityRef.current && !utilityRef.current.contains(e.target as Node)) {
        setShowUtilityMenu(false);
      }
      if (exportMenuRef.current && !exportMenuRef.current.contains(e.target as Node)) {
        setShowExportMenu(false);
      }
    };
    document.addEventListener('mousedown', onDocDown);
    return () => document.removeEventListener('mousedown', onDocDown);
  }, []);

  const loadAppTree = useCallback(async () => {
    if (!appCode) return;
    setLoadingTree(true);
    try {
      const qs = previewDraft ? '?previewDraft=true' : '';
      const res = await fetch(`/api/apps/${encodeURIComponent(appCode)}${qs}`).then((r) => r.json());
      if (res.success && res.data) {
        const data: AppRuntimeTree = res.data;
        setTree(data);
        const targetTblParam = searchParams?.get('table');
        const targetModNode = targetTblParam
          ? data.modules.find((m) => m.tables.some((t) => t.table.id === targetTblParam))
          : undefined;
        const firstMod = targetModNode?.module || data.modules[0]?.module;
        const firstTbl = targetModNode
          ? targetModNode.tables.find((t) => t.table.id === targetTblParam)?.table
          : data.modules[0]?.tables[0]?.table;
        setActiveModId((prev) => {
          if (targetModNode) return targetModNode.module.id;
          return prev && data.modules.some((m) => m.module.id === prev) ? prev : firstMod?.id || '';
        });
        setActiveTableId((prev) => {
          if (targetTblParam && firstTbl) return firstTbl.id;
          const allTables = data.modules.flatMap((m) => m.tables.map((t) => t.table));
          return prev && allTables.some((t) => t.id === prev) ? prev : firstTbl?.id || '';
        });
      } else {
        setTree(null);
      }
    } catch {
      const snap = await loadOfflineSnapshot<{ tree: AppRuntimeTree; records: AppRecord[] }>(appCode, user?.username);
      if (snap?.tree) {
        setTree(snap.tree);
        if (Array.isArray(snap.records)) setRecords(snap.records);
        const firstMod = snap.tree.modules[0]?.module;
        const firstTbl = snap.tree.modules[0]?.tables[0]?.table;
        if (firstMod) setActiveModId((prev) => prev || firstMod.id);
        if (firstTbl) setActiveTableId((prev) => prev || firstTbl.id);
      } else {
        setTree(null);
      }
    } finally {
      setLoadingTree(false);
    }
  }, [appCode, previewDraft, searchParams, user?.username]);

  useEffect(() => {
    let cancelled = false;
    Promise.resolve().then(() => {
      if (!cancelled) void loadAppTree();
    });
    return () => {
      cancelled = true;
    };
  }, [loadAppTree]);

  const currentModNode = tree?.modules.find((m) => m.module.id === activeModId) || tree?.modules[0];
  const currentTableNode =
    currentModNode?.tables.find((t) => t.table.id === activeTableId) || currentModNode?.tables[0];

  const activeModule: SysModule | undefined = currentModNode?.module;
  const activeTable: SysTable | undefined = currentTableNode?.table;
  const activeColumns: SysColumn[] = useMemo(() => currentTableNode?.columns || [], [currentTableNode]);

  useEffect(() => {
    if (activeColumns.length === 0) return;
    const catCodes = activeColumns.map((c) => c.refCategoryCode).filter(Boolean) as string[];
    Promise.all([
      catCodes.length > 0 ? batchFetchCategories(catCodes) : Promise.resolve(),
      batchFetchLookups(activeColumns),
    ]).then(() => setRefFilterVersion((v) => v + 1));
  }, [activeColumns]);

  const kanbanColumn = useMemo(
    () =>
      activeColumns.find(
        (c) => Boolean(c.refCategoryCode) || ['STATUS', 'PRIORITY', 'SELECT'].includes(c.dataType.toUpperCase())
      ),
    [activeColumns]
  );
  const kanbanGroupableColumns = useMemo(
    () =>
      activeColumns.filter(
        (c) => Boolean(c.refCategoryCode) || ['STATUS', 'PRIORITY', 'SELECT'].includes(c.dataType.toUpperCase())
      ),
    [activeColumns],
  );
  const canKanban = kanbanGroupableColumns.length > 0 || Boolean(activeTable?.approvalEnabled);
  const effectiveFilters = useMemo(
    () => buildEffectiveFiltersPayload(columnFilters, structuredFilter),
    [columnFilters, structuredFilter],
  );
  const calendarColumn = useMemo(
    () => activeColumns.find((c) => ['DATE', 'DATETIME'].includes(c.dataType.toUpperCase())),
    [activeColumns]
  );

  const resolvedAppCode = tree?.app?.code || appCode;
  const userIsAppAdmin = isAppAdmin(resolvedAppCode);
  const viewKey = `${user?.username || ''}:${resolvedAppCode}`;
  const appViewMode: 'admin' | 'personal' = !userIsAppAdmin ? 'personal' : (appViewModes[viewKey] ?? 'admin');
  const setAppViewMode = (mode: 'admin' | 'personal') =>
    setAppViewModes((prev) => ({ ...prev, [viewKey]: mode }));
  const effectivePersonalMode = !userIsAppAdmin || appViewMode === 'personal';

  const loadRecords = useCallback(async () => {
    if (!tree?.app || !activeTable) {
      setRecords([]);
      setTotal(0);
      return;
    }
    setLoadingRecords(true);
    try {
      const qs = new URLSearchParams({
        appId: tree.app.id,
        tableId: activeTable.id,
        page: String(page),
        limit: String(pageSize),
        status: statusFilter,
      });
      if (effectivePersonalMode) qs.set('personalMode', 'true');
      if (search.trim()) qs.set('search', search.trim());
      if (effectiveFilters) qs.set('filters', JSON.stringify(effectiveFilters));
      if (sortField) {
        qs.set('sortField', sortField);
        qs.set('sortDir', sortDir);
      }
      const res = await fetch(`/api/records?${qs.toString()}`).then((r) => r.json());
      if (res.success && res.data) {
        setRecords(res.data.items || []);
        setTotal(res.data.total || 0);
        setTotalPages(res.data.totalPages || 1);
      }
    } finally {
      setLoadingRecords(false);
    }
  }, [tree, activeTable, page, pageSize, search, statusFilter, effectiveFilters, sortField, sortDir, effectivePersonalMode]);

  const loadSavedViews = useCallback(async () => {
    if (!activeTable) {
      setSavedViews([]);
      return;
    }
    const res = await fetch(`/api/records/views?tableId=${encodeURIComponent(activeTable.id)}`, {
      credentials: 'include',
    }).then((r) => r.json());
    if (res.success) setSavedViews(res.data || []);
  }, [activeTable]);

  useEffect(() => {
    let cancelled = false;
    Promise.resolve().then(() => {
      if (!cancelled) void loadSavedViews();
    });
    return () => {
      cancelled = true;
    };
  }, [loadSavedViews]);

  useEffect(() => {
    let cancelled = false;
    Promise.resolve().then(() => {
      if (!cancelled) void loadRecords();
    });
    return () => {
      cancelled = true;
    };
  }, [loadRecords]);

  useEffect(() => {
    const onRt = (e: Event) => {
      const detail = (e as CustomEvent<{ type?: string; tableId?: string; appCode?: string }>).detail;
      if (!detail || detail.type !== 'record_changed') return;
      if (!detail.tableId || detail.tableId === activeTable?.id || detail.appCode?.toUpperCase() === appCode.toUpperCase()) {
        loadRecords();
      }
    };
    window.addEventListener('smartapps_realtime', onRt);
    return () => window.removeEventListener('smartapps_realtime', onRt);
  }, [activeTable?.id, appCode, loadRecords]);

  const employeeCode = user?.employeeCode;
  const personalFormDefaults = useMemo(() => {
    const defaults: Record<string, unknown> = {};
    const locked: string[] = [];
    if (!effectivePersonalMode || !employeeCode) {
      return { defaults, locked };
    }
    for (const c of activeColumns) {
      if (
        c.refTableId === 'tbl_hrm_nhanvien' ||
        ['ho_ten_nv', 'nhan_vien', 'nguoi_tao', 'nguoi_nhan'].includes(c.columnCode)
      ) {
        defaults[c.columnCode] = employeeCode;
        locked.push(c.columnCode);
      }
    }
    return { defaults, locked };
  }, [effectivePersonalMode, employeeCode, activeColumns]);

  if (!canAccessApp(appCode)) {
    return (
      <div className="page-content">
        <EmptyState
          title="Không có quyền truy cập ứng dụng này"
          description={`Tài khoản "${user?.fullName || user?.username}" (${user?.roleCode}) chưa được phân quyền sử dụng App ${appCode}.`}
          icon={<LockRoundedIcon sx={{ fontSize: 28, color: '#d93025' }} />}
          action={
            <Link href="/" className="btn btn-primary">
              Quay về Cổng Ứng dụng
            </Link>
          }
        />
      </div>
    );
  }

  const canCreate = canPerformTableAction(resolvedAppCode, activeTable?.id, 'create');
  const canEdit = canPerformTableAction(resolvedAppCode, activeTable?.id, 'edit');
  const canDelete = canPerformTableAction(resolvedAppCode, activeTable?.id, 'delete');
  const canExport = canPerformTableAction(resolvedAppCode, activeTable?.id, 'export');
  const canApproveInApp = canPerformTableAction(resolvedAppCode, activeTable?.id, 'approve');
  const canManageWidgets = hasAppPermission(resolvedAppCode, 'widget.manage');
  const canStudio = hasPermission('studio.manage');

  const saveRecord = async (data: Record<string, unknown>, childMutations: ChildRelationMutation[] = []) => {
    if (!tree?.app || !activeModule || !activeTable) return;
    setSaving(true);
    const hasChildMutations = childMutations.length > 0;
    const endpoint = hasChildMutations ? '/api/records/with-relations' : '/api/records';
    const payload = hasChildMutations
      ? {
          id: editingRecord?.id,
          appId: tree.app.id,
          moduleId: activeModule.id,
          tableId: activeTable.id,
          data,
          expectedVersion: editingRecord?.version,
          childMutations,
        }
      : {
          id: editingRecord?.id,
          appId: tree.app.id,
          moduleId: activeModule.id,
          tableId: activeTable.id,
          data,
          expectedVersion: editingRecord?.version,
        };
    try {
      const res = await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      }).then((r) => r.json());
      if (res.success) {
        setShowRecordModal(false);
        await loadRecords();
        await refreshApps();
        const autoList: string[] = res.triggeredAutomations || [];
        toast(autoList.length > 0 ? `Đã lưu & tự động chạy: ${autoList.join(', ')}` : 'Đã lưu bản ghi thành công!');
      } else {
        toast(res.message || res.error || 'Lỗi lưu bản ghi', 'error');
      }
    } catch {
      await queueOfflineMutation({
        id: `off_${Date.now()}`,
        appCode: tree.app.code,
        tableId: activeTable.id,
        method: 'POST',
        endpoint,
        payload,
        createdAt: new Date().toISOString(),
      });
      setShowRecordModal(false);
      toast('Mất kết nối máy chủ: đã lưu bản ghi vào Hàng đợi Offline để đồng bộ sau!', 'info');
    } finally {
      setSaving(false);
    }
  };

  const loadTrash = async () => {
    if (!tree?.app || !activeTable) return;
    const res = await fetch(
      `/api/records/trash?appId=${encodeURIComponent(tree.app.id)}&tableId=${encodeURIComponent(activeTable.id)}`,
      { credentials: 'include' }
    ).then((r) => r.json());
    if (res.success) setTrashRecords(res.data || []);
  };

  const handleSubmitApproval = async (rec: AppRecord) => {
    const res = await fetch(`/api/records/${rec.id}/submit-approval`, {
      method: 'POST',
      credentials: 'include',
    }).then((r) => r.json());
    if (res.success) {
      await loadRecords();
      toast('Đã gửi yêu cầu phê duyệt!');
    } else {
      toast(res.message || 'Không thể trình duyệt', 'error');
    }
  };

  const submitApprovalDecision = async (rec: AppRecord, approve: boolean, reason?: string) => {
    const res = await fetch(`/api/records/${rec.id}/approve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({
        approve,
        reason,
        expectedVersion: rec.version,
        expectedStepOrder: rec.currentStepOrder,
      }),
    }).then((r) => r.json());
    if (res.success) {
      await loadRecords();
      toast(approve ? 'Đã phê duyệt bản ghi!' : 'Đã từ chối bản ghi!');
    } else {
      toast(res.message || 'Không đủ quyền phê duyệt', 'error');
    }
  };

  const handleRecallApproval = async (rec: AppRecord) => {
    const res = await fetch(`/api/records/${rec.id}/recall-approval`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ expectedVersion: rec.version }),
    }).then((r) => r.json());
    if (res.success) {
      await loadRecords();
      toast('Đã thu hồi trình duyệt!');
    } else {
      toast(res.message || 'Không thể thu hồi hồ sơ', 'error');
    }
  };

  const handleCancelApproval = async (rec: AppRecord) => {
    const res = await fetch(`/api/records/${rec.id}/cancel-approval`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ expectedVersion: rec.version }),
    }).then((r) => r.json());
    if (res.success) {
      await loadRecords();
      toast('Đã hủy hồ sơ!');
    } else {
      toast(res.message || 'Không thể hủy hồ sơ', 'error');
    }
  };

  const handleApproveRecord = async (rec: AppRecord, approve: boolean) => {
    if (!approve) {
      setRejectTarget(rec);
      setRejectReason('');
      return;
    }
    await submitApprovalDecision(rec, true);
  };

  const handleValidateTable = async () => {
    if (!activeTable) return;
    setValidating(true);
    try {
      const res = await fetch('/api/records/validate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tableId: activeTable.id }),
      }).then((r) => r.json());
      if (res.success && res.data) {
        await loadRecords();
        toast(
          `Đã kiểm tra ${res.data.total} dòng (${res.data.validCount} hợp lệ, ${res.data.errorCount} lỗi)`,
          res.data.errorCount > 0 ? 'info' : 'success'
        );
      }
    } finally {
      setValidating(false);
    }
  };

  const handleExportExcel = async (ignoreFilters = false) => {
    if (!activeTable || exporting) return;
    const table = activeTable;
    const qs = new URLSearchParams({ tableId: table.id, status: ignoreFilters ? 'ALL' : statusFilter });
    if (effectivePersonalMode) qs.set('personalMode', 'true');
    if (!ignoreFilters) {
      if (search.trim()) qs.set('search', search.trim());
      if (effectiveFilters) qs.set('filters', JSON.stringify(effectiveFilters));
      if (sortField) { qs.set('sortField', sortField); qs.set('sortDir', sortDir); }
    }
    setExporting(true);
    toast(ignoreFilters ? 'Đang xuất toàn bộ dữ liệu bảng ra Excel...' : 'Đang xuất toàn bộ kết quả theo bộ lọc hiện tại...', 'info');
    try {
      const response = await fetch(`/api/records/export?${qs}`, { credentials: 'include' });
      if (!response.ok) {
        const error = await response.json().catch(() => null);
        throw new Error(error?.message || 'Không thể xuất dữ liệu Excel.');
      }
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${table.tableName}${ignoreFilters ? '_all' : ''}.xlsx`;
      document.body.appendChild(link);
      link.click();
      link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
      toast(ignoreFilters ? 'Đã xuất toàn bộ bảng ra Excel.' : 'Đã xuất toàn bộ kết quả lọc ra Excel.');
    } catch (error) {
      toast(error instanceof Error ? error.message : 'Lỗi kết nối khi xuất Excel.', 'error');
    } finally {
      setExporting(false);
    }
  };

  /** Patches a single cell with optimistic locking (version) and surfaces a 409 Conflict modal if stale. */
  const patchCellWithConflict = async (
    recordId: string,
    field: string,
    nextVal: unknown,
    expectedVersionOverride?: number,
  ): Promise<boolean> => {
    const rec = records.find((r) => r.id === recordId);
    const expectedVersion = expectedVersionOverride !== undefined ? expectedVersionOverride : rec?.version;
    const response = await fetch(`/api/records/${recordId}/cell`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ field, value: nextVal, expectedVersion }),
    });
    const res = await response.json().catch(() => null);
    if (response.status === 409) {
      const colObj = activeColumns.find((c) => c.columnCode === field);
      setConflictState({
        recordId,
        recordKey: rec?.recordKey || null,
        field,
        fieldLabel: colObj?.columnName || field,
        attemptedValue: nextVal,
        serverMessage: res?.message || res?.error || 'Bản ghi vừa được cập nhật bởi người dùng khác.',
      });
      await loadRecords();
      return false;
    }
    if (res?.success) {
      await loadRecords();
      const autoList: string[] = res.triggeredAutomations || [];
      toast(autoList.length > 0 ? `Đã lưu & chạy tự động: ${autoList.join(', ')}` : 'Đã lưu thay đổi');
      return true;
    }
    toast(res?.message || res?.error || 'Không thể lưu thay đổi', 'error');
    return false;
  };

  const handleDownloadExcelTemplate = async () => {
    if (!activeTable) return;
    try {
      const XLSX = await import('xlsx');
      const templateCols = activeColumns.filter((c) => c.dataType !== 'AUTO_NUMBER' && !c.formula);
      const headers = templateCols.map((c) => c.excelHeader || c.columnName || c.columnCode);
      const ws = XLSX.utils.aoa_to_sheet([headers]);
      const wb = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(wb, ws, activeTable.displayName.slice(0, 31) || 'MauNhap');
      XLSX.writeFile(wb, `${activeTable.tableName}_template.xlsx`);
      toast('Đã tải xuống file mẫu nhập Excel.');
    } catch {
      toast('Không thể tạo file mẫu Excel.', 'error');
    }
  };

  if (loadingTree) {
    return (
      <div className="page-content" aria-busy="true" aria-label="Đang nạp cấu trúc ứng dụng">
        <div className="card" style={{ padding: 20, marginBottom: 16, display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 16 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
            <div className="skeleton-box" style={{ width: 44, height: 44, borderRadius: 12 }} />
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              <div className="skeleton-box" style={{ height: 18, width: 220 }} />
              <div className="skeleton-box" style={{ height: 12, width: 320 }} />
            </div>
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <div className="skeleton-box" style={{ height: 34, width: 110 }} />
            <div className="skeleton-box" style={{ height: 34, width: 120 }} />
          </div>
        </div>
        <div className="app-workspace-grid">
          <div className="card" style={{ padding: 14, display: 'flex', flexDirection: 'column', gap: 10 }}>
            <div className="skeleton-box" style={{ height: 14, width: '60%', marginBottom: 6 }} />
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={`rail-sk-${i}`} className="skeleton-box" style={{ height: 34, width: '100%' }} />
            ))}
          </div>
          <div className="card" style={{ padding: 16, display: 'flex', flexDirection: 'column', gap: 12 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <div className="skeleton-box" style={{ height: 20, width: 200 }} />
              <div style={{ display: 'flex', gap: 8 }}>
                <div className="skeleton-box" style={{ height: 32, width: 110 }} />
                <div className="skeleton-box" style={{ height: 32, width: 130 }} />
              </div>
            </div>
            {Array.from({ length: 6 }).map((_, r) => (
              <div key={`tbl-sk-${r}`} className="skeleton-box" style={{ height: 38, width: '100%' }} />
            ))}
          </div>
        </div>
      </div>
    );
  }

  if (!tree) {
    return (
      <div className="page-content">
        <EmptyState
          title={`Không tìm thấy ứng dụng "${appCode}"`}
          description="Ứng dụng này chưa được cấu hình hoặc đã bị xóa."
          action={
            <Link href="/" className="btn btn-primary">
              Về Cổng Ứng dụng
            </Link>
          }
        />
      </div>
    );
  }

  const validRows = records.filter((r) => r.status !== 'ERROR').length;
  const errorRows = records.filter((r) => r.status === 'ERROR').length;
  const activeFilterCount = Object.keys(columnFilters).length + structuredFilter.conditions.length;

  return (
    <div>
      <PageHeader
        title={tree.app.name}
        subtitle={`${tree.app.department} • Phiên bản ${tree.app.version} • ${tree.app.description || ''}`}
        icon={<ClusterIcon icon={tree.app.icon} fontSize={24} />}
        accentColor={tree.app.color}
        badge={
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <span className="badge badge-info">{tree.app.code}</span>
            <span className={`badge ${Number((tree.app.version || '1').split('.')[0]) >= 2 ? 'badge-success' : 'badge-warning'}`}>
              v{tree.app.version || '1.0.0'}
            </span>
          </span>
        }
        breadcrumbs={[{ label: 'Cổng Ứng dụng', href: '/' }, { label: tree.app.name }]}
        actions={
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <button
              type="button"
              className="btn btn-outline btn-sm btn-icon"
              onClick={() => setShowActivityDrawer(true)}
              title={`Lịch sử hoạt động & chỉnh sửa (${tree.app.name})`}
              aria-label="Lịch sử hoạt động và chỉnh sửa"
            >
              <HistoryRoundedIcon sx={{ fontSize: 18, color: tree.app.color }} />
            </button>
            {userIsAppAdmin && (
              <button
                type="button"
                className="btn btn-outline btn-sm"
                onClick={() => setShowAppPermModal(true)}
                title={`Phân quyền vai trò & bảng trong App ${tree.app.code}`}
              >
                <ShieldRoundedIcon sx={{ fontSize: 16, color: '#00897b' }} />
                Phân quyền App
              </button>
            )}
            {canStudio && (
              <>
                <button
                  type="button"
                  className={`btn btn-sm ${previewDraft ? 'btn-primary' : 'btn-outline'}`}
                  style={previewDraft ? { backgroundColor: '#b06000', borderColor: '#b06000', color: '#ffffff' } : undefined}
                  onClick={() => togglePreviewDraft()}
                  title={previewDraft ? 'Quay lại xem Bản Phát hành chính thức' : 'Xem thử Bản Nháp đang thiết kế trong Studio (chưa Publish)'}
                >
                  {previewDraft ? 'Đang xem: Bản Nháp' : 'Xem Bản Nháp'}
                </button>
                <Link href={`/studio?appId=${encodeURIComponent(tree.app.id)}`} className="btn btn-outline btn-sm">
                  <DesignServicesRoundedIcon sx={{ fontSize: 16 }} />
                  Cấu trúc App
                </Link>
              </>
            )}

            {(activeTable || inDesktop) && (
              <div ref={utilityRef} style={{ position: 'relative' }}>
                <button
                  type="button"
                  className="btn btn-outline btn-sm"
                  onClick={() => setShowUtilityMenu((v) => !v)}
                  aria-expanded={showUtilityMenu}
                >
                  <TuneRoundedIcon sx={{ fontSize: 16 }} />
                  <span>Tiện ích</span>
                  <KeyboardArrowDownRoundedIcon sx={{ fontSize: 16 }} />
                </button>

                {showUtilityMenu && (
                  <div
                    style={{
                      position: 'absolute',
                      right: 0,
                      top: 'calc(100% + 6px)',
                      minWidth: 256,
                      backgroundColor: 'var(--bg-surface)',
                      border: '1px solid var(--border-subtle)',
                      borderRadius: 12,
                      boxShadow: 'var(--shadow-lg)',
                      padding: 6,
                      zIndex: 120,
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 2,
                    }}
                  >
                    {activeTable && canCreate && (
                      <button
                        type="button"
                        className="utility-menu-item"
                        onClick={() => {
                          setShowUtilityMenu(false);
                          setShowImportModal(true);
                        }}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          gap: 10,
                          width: '100%',
                          textAlign: 'left',
                          padding: '9px 12px',
                          border: 'none',
                          borderRadius: 8,
                          backgroundColor: 'transparent',
                          cursor: 'pointer',
                          fontSize: 13,
                          fontWeight: 500,
                          color: 'var(--text-primary)',
                        }}
                      >
                        <CloudUploadRoundedIcon sx={{ fontSize: 18, color: '#1a73e8' }} />
                        <div>
                          <div>Import dữ liệu Excel</div>
                          <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>Tải lên .xlsx, .csv &amp; cập nhật trùng mã</div>
                        </div>
                      </button>
                    )}

                    {activeTable && canExport && (
                      <button
                        type="button"
                        className="utility-menu-item"
                        disabled={exporting}
                        aria-busy={exporting}
                        onClick={() => {
                          setShowUtilityMenu(false);
                          handleExportExcel();
                        }}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          gap: 10,
                          width: '100%',
                          textAlign: 'left',
                          padding: '9px 12px',
                          border: 'none',
                          borderRadius: 8,
                          backgroundColor: 'transparent',
                          cursor: 'pointer',
                          fontSize: 13,
                          fontWeight: 500,
                          color: 'var(--text-primary)',
                        }}
                      >
                        <CloudDownloadRoundedIcon sx={{ fontSize: 18, color: '#1e8e3e' }} />
                        <span>{exporting ? 'Đang xuất Excel...' : 'Xuất toàn bộ kết quả lọc (.xlsx)'}</span>
                      </button>
                    )}

                    {activeTable && canEdit && (
                      <button
                        type="button"
                        className="utility-menu-item"
                        disabled={validating}
                        onClick={() => {
                          setShowUtilityMenu(false);
                          handleValidateTable();
                        }}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          gap: 10,
                          width: '100%',
                          textAlign: 'left',
                          padding: '9px 12px',
                          border: 'none',
                          borderRadius: 8,
                          backgroundColor: 'transparent',
                          cursor: 'pointer',
                          fontSize: 13,
                          fontWeight: 500,
                          color: 'var(--text-primary)',
                        }}
                      >
                        <FactCheckRoundedIcon sx={{ fontSize: 18, color: '#7b1fa2' }} />
                        <span>{validating ? 'Đang kiểm tra...' : 'Kiểm tra & Tính công thức'}</span>
                      </button>
                    )}

                    {activeTable && canCreate && (
                      <button
                        type="button"
                        className="utility-menu-item"
                        onClick={async () => {
                          setShowUtilityMenu(false);
                          await loadTrash();
                          setShowTrash(true);
                        }}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          gap: 10,
                          width: '100%',
                          textAlign: 'left',
                          padding: '9px 12px',
                          border: 'none',
                          borderRadius: 8,
                          backgroundColor: 'transparent',
                          cursor: 'pointer',
                          fontSize: 13,
                          fontWeight: 500,
                          color: 'var(--text-primary)',
                        }}
                      >
                        <DeleteSweepRoundedIcon sx={{ fontSize: 18, color: '#5f6368' }} />
                        <span>Thùng rác khôi phục</span>
                      </button>
                    )}

                    {activeTable && canCreate && activeTable.publicFormEnabled && (
                      <Link
                        href={`/forms/${activeTable.id}`}
                        target="_blank"
                        onClick={() => setShowUtilityMenu(false)}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          gap: 10,
                          width: '100%',
                          textDecoration: 'none',
                          padding: '9px 12px',
                          borderRadius: 8,
                          fontSize: 13,
                          fontWeight: 500,
                          color: '#1a73e8',
                        }}
                      >
                        <OpenInNewRoundedIcon sx={{ fontSize: 18 }} />
                        <span>Mở Biểu mẫu công khai</span>
                      </Link>
                    )}

                    {inDesktop && (
                      <div style={{ borderTop: '1px solid var(--border-light)', marginTop: 4, paddingTop: 4 }}>
                        <button
                          type="button"
                          onClick={() => {
                            setShowUtilityMenu(false);
                            openAppDesktopWindow(tree.app.code, tree.app.name);
                          }}
                          style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: 10,
                            width: '100%',
                            textAlign: 'left',
                            padding: '8px 12px',
                            border: 'none',
                            borderRadius: 8,
                            backgroundColor: 'transparent',
                            cursor: 'pointer',
                            fontSize: 13,
                            color: 'var(--text-primary)',
                          }}
                        >
                          <OpenInNewRoundedIcon sx={{ fontSize: 17 }} />
                          <span>Mở cửa sổ Desktop riêng</span>
                        </button>
                        <button
                          type="button"
                          onClick={async () => {
                            setShowUtilityMenu(false);
                            const p = await saveOfflineSnapshot(tree.app.code, { tree, records }, user?.username);
                            if (p) toast(`Đã lưu offline snapshot (${p})`);
                          }}
                          style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: 10,
                            width: '100%',
                            textAlign: 'left',
                            padding: '8px 12px',
                            border: 'none',
                            borderRadius: 8,
                            backgroundColor: 'transparent',
                            cursor: 'pointer',
                            fontSize: 13,
                            color: 'var(--text-primary)',
                          }}
                        >
                          <SaveAltRoundedIcon sx={{ fontSize: 17 }} />
                          <span>Lưu dữ liệu Offline</span>
                        </button>
                      </div>
                    )}
                  </div>
                )}
              </div>
            )}


            {activeTable && canCreate && (
              <button
                type="button"
                className="btn btn-primary btn-sm"
                style={{ backgroundColor: tree.app.color }}
                onClick={() => {
                  setEditingRecord(null);
                  setShowRecordModal(true);
                }}
              >
                <AddRoundedIcon sx={{ fontSize: 17 }} />
                {String(tree.app.settings?.primaryActionLabel || '') || `Thêm ${activeTable.displayName}`}
              </button>
            )}
          </div>
        }
      />

      <div className="page-content">
        {previewDraft && (
          <div
            style={{
              padding: '10px 14px',
              borderRadius: 10,
              backgroundColor: 'var(--warning-bg)',
              border: '1px solid var(--warning-border)',
              color: 'var(--warning-text)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              flexWrap: 'wrap',
              gap: 10,
              fontSize: 12.5,
            }}
          >
            <div>
              <strong>Chế độ xem thử Bản Nháp (Draft Preview):</strong> Bạn đang xem trực tiếp cấu trúc chưa phát hành trong Studio
              {tree.app.publishStatus ? ` (Trạng thái hiện tại: ${tree.app.publishStatus})` : ''}. Người dùng thường vẫn chạy Bản Phát hành ổn định.
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <Link href={`/studio?appId=${encodeURIComponent(tree.app.id)}`} className="btn btn-outline btn-xs">
                Mở Studio để Phát hành
              </Link>
              <button type="button" className="btn btn-primary btn-xs" onClick={() => togglePreviewDraft(false)}>
                Về Bản Phát hành
              </button>
            </div>
          </div>
        )}
        {/* App Top Mode Bar + Compact Status Strip */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 10 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <div className="segmented-tabs">
              <button
                type="button"
                className={`segmented-tab ${appSection === 'dashboard' ? 'active' : ''}`}
                style={appSection === 'dashboard' ? { backgroundColor: tree.app.color, color: '#ffffff' } : undefined}
                onClick={() => setAppSection('dashboard')}
              >
                <DashboardRoundedIcon sx={{ fontSize: 16 }} />
                App Dashboard (Widget)
              </button>
              <button
                type="button"
                className={`segmented-tab ${appSection === 'data' ? 'active' : ''}`}
                style={appSection === 'data' ? { backgroundColor: tree.app.color, color: '#ffffff' } : undefined}
                onClick={() => setAppSection('data')}
              >
                <TableViewRoundedIcon sx={{ fontSize: 16 }} />
                Làm việc với Dữ liệu
              </button>
            </div>

            {userIsAppAdmin ? (
              <div className="segmented-tabs subtle" style={{ padding: 3 }}>
                <button
                  type="button"
                  className={`segmented-tab ${appViewMode === 'admin' ? 'active' : ''}`}
                  style={{
                    height: 26,
                    fontSize: 11.5,
                    ...(appViewMode === 'admin' ? { backgroundColor: 'var(--accent-primary)', color: 'var(--accent-on-primary)' } : {}),
                  }}
                  onClick={() => {
                    setAppViewMode('admin');
                    setPage(1);
                  }}
                  title="Xem toàn bộ dữ liệu và quản trị App"
                >
                  <AdminPanelSettingsRoundedIcon sx={{ fontSize: 14 }} />
                  Chế độ Quản trị
                </button>
                <button
                  type="button"
                  className={`segmented-tab ${appViewMode === 'personal' ? 'active' : ''}`}
                  style={{
                    height: 26,
                    fontSize: 11.5,
                    ...(appViewMode === 'personal' ? { backgroundColor: 'var(--success)', color: '#ffffff' } : {}),
                  }}
                  onClick={() => {
                    setAppViewMode('personal');
                    setPage(1);
                  }}
                  title="Xem ở chế độ nhân viên cá nhân (chỉ dữ liệu của bạn)"
                >
                  <PersonRoundedIcon sx={{ fontSize: 14 }} />
                  Chế độ Cá nhân
                </button>
              </div>
            ) : (
              <span
                className="badge"
                style={{ backgroundColor: '#e0f2f1', color: '#00695c', fontWeight: 600, padding: '6px 12px' }}
                title="Bạn đang dùng App ở chế độ Nhân viên (chỉ xem và thao tác dữ liệu của chính mình)"
              >
                <PersonRoundedIcon sx={{ fontSize: 14 }} />
                Chế độ Sử dụng (Cá nhân{user?.employeeCode ? ` • ${user.employeeCode}` : ''})
              </span>
            )}
          </div>

          {appSection === 'data' && activeTable && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <span className="badge" style={{ backgroundColor: 'var(--bg-surface)', border: '1px solid var(--border-subtle)', color: 'var(--text-secondary)' }}>
                Tổng: <strong>{total}</strong> bản ghi
              </span>
              <span className="badge badge-success">
                Hợp lệ: <strong>{validRows}</strong>
              </span>
              {errorRows > 0 ? (
                <button
                  type="button"
                  className="badge badge-danger"
                  style={{ cursor: 'pointer', border: '1px solid var(--danger-border)' }}
                  onClick={() => {
                    setStatusFilter('ERROR');
                    setPage(1);
                  }}
                  title="Nhấn để lọc các dòng cần rà soát"
                >
                  Cần rà soát: <strong>{errorRows}</strong>
                </button>
              ) : (
                <span className="badge" style={{ backgroundColor: 'var(--bg-secondary)', color: 'var(--text-muted)' }}>
                  0 lỗi kiểm tra
                </span>
              )}
            </div>
          )}
        </div>

        {appSection === 'dashboard' ? (
          <AppDashboardView
            appTree={tree}
            canCustomize={userIsAppAdmin && !effectivePersonalMode && (canStudio || canManageWidgets || canEdit)}
            personalMode={effectivePersonalMode}
            onJumpToTable={(targetTableId, searchHint) => {
              const ownerMod = tree.modules.find((m) => m.tables.some((t) => t.table.id === targetTableId));
              if (ownerMod) setActiveModId(ownerMod.module.id);
              setActiveTableId(targetTableId);
              if (searchHint !== undefined) {
                setSearch(searchHint);
                setPage(1);
              }
              setAppSection('data');
              setViewMode('table');
            }}
          />
        ) : (
        <div className="app-workspace-grid">
        {/* Enterprise Left Navigation Rail: Modules & Tables */}
        <aside className="card app-workspace-rail">
          <div>
            <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-muted)', marginBottom: 8, letterSpacing: 0.5 }}>
              PHÂN HỆ ({tree.modules.length})
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
              {tree.modules.map((m) => {
                const active = m.module.id === activeModule?.id;
                return (
                  <button
                    key={m.module.id}
                    type="button"
                    className={`btn btn-sm ${active ? 'btn-primary' : 'btn-outline'}`}
                    style={{
                      justifyContent: 'flex-start',
                      width: '100%',
                      ...(active ? { backgroundColor: tree.app.color, borderColor: tree.app.color } : {}),
                    }}
                    onClick={() => {
                      setActiveModId(m.module.id);
                      const firstTbl = m.tables[0]?.table.id || '';
                      setActiveTableId(firstTbl);
                      setPage(1);
                      setSelectedIds([]);
                      setViewMode('table');
                    }}
                  >
                    <ClusterIcon icon={m.module.icon} fontSize={16} />
                    <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{m.module.name}</span>
                  </button>
                );
              })}
            </div>
          </div>

          {currentModNode && currentModNode.tables.length > 0 && (
            <div style={{ paddingTop: 12, borderTop: '1px solid var(--border-light)' }}>
              <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-muted)', marginBottom: 8, letterSpacing: 0.5 }}>
                BẢNG DỮ LIỆU ({currentModNode.tables.length})
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                {currentModNode.tables.map((t) => {
                  const active = t.table.id === activeTable?.id;
                  return (
                    <button
                      key={t.table.id}
                      type="button"
                      className={`btn btn-xs ${active ? 'btn-secondary' : 'btn-outline'}`}
                      style={{
                        justifyContent: 'space-between',
                        width: '100%',
                        padding: '6px 10px',
                        height: 'auto',
                        minHeight: 32,
                        ...(active
                          ? { backgroundColor: tree.app.bgColor, color: tree.app.color, borderColor: tree.app.color, borderLeftWidth: 3 }
                          : {}),
                      }}
                      onClick={() => {
                        setActiveTableId(t.table.id);
                        setPage(1);
                        setSelectedIds([]);
                      }}
                    >
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        <TableViewRoundedIcon sx={{ fontSize: 14, flexShrink: 0 }} />
                        {t.table.displayName}
                      </span>
                      <span style={{ fontSize: 10.5, opacity: 0.75, flexShrink: 0 }}>({t.columns.length} cột)</span>
                    </button>
                  );
                })}
              </div>
            </div>
          )}
        </aside>

        {/* Right Main Content Workspace */}
        <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 12 }}>
        {/* View Mode + Collapsible Filter & Saved Views */}
        <div className="card" style={{ padding: '10px 16px', display: 'flex', flexDirection: 'column', gap: 10 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 8 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <strong style={{ fontSize: 14, color: 'var(--text-primary)' }}>{activeTable?.displayName || 'Chọn bảng dữ liệu'}</strong>
              {activeTable?.approvalEnabled && (
                <span className="badge badge-info" style={{ fontSize: 10.5 }}>Có quy trình duyệt</span>
              )}
            </div>

            {activeTable && (
              <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
                <button
                  type="button"
                  className={`btn btn-xs ${viewMode === 'table' ? 'btn-primary' : 'btn-outline'}`}
                  onClick={() => setViewMode('table')}
                >
                  <TableViewRoundedIcon sx={{ fontSize: 14 }} /> Bảng động
                </button>
                <button
                  type="button"
                  className={`btn btn-xs ${viewMode === 'split' ? 'btn-primary' : 'btn-outline'}`}
                  onClick={() => setViewMode('split')}
                  title="Chế độ Chia đôi: danh sách trái + Hồ sơ 360° phải (dùng phím ↑ / ↓)"
                >
                  <VerticalSplitRoundedIcon sx={{ fontSize: 14 }} /> Chia đôi
                </button>
                <button
                  type="button"
                  className={`btn btn-xs ${viewMode === 'gallery' ? 'btn-primary' : 'btn-outline'}`}
                  onClick={() => setViewMode('gallery')}
                  title="Hiển thị dạng lưới thẻ Thư viện trực quan"
                >
                  <PhotoLibraryRoundedIcon sx={{ fontSize: 14 }} /> Thư viện
                </button>
                {calendarColumn && (
                  <button
                    type="button"
                    className={`btn btn-xs ${viewMode === 'timeline' ? 'btn-primary' : 'btn-outline'}`}
                    onClick={() => setViewMode('timeline')}
                    title="Biểu đồ thanh tiến độ theo mốc ngày (Timeline / Gantt)"
                  >
                    <TimelineRoundedIcon sx={{ fontSize: 14 }} /> Tiến độ
                  </button>
                )}
                {canKanban && (
                  <button
                    type="button"
                    className={`btn btn-xs ${viewMode === 'kanban' ? 'btn-primary' : 'btn-outline'}`}
                    onClick={() => setViewMode('kanban')}
                  >
                    <ViewKanbanRoundedIcon sx={{ fontSize: 14 }} /> Kanban
                  </button>
                )}
                {calendarColumn && (
                  <button
                    type="button"
                    className={`btn btn-xs ${viewMode === 'calendar' ? 'btn-primary' : 'btn-outline'}`}
                    onClick={() => setViewMode('calendar')}
                  >
                    <CalendarMonthRoundedIcon sx={{ fontSize: 14 }} /> Lịch
                  </button>
                )}
                <button
                  type="button"
                  className={`btn btn-xs ${viewMode === 'analytics' ? 'btn-primary' : 'btn-outline'}`}
                  onClick={() => setViewMode('analytics')}
                >
                  <BarChartRoundedIcon sx={{ fontSize: 14 }} /> Thống kê
                </button>
                {viewMode !== 'analytics' && (
                  <button
                    type="button"
                    className={`btn btn-xs ${showAdvancedFilter || activeFilterCount > 0 ? 'btn-secondary' : 'btn-outline'}`}
                    onClick={() => setShowAdvancedFilter((v) => !v)}
                    title="Mở bộ lọc nhanh, truy vấn nâng cao AND/OR và chế độ xem đã lưu"
                  >
                    <FilterListRoundedIcon sx={{ fontSize: 14 }} />
                    Lọc &amp; Xem{activeFilterCount > 0 ? ` (${activeFilterCount})` : ''}
                  </button>
                )}
              </div>
            )}
          </div>

          {activeTable && viewMode !== 'analytics' && (showAdvancedFilter || activeFilterCount > 0 || savedViews.length > 0) && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8, paddingTop: 10, borderTop: '1px solid var(--border-light)' }}>
              {showAdvancedFilter && (() => {
                const selectedFilterColumn = activeColumns.find((c) => c.columnCode === filterCol);
                const catOpts =
                  selectedFilterColumn?.refCategoryCode && refFilterVersion >= 0
                    ? getCategoryItems(selectedFilterColumn.refCategoryCode)
                    : [];
                const lookupOpts =
                  selectedFilterColumn?.refTableId && refFilterVersion >= 0
                    ? getLookupOptions(selectedFilterColumn)
                    : [];
                const colDataType = (selectedFilterColumn?.dataType || 'STRING').toUpperCase();

                return (
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                      <FilterListRoundedIcon sx={{ fontSize: 16, color: 'var(--text-muted)' }} />
                      <select
                        className="form-select"
                        style={{ width: 175, height: 32, fontSize: 12.5, padding: '2px 8px' }}
                        value={filterCol}
                        onChange={(e) => {
                          setFilterCol(e.target.value);
                          setFilterVal('');
                        }}
                      >
                        <option value="">-- Chọn cột cần lọc --</option>
                        {activeColumns.filter((c) => c.dataType !== 'SUB_TABLE').map((c) => (
                          <option key={c.id} value={c.columnCode}>{c.columnName}</option>
                        ))}
                        {activeTable.approvalEnabled && <option value="approvalStatus">Trạng thái duyệt</option>}
                      </select>

                      {filterCol === 'approvalStatus' ? (
                        <select
                          className="form-select"
                          style={{ width: 175, height: 32, fontSize: 12.5, padding: '2px 8px' }}
                          value={filterVal}
                          onChange={(e) => setFilterVal(e.target.value)}
                        >
                          <option value="">-- Chọn trạng thái --</option>
                          <option value="DRAFT">Nháp</option>
                          <option value="PENDING">Chờ duyệt</option>
                          <option value="APPROVED">Đã duyệt</option>
                          <option value="REJECTED">Từ chối</option>
                        </select>
                      ) : catOpts.length > 0 ? (
                        <select
                          className="form-select"
                          style={{ width: 195, height: 32, fontSize: 12.5, padding: '2px 8px' }}
                          value={filterVal}
                          onChange={(e) => setFilterVal(e.target.value)}
                        >
                          <option value="">-- Chọn giá trị --</option>
                          {catOpts.map((opt) => (
                            <option key={opt.id} value={opt.itemCode}>{opt.itemName}</option>
                          ))}
                        </select>
                      ) : lookupOpts.length > 0 ? (
                        <select
                          className="form-select"
                          style={{ width: 195, height: 32, fontSize: 12.5, padding: '2px 8px' }}
                          value={filterVal}
                          onChange={(e) => setFilterVal(e.target.value)}
                        >
                          <option value="">-- Chọn bản ghi liên kết --</option>
                          {lookupOpts.map((opt) => (
                            <option key={opt.value} value={opt.value}>{opt.label}</option>
                          ))}
                        </select>
                      ) : colDataType === 'BOOLEAN' ? (
                        <select
                          className="form-select"
                          style={{ width: 150, height: 32, fontSize: 12.5, padding: '2px 8px' }}
                          value={filterVal}
                          onChange={(e) => setFilterVal(e.target.value)}
                        >
                          <option value="">-- Chọn Có / Không --</option>
                          <option value="true">Có</option>
                          <option value="false">Không</option>
                        </select>
                      ) : (
                        <input
                          type={colDataType === 'DATE' ? 'date' : 'text'}
                          className="form-input"
                          style={{ width: 185, height: 32, fontSize: 12.5, padding: '2px 10px' }}
                          placeholder="Nhập giá trị cần lọc..."
                          value={filterVal}
                          onChange={(e) => setFilterVal(e.target.value)}
                        />
                      )}

                      <button
                        type="button"
                        className="btn btn-primary btn-xs"
                        disabled={!filterCol || !filterVal.trim()}
                        onClick={() => {
                          if (!filterCol || !filterVal.trim()) return;
                          setColumnFilters((prev) => ({ ...prev, [filterCol]: filterVal.trim() }));
                          setFilterVal('');
                          setPage(1);
                        }}
                      >
                        Áp dụng
                      </button>

                      {sortField && (
                        <span
                          className="badge badge-info"
                          style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: 11.5 }}
                          title="Nhấp vào tiêu đề cột trên bảng để đổi thứ tự sắp xếp"
                        >
                          Sắp xếp: {activeColumns.find((c) => c.columnCode === sortField)?.columnName || sortField} ({sortDir === 'ASC' ? '↑' : '↓'})
                          <button
                            type="button"
                            style={{ border: 'none', background: 'transparent', cursor: 'pointer', padding: 0, color: 'inherit', fontWeight: 700 }}
                            onClick={() => {
                              setSortField('');
                              setSortDir('DESC');
                              setPage(1);
                            }}
                            title="Khôi phục sắp xếp mặc định (Mới nhất)"
                          >
                            ×
                          </button>
                        </span>
                      )}
                    </div>

                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      onClick={() => {
                        setSaveViewName('');
                        setShowSaveViewModal(true);
                      }}
                    >
                      <BookmarkAddRoundedIcon sx={{ fontSize: 14 }} /> Lưu chế độ xem
                    </button>
                  </div>
                );
              })()}

              {showAdvancedFilter && (
                <StructuredFilterBuilder
                  key={`${activeTable.id}_${filterBuilderKey}`}
                  columns={activeColumns}
                  approvalEnabled={Boolean(activeTable.approvalEnabled)}
                  value={structuredFilter}
                  onApply={(next) => {
                    setStructuredFilter(next);
                    setPage(1);
                  }}
                  onClear={() => {
                    setStructuredFilter(EMPTY_STRUCTURED_FILTER);
                    setPage(1);
                  }}
                />
              )}

              {(activeFilterCount > 0 || savedViews.length > 0) && (
                <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                  {savedViews.length > 0 && (
                    <span style={{ fontSize: 11.5, color: 'var(--text-muted)', fontWeight: 600 }}>Chế độ xem:</span>
                  )}
                  {savedViews.map((sv) => (
                    <span key={sv.id} className="badge badge-info" style={{ display: 'inline-flex', alignItems: 'center', gap: 5, cursor: 'pointer' }}>
                      <span
                        onClick={() => {
                          const norm = normalizeSavedViewFilters(sv.filters);
                          setColumnFilters(norm.flatFilters);
                          setStructuredFilter(norm.structured);
                          setFilterBuilderKey((k) => k + 1);
                          setSortField(sv.sortField || '');
                          setSortDir(sv.sortDir === 'ASC' ? 'ASC' : 'DESC');
                          setPage(1);
                        }}
                      >
                        ★ {sv.name}
                      </span>
                      <button
                        type="button"
                        style={{ border: 'none', background: 'transparent', cursor: 'pointer', padding: 0, color: 'inherit', fontWeight: 700 }}
                        onClick={async () => {
                          await fetch(`/api/records/views/${sv.id}`, { method: 'DELETE', credentials: 'include' });
                          await loadSavedViews();
                        }}
                        title="Xóa chế độ xem"
                      >
                        ×
                      </button>
                    </span>
                  ))}
                  {structuredFilter.conditions.length > 0 && (
                    <span className="badge" style={{ backgroundColor: 'var(--bg-surface)', border: '1px solid var(--border-subtle)', color: 'var(--text-secondary)', fontSize: 11 }}>
                      Logic: <strong>{structuredFilter.logic}</strong>
                    </span>
                  )}
                  {structuredFilter.conditions.map((cond, idx) => (
                    <span key={`sc_${idx}`} className="badge badge-info" style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                      {describeCondition(cond, activeColumns)}
                      <button
                        type="button"
                        style={{ border: 'none', background: 'transparent', cursor: 'pointer', padding: 0, color: 'inherit', fontWeight: 700 }}
                        onClick={() => {
                          setStructuredFilter((prev) => ({
                            ...prev,
                            conditions: prev.conditions.filter((_, i) => i !== idx),
                          }));
                          setFilterBuilderKey((k) => k + 1);
                          setPage(1);
                        }}
                      >
                        ×
                      </button>
                    </span>
                  ))}
                  {Object.entries(columnFilters).map(([k, v]) => {
                    const colObj = activeColumns.find((c) => c.columnCode === k);
                    const colLabel = k === 'approvalStatus' ? 'Trạng thái duyệt' : colObj?.columnName || k;
                    let valLabel = v;
                    if (k === 'approvalStatus') {
                      valLabel = v === 'DRAFT' ? 'Nháp' : v === 'PENDING' ? 'Chờ duyệt' : v === 'APPROVED' ? 'Đã duyệt' : v === 'REJECTED' ? 'Từ chối' : v;
                    } else if (colObj?.refCategoryCode && refFilterVersion >= 0) {
                      valLabel = getCategoryItemName(colObj.refCategoryCode, v)?.name || v;
                    } else if (colObj?.refTableId && refFilterVersion >= 0) {
                      valLabel = resolveLookupLabel(colObj, v) || v;
                    } else if (colObj?.dataType?.toUpperCase() === 'BOOLEAN') {
                      valLabel = v === 'true' ? 'Có' : 'Không';
                    }
                    return (
                      <span key={k} className="badge badge-warning" style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                        {colLabel}: {valLabel}
                        <button
                          type="button"
                          style={{ border: 'none', background: 'transparent', cursor: 'pointer', padding: 0, fontWeight: 700 }}
                          onClick={() => {
                            setColumnFilters((prev) => {
                              const next = { ...prev };
                              delete next[k];
                              return next;
                            });
                            setPage(1);
                          }}
                        >
                          ×
                        </button>
                      </span>
                    );
                  })}
                </div>
              )}
            </div>
          )}
        </div>

        {/* Quick Status Pills */}
        {activeTable && (viewMode === 'table' || viewMode === 'split') && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap', marginBottom: -4 }}>
            {[
              { key: 'ALL', label: 'Tất cả', active: statusFilter === 'ALL' && !columnFilters.approvalStatus, color: tree.app.color },
              { key: 'ACTIVE', label: 'Hợp lệ', active: statusFilter === 'ACTIVE' && !columnFilters.approvalStatus, color: '#137333' },
              { key: 'ERROR', label: 'Có lỗi', active: statusFilter === 'ERROR' && !columnFilters.approvalStatus, color: '#c5221f' },
              ...(activeTable.approvalEnabled
                ? [
                    { key: 'APP_DRAFT', label: 'Nháp', active: columnFilters.approvalStatus === 'DRAFT', color: '#5f6368' },
                    { key: 'APP_PENDING', label: 'Chờ duyệt', active: columnFilters.approvalStatus === 'PENDING', color: '#b06000' },
                    { key: 'APP_APPROVED', label: 'Đã duyệt', active: columnFilters.approvalStatus === 'APPROVED', color: '#137333' },
                    { key: 'APP_REJECTED', label: 'Từ chối', active: columnFilters.approvalStatus === 'REJECTED', color: '#c5221f' },
                  ]
                : []),
            ].map((pill) => (
              <button
                key={pill.key}
                type="button"
                className={`btn btn-xs ${pill.active ? 'btn-primary' : 'btn-outline'}`}
                style={{
                  borderRadius: 999,
                  padding: '4px 12px',
                  ...(pill.active ? { backgroundColor: pill.color, borderColor: pill.color, color: '#fff' } : {}),
                }}
                onClick={() => {
                  if (pill.key === 'ALL') {
                    setStatusFilter('ALL');
                    setColumnFilters((prev) => {
                      const next = { ...prev };
                      delete next.approvalStatus;
                      return next;
                    });
                  } else if (pill.key === 'ACTIVE' || pill.key === 'ERROR') {
                    setStatusFilter(pill.key);
                    setColumnFilters((prev) => {
                      const next = { ...prev };
                      delete next.approvalStatus;
                      return next;
                    });
                  } else {
                    const appSt = pill.key.replace('APP_', '');
                    setStatusFilter('ALL');
                    setColumnFilters((prev) => ({ ...prev, approvalStatus: appSt }));
                  }
                  setPage(1);
                }}
              >
                {pill.label}
              </button>
            ))}
          </div>
        )}

        {/* Dynamic Views */}
        {activeTable ? (
          viewMode === 'split' ? (
            <RuntimeSplitView
              records={records}
              columns={activeColumns}
              tableDisplayName={activeTable.displayName}
              appCode={tree.app.code}
              loading={loadingRecords}
              canEdit={canEdit}
              canApprove={(() => {
                if (!user) return false;
                if (user.isAdmin) return true;
                const uRoles = user.roleCodes?.length ? user.roleCodes : [user.roleCode];
                if (uRoles.some((r) => r.toUpperCase() === 'ADMIN')) return true;
                const approvers = activeTable.approverRoles || [];
                if (approvers.length === 0) return canEdit;
                return uRoles.some((ur) => approvers.some((ar) => ar.toUpperCase() === ur.toUpperCase()));
              })()}
              searchValue={search}
              onSearchChange={(val) => {
                setSearch(val);
                setPage(1);
              }}
              onChanged={() => {
                void loadRecords();
                void refreshApps();
              }}
            />
          ) : viewMode === 'gallery' ? (
            <GalleryView
              records={records}
              columns={activeColumns}
              accentColor={tree.app.color}
              canEdit={canEdit}
              onInspectRelations={(rec) => setDrawerRecord(rec)}
              onEditRecord={
                canEdit
                  ? (rec) => {
                      setEditingRecord(rec);
                      setShowRecordModal(true);
                    }
                  : undefined
              }
            />
          ) : viewMode === 'timeline' && calendarColumn ? (
            <TimelineGanttView
              records={records}
              columns={activeColumns}
              accentColor={tree.app.color}
              onSelectRecord={(rec) => setDrawerRecord(rec)}
            />
          ) : viewMode === 'kanban' && canKanban ? (
            <KanbanBoardView
              groupColumn={kanbanColumn}
              groupableColumns={kanbanGroupableColumns}
              approvalEnabled={Boolean(activeTable.approvalEnabled)}
              columns={activeColumns}
              records={records}
              onInspectRelations={(rec) => setDrawerRecord(rec)}
              onEditRecord={(rec) => {
                if (!canEdit) return;
                setEditingRecord(rec);
                setShowRecordModal(true);
              }}
              onMoveStatus={async (recordId, field, nextVal) => {
                if (!canEdit) {
                  toast('Vai trò của bạn chỉ có quyền xem, không được sửa.', 'error');
                  return;
                }
                await patchCellWithConflict(recordId, field, nextVal);
              }}
            />
          ) : viewMode === 'calendar' && calendarColumn ? (
            <CalendarView
              dateColumn={calendarColumn}
              columns={activeColumns}
              records={records}
              accentColor={tree.app.color}
              onSelectRecord={(rec) => setDrawerRecord(rec)}
            />
          ) : viewMode === 'analytics' ? (
            <AnalyticsPanel key={activeTable.id} tableId={activeTable.id} />
          ) : (
            <DynamicDataTable
              title={activeTable.displayName}
              subtitle={activeTable.description || `Bảng động ${activeTable.tableName}`}
              badge={<span className="badge badge-info">{activeColumns.length} trường</span>}
              headerActions={
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                  {canExport && (
                    <div ref={exportMenuRef} style={{ position: 'relative', display: 'inline-flex' }}>
                      <button
                        type="button"
                        className="btn btn-outline btn-sm"
                        disabled={exporting}
                        aria-busy={exporting}
                        onClick={() => handleExportExcel(false)}
                        title="Xuất Excel theo bộ lọc hiện tại"
                        style={{
                          borderTopRightRadius: 0,
                          borderBottomRightRadius: 0,
                          borderRight: 'none',
                          display: 'inline-flex',
                          alignItems: 'center',
                          gap: 6,
                        }}
                      >
                        <CloudDownloadRoundedIcon sx={{ fontSize: 16 }} />
                        <span>{exporting ? 'Đang xuất...' : 'Xuất Excel'}</span>
                      </button>
                      <button
                        type="button"
                        className="btn btn-outline btn-sm"
                        disabled={exporting}
                        aria-label="Tùy chọn xuất Excel"
                        aria-expanded={showExportMenu}
                        onClick={() => setShowExportMenu((v) => !v)}
                        style={{
                          borderTopLeftRadius: 0,
                          borderBottomLeftRadius: 0,
                          paddingLeft: 6,
                          paddingRight: 6,
                        }}
                      >
                        <KeyboardArrowDownRoundedIcon sx={{ fontSize: 16 }} />
                      </button>
                      {showExportMenu && (
                        <div
                          style={{
                            position: 'absolute',
                            top: 'calc(100% + 6px)',
                            right: 0,
                            minWidth: 230,
                            backgroundColor: 'var(--bg-surface)',
                            border: '1px solid var(--border-subtle)',
                            borderRadius: 10,
                            boxShadow: 'var(--shadow-lg)',
                            padding: 6,
                            zIndex: 40,
                            display: 'flex',
                            flexDirection: 'column',
                            gap: 2,
                          }}
                        >
                          <button
                            type="button"
                            className="utility-menu-item"
                            onClick={() => {
                              setShowExportMenu(false);
                              handleExportExcel(false);
                            }}
                            style={{
                              width: '100%',
                              textAlign: 'left',
                              padding: '8px 10px',
                              border: 'none',
                              borderRadius: 6,
                              background: 'transparent',
                              cursor: 'pointer',
                              fontSize: 13,
                              fontWeight: 500,
                              color: 'var(--text-primary)',
                            }}
                          >
                            Xuất kết quả đang lọc (.xlsx)
                          </button>
                          <button
                            type="button"
                            className="utility-menu-item"
                            onClick={() => {
                              setShowExportMenu(false);
                              handleExportExcel(true);
                            }}
                            style={{
                              width: '100%',
                              textAlign: 'left',
                              padding: '8px 10px',
                              border: 'none',
                              borderRadius: 6,
                              background: 'transparent',
                              cursor: 'pointer',
                              fontSize: 13,
                              fontWeight: 500,
                              color: 'var(--text-primary)',
                            }}
                          >
                            Xuất toàn bộ bảng (.xlsx)
                          </button>
                          <button
                            type="button"
                            className="utility-menu-item"
                            onClick={() => {
                              setShowExportMenu(false);
                              handleDownloadExcelTemplate();
                            }}
                            style={{
                              width: '100%',
                              textAlign: 'left',
                              padding: '8px 10px',
                              border: 'none',
                              borderRadius: 6,
                              background: 'transparent',
                              cursor: 'pointer',
                              fontSize: 13,
                              fontWeight: 500,
                              color: 'var(--text-primary)',
                            }}
                          >
                            Tải file mẫu nhập (.xlsx)
                          </button>
                        </div>
                      )}
                    </div>
                  )}
                  <button
                    type="button"
                    className="btn btn-outline btn-sm"
                    onClick={() => setShowPrintList(true)}
                    title={`In danh sách ${activeTable.displayName} (A4)`}
                    style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}
                  >
                    <PrintRoundedIcon sx={{ fontSize: 16 }} />
                    <span>In danh sách</span>
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline btn-sm"
                    onClick={() => setShowActivityDrawer(true)}
                    title={`Lịch sử thao tác (${activeTable.displayName})`}
                    style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}
                  >
                    <HistoryRoundedIcon sx={{ fontSize: 16 }} />
                    <span>Lịch sử thao tác</span>
                  </button>
                </div>
              }
              columns={activeColumns}
              records={records}
              loading={loadingRecords}
              enableSelection={canDelete || canEdit}
              selectedIds={selectedIds}
              onSelectionChange={setSelectedIds}
              batchActions={
                canEdit && selectedIds.length > 0 ? (
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    onClick={() => {
                      const editable = activeColumns.filter(
                        (c) =>
                          !c.formula &&
                          !c.isIdentityKey &&
                          !['AUTO_NUMBER', 'SUB_TABLE', 'FILE', 'IMAGE'].includes((c.dataType || '').toUpperCase())
                      );
                      setBulkField(editable[0]?.columnCode || '');
                      setBulkValue('');
                      setShowBulkEdit(true);
                    }}
                  >
                    <EditRoundedIcon sx={{ fontSize: 14 }} /> Cập nhật hàng loạt
                  </button>
                ) : undefined
              }
              inlineEditEnabled={canEdit}
              searchValue={search}
              onSearchChange={(v) => {
                setSearch(v);
                setPage(1);
              }}
              statusFilter={{
                value: statusFilter,
                onChange: (v) => {
                  setStatusFilter(v);
                  setPage(1);
                },
                options: [
                  { value: 'ALL', label: 'Tất cả trạng thái' },
                  { value: 'ACTIVE', label: 'Hợp lệ' },
                  { value: 'ERROR', label: 'Có lỗi kiểm tra' },
                ],
              }}
              storageKey={`${tree.app.code}_${activeTable.id}`}
              serverSort={{
                sortKey: sortField || 'updated_at',
                direction: sortDir === 'ASC' ? 'asc' : 'desc',
                onSortChange: (key, dir) => {
                  setSortField(key);
                  setSortDir(dir === 'asc' ? 'ASC' : 'DESC');
                  setPage(1);
                },
              }}
              pagination={{
                page,
                pageSize,
                total,
                totalPages,
                onPageChange: setPage,
                onPageSizeChange: (s) => {
                  setPageSize(s);
                  setPage(1);
                },
              }}
              onInspectRelations={(rec) => setDrawerRecord(rec)}
              onPrintRecord={(rec) => setPrintRecord(rec)}
              approvalEnabled={Boolean(activeTable.approvalEnabled)}
              currentUserRoles={user?.roleCodes?.length ? user.roleCodes : user?.roleCode ? [user.roleCode] : []}
              canApprove={(() => {
                if (user?.isAdmin || user?.roleCode === 'ADMIN') return true;
                if (!canApproveInApp) return false;
                const approvers = activeTable.approverRoles || [];
                if (approvers.length === 0) return true;
                const uRoles = user?.roleCodes?.length ? user.roleCodes : user?.roleCode ? [user.roleCode] : [];
                return uRoles.some((ur) => approvers.some((ar) => ar.toUpperCase() === ur.toUpperCase()));
              })()}
              onSubmitApproval={handleSubmitApproval}
              onApproveRecord={handleApproveRecord}
              onRecallApproval={handleRecallApproval}
              onCancelApproval={handleCancelApproval}
              onEditRecord={
                canEdit
                  ? (rec) => {
                      setEditingRecord(rec);
                      setShowRecordModal(true);
                    }
                  : undefined
              }
              onDeleteRecord={
                canDelete
                  ? (id) => {
                      setConfirm({
                        open: true,
                        title: 'Xóa bản ghi',
                        message: 'Bạn chắc chắn muốn xóa bản ghi này khỏi bảng động?',
                        onConfirm: async () => {
                          setConfirm((c) => ({ ...c, open: false }));
                          await fetch(`/api/records/${id}`, { method: 'DELETE' });
                          await loadRecords();
                          await refreshApps();
                          toast('Đã chuyển bản ghi vào Thùng rác', 'success', {
                            actionLabel: 'Hoàn tác',
                            onAction: async () => {
                              const r = await fetch(`/api/records/${id}/restore`, {
                                method: 'POST',
                                credentials: 'include',
                              }).then((res) => res.json());
                              if (r.success) {
                                await loadRecords();
                                await refreshApps();
                                toast('Đã khôi phục bản ghi');
                              } else {
                                toast(r.error || 'Không thể khôi phục bản ghi', 'error');
                              }
                            },
                          });
                        },
                      });
                    }
                  : undefined
              }
              onBatchDelete={
                canDelete
                  ? (ids) => {
                      setConfirm({
                        open: true,
                        title: `Xóa ${ids.length} bản ghi`,
                        message: `Xác nhận xóa ${ids.length} bản ghi đã chọn?`,
                        onConfirm: async () => {
                          setConfirm((c) => ({ ...c, open: false }));
                          await fetch('/api/records/batch-delete', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ ids }),
                          });
                          setSelectedIds([]);
                          await loadRecords();
                          await refreshApps();
                          toast(`Đã chuyển ${ids.length} bản ghi vào Thùng rác`, 'success', {
                            actionLabel: 'Hoàn tác',
                            onAction: async () => {
                              const results = await Promise.all(
                                ids.map((id) =>
                                  fetch(`/api/records/${id}/restore`, {
                                    method: 'POST',
                                    credentials: 'include',
                                  }).then((res) => res.json())
                                )
                              );
                              await loadRecords();
                              await refreshApps();
                              if (results.every((r) => r.success)) {
                                toast(`Đã khôi phục ${ids.length} bản ghi`);
                              } else {
                                toast('Một số bản ghi không thể khôi phục', 'error');
                              }
                            },
                          });
                        },
                      });
                    }
                  : undefined
              }
              onDirectSave={
                canEdit
                  ? async (rowId, field, newVal) => patchCellWithConflict(rowId, field, newVal)
                  : undefined
              }
              emptyAction={
                canCreate ? (
                  <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', justifyContent: 'center' }}>
                    <button
                      type="button"
                      className="btn btn-primary btn-sm"
                      onClick={() => {
                        setEditingRecord(null);
                        setShowRecordModal(true);
                      }}
                    >
                      <AddRoundedIcon sx={{ fontSize: 16 }} /> Thêm bản ghi đầu tiên
                    </button>
                    <button
                      type="button"
                      className="btn btn-outline btn-sm"
                      onClick={() => setShowImportModal(true)}
                    >
                      <CloudUploadRoundedIcon sx={{ fontSize: 16 }} /> Nhập từ Excel
                    </button>
                  </div>
                ) : undefined
              }
            />
          )
        ) : (
          <EmptyState
            title="Ứng dụng chưa có Bảng động"
            description="Hãy vào App Builder Studio để thêm Phân hệ và Bảng động cho ứng dụng này."
            action={
              canStudio ? (
                <Link href="/studio" className="btn btn-primary">
                  Mở App Builder Studio
                </Link>
              ) : undefined
            }
          />
        )}
        </div>
        </div>
        )}
      </div>

      {activeTable && (
        <>
          <RecordEditorModal
            open={showRecordModal}
            record={editingRecord}
            tableId={activeTable.id}
            tableName={activeTable.displayName}
            columns={activeColumns}
            formRules={activeTable.formRules}
            formLayout={activeTable.formLayout}
            defaultOverrides={personalFormDefaults.defaults}
            lockedFields={personalFormDefaults.locked}
            saving={saving}
            onClose={() => setShowRecordModal(false)}
            onSave={saveRecord}
            onSaveWithRelations={saveRecord}
          />
          <Record360Drawer
            open={Boolean(drawerRecord)}
            record={drawerRecord}
            columns={activeColumns}
            tableDisplayName={activeTable.displayName}
            appCode={tree.app.code}
            canEdit={canEdit}
            canApprove={canApproveInApp}
            onClose={() => setDrawerRecord(null)}
            onChanged={async () => {
              await loadRecords();
              await refreshApps();
            }}
          />
          <PrintableRecordModal
            open={Boolean(printRecord)}
            app={tree.app}
            table={activeTable}
            columns={activeColumns}
            record={printRecord}
            onClose={() => setPrintRecord(null)}
          />
        </>
      )}

      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((c) => ({ ...c, open: false }))}
      />

      <AppActivityDrawer
        open={showActivityDrawer}
        appId={tree.app.id}
        appName={tree.app.name}
        appColor={tree.app.color}
        defaultTableId={activeTable?.id || ''}
        tables={tree.modules.flatMap((m) =>
          m.tables.map((t) => ({
            id: t.table.id,
            displayName: t.table.displayName,
            columns: t.columns,
          }))
        )}
        onClose={() => setShowActivityDrawer(false)}
        onOpenRecord={async (recordId, targetTableId) => {
          const hit = records.find((r) => r.id === recordId);
          if (hit) {
            setDrawerRecord(hit);
            return;
          }
          const ownerMod = tree.modules.find((m) => m.tables.some((t) => t.table.id === targetTableId));
          if (ownerMod) setActiveModId(ownerMod.module.id);
          if (targetTableId) setActiveTableId(targetTableId);
          setAppSection('data');
          setViewMode('table');
          try {
            const qs = new URLSearchParams({
              appId: tree.app.id,
              tableId: targetTableId,
              limit: '200',
              personalMode: effectivePersonalMode ? 'true' : 'false',
            });
            const res = await fetch(`/api/records?${qs.toString()}`, { credentials: 'include' }).then((r) => r.json());
            const items: AppRecord[] = res.data?.items || [];
            const found = items.find((item) => item.id === recordId);
            if (found) setDrawerRecord(found);
            else toast('Bản ghi đã chuyển trang hoặc không còn khả dụng.', 'info');
          } catch {
            toast('Không tải được chi tiết bản ghi.', 'error');
          }
        }}
      />

      <AppPermissionsModal
        open={showAppPermModal}
        app={tree.app}
        onClose={() => setShowAppPermModal(false)}
        onSaved={async () => {
          setShowAppPermModal(false);
          await refreshAuth();
          await loadAppTree();
          await loadRecords();
          toast(`Đã cập nhật phân quyền trong App ${tree.app.code}!`);
        }}
      />

      <NotificationSnackbar
        open={snack.open}
        message={snack.message}
        severity={snack.severity}
        actionLabel={snack.actionLabel}
        onAction={snack.onAction}
        onClose={() => setSnack((s) => ({ ...s, open: false }))}
      />

      {showPrintList && activeTable && (() => {
        const printCols = activeColumns.filter(
          (c) => c.showInTable !== false && (c.dataType || '').toUpperCase() !== 'SUB_TABLE'
        ).slice(0, 8);
        return (
          <div className="modal-overlay" onClick={() => setShowPrintList(false)}>
            <div
              className="modal-dialog"
              style={{ maxWidth: 960, width: '95vw', maxHeight: '92vh', overflowY: 'auto' }}
              onClick={(e) => e.stopPropagation()}
            >
              <div className="no-print" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14, paddingBottom: 10, borderBottom: '1px solid var(--border-light)' }}>
                <span style={{ fontSize: 14, fontWeight: 700 }}>Xem trước bản in danh sách (A4)</span>
                <div style={{ display: 'flex', gap: 8 }}>
                  <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowPrintList(false)}>
                    Đóng
                  </button>
                  <button type="button" className="btn btn-primary btn-sm" onClick={() => window.print()}>
                    <PrintRoundedIcon sx={{ fontSize: 15 }} /> In báo cáo (A4)
                  </button>
                </div>
              </div>
              <div style={{ padding: '4px 2px', color: '#111827', background: '#fff' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 12 }}>
                  <div>
                    <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: '#4b5563' }}>
                      {tree.app.name} ({tree.app.code})
                    </div>
                    <h2 style={{ fontSize: 18, fontWeight: 700, margin: '2px 0' }}>
                      DANH SÁCH {activeTable.displayName.toUpperCase()}
                    </h2>
                  </div>
                  <div style={{ textAlign: 'right', fontSize: 11.5, color: '#4b5563' }}>
                    <div>Ngày in: {new Date().toLocaleString('vi-VN')}</div>
                    <div>Hiển thị: {records.length} / {total} bản ghi</div>
                  </div>
                </div>
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
                  <thead>
                    <tr style={{ backgroundColor: '#f3f4f6' }}>
                      <th style={{ border: '1px solid #d1d5db', padding: '6px 8px', textAlign: 'center', width: 42 }}>STT</th>
                      <th style={{ border: '1px solid #d1d5db', padding: '6px 8px', textAlign: 'left' }}>Mã</th>
                      {printCols.map((c) => (
                        <th key={c.id} style={{ border: '1px solid #d1d5db', padding: '6px 8px', textAlign: 'left' }}>
                          {c.columnName}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {records.map((rec, idx) => (
                      <tr key={rec.id}>
                        <td style={{ border: '1px solid #d1d5db', padding: '5px 8px', textAlign: 'center' }}>{(page - 1) * pageSize + idx + 1}</td>
                        <td style={{ border: '1px solid #d1d5db', padding: '5px 8px', fontWeight: 600 }}>{rec.recordKey || rec.id.slice(0, 8)}</td>
                        {printCols.map((c) => {
                          const raw = rec.data?.[c.columnCode];
                          const dt = (c.dataType || '').toUpperCase();
                          let disp = raw === null || raw === undefined ? '' : String(raw);
                          if (dt === 'BOOLEAN') disp = raw ? 'Có' : 'Không';
                          else if (c.refCategoryCode && raw) disp = getCategoryItemName(c.refCategoryCode, String(raw))?.name ?? String(raw);
                          else if (c.refTableId && raw) disp = resolveLookupLabel(c, raw) ?? String(raw);
                          return (
                            <td key={c.id} style={{ border: '1px solid #d1d5db', padding: '5px 8px' }}>
                              {disp}
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
        );
      })()}

      {showBulkEdit && activeTable && (() => {
        const editableCols = activeColumns.filter(
          (c) =>
            !c.formula &&
            !c.isIdentityKey &&
            !['AUTO_NUMBER', 'SUB_TABLE', 'FILE', 'IMAGE'].includes((c.dataType || '').toUpperCase())
        );
        const targetCol = editableCols.find((c) => c.columnCode === bulkField) || editableCols[0] || null;
        return (
          <div className="modal-overlay" onClick={() => !bulkSaving && setShowBulkEdit(false)}>
            <div className="modal-dialog" style={{ maxWidth: 460 }} onClick={(e) => e.stopPropagation()}>
              <h3 style={{ fontSize: 16, fontWeight: 700, marginBottom: 4 }}>
                Cập nhật hàng loạt ({selectedIds.length} bản ghi)
              </h3>
              <p style={{ fontSize: 12, color: 'var(--text-muted)', marginBottom: 14 }}>
                Chọn trường dữ liệu và giá trị mới để áp dụng cho tất cả bản ghi đang chọn trong {activeTable.displayName}.
              </p>
              <form
                onSubmit={async (e) => {
                  e.preventDefault();
                  if (!targetCol || bulkSaving) return;
                  setBulkSaving(true);
                  let okCount = 0;
                  let failCount = 0;
                  try {
                    const results = await Promise.all(
                      selectedIds.map(async (id) => {
                        try {
                          const res = await fetch(`/api/records/${id}/cell`, {
                            method: 'PATCH',
                            headers: { 'Content-Type': 'application/json' },
                            credentials: 'include',
                            body: JSON.stringify({ field: targetCol.columnCode, value: bulkValue }),
                          });
                          const j = await res.json().catch(() => null);
                          return Boolean(res.ok && j?.success);
                        } catch {
                          return false;
                        }
                      })
                    );
                    okCount = results.filter(Boolean).length;
                    failCount = results.length - okCount;
                    setShowBulkEdit(false);
                    setSelectedIds([]);
                    await loadRecords();
                    await refreshApps();
                    toast(
                      failCount > 0
                        ? `Đã cập nhật ${okCount}/${results.length} bản ghi (${failCount} lỗi).`
                        : `Đã cập nhật "${targetCol.columnName}" cho ${okCount} bản ghi.`,
                      failCount > 0 ? 'error' : 'success'
                    );
                  } finally {
                    setBulkSaving(false);
                  }
                }}
                style={{ display: 'flex', flexDirection: 'column', gap: 12 }}
              >
                <div className="form-group">
                  <label className="form-label">Trường cần cập nhật</label>
                  <select
                    className="form-select"
                    value={targetCol?.columnCode || ''}
                    onChange={(e) => {
                      setBulkField(e.target.value);
                      setBulkValue('');
                    }}
                  >
                    {editableCols.map((c) => (
                      <option key={c.id} value={c.columnCode}>
                        {c.columnName} ({c.columnCode})
                      </option>
                    ))}
                  </select>
                </div>
                {targetCol && (
                  <div className="form-group">
                    <label className="form-label">Giá trị mới</label>
                    <DynamicFieldInput
                      column={targetCol}
                      value={bulkValue}
                      formData={{ [targetCol.columnCode]: bulkValue }}
                      onChange={(val) => setBulkValue(val === null || val === undefined ? '' : String(val))}
                    />
                  </div>
                )}
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 6 }}>
                  <button type="button" className="btn btn-outline btn-sm" disabled={bulkSaving} onClick={() => setShowBulkEdit(false)}>
                    Hủy
                  </button>
                  <button type="submit" className="btn btn-primary btn-sm" disabled={bulkSaving || !targetCol}>
                    {bulkSaving ? 'Đang cập nhật...' : `Áp dụng cho ${selectedIds.length} bản ghi`}
                  </button>
                </div>
              </form>
            </div>
          </div>
        );
      })()}

      {showTrash && activeTable && (
        <div className="modal-overlay" onClick={() => setShowTrash(false)}>
          <div className="modal-dialog" style={{ maxWidth: 720 }} onClick={(e) => e.stopPropagation()}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
              <h3 style={{ fontSize: 17, fontWeight: 700 }}>
                Thùng rác — {activeTable.displayName} ({trashRecords.length})
              </h3>
              <button type="button" className="btn btn-outline btn-xs" onClick={() => setShowTrash(false)}>
                Đóng
              </button>
            </div>
            {trashRecords.length === 0 ? (
              <div style={{ padding: 28, textAlign: 'center', color: 'var(--text-muted)', fontSize: 13 }}>
                Thùng rác đang trống.
              </div>
            ) : (
              <table className="data-table">
                <thead>
                  <tr><th>Khóa / ID</th><th>Người xóa</th><th>Thời điểm xóa</th><th>Thao tác</th></tr>
                </thead>
                <tbody>
                  {trashRecords.map((tr) => (
                    <tr key={tr.id}>
                      <td><code>{tr.recordKey || tr.id}</code></td>
                      <td>{tr.deletedBy || 'system'}</td>
                      <td style={{ fontSize: 12 }}>
                        {tr.deletedAt ? new Date(tr.deletedAt).toLocaleString('vi-VN') : ''}
                      </td>
                      <td>
                        <div style={{ display: 'inline-flex', gap: 6 }}>
                          <button
                            type="button"
                            className="btn btn-outline btn-xs"
                            style={{ color: '#1e8e3e' }}
                            onClick={async () => {
                              const res = await fetch(`/api/records/${tr.id}/restore`, {
                                method: 'POST',
                                credentials: 'include',
                              }).then((r) => r.json());
                              if (res.success) {
                                await loadTrash();
                                await loadRecords();
                                await refreshApps();
                                toast('Đã khôi phục bản ghi!');
                              }
                            }}
                          >
                            <RestoreFromTrashRoundedIcon sx={{ fontSize: 14 }} /> Khôi phục
                          </button>
                          {(user?.isAdmin || user?.roleCode === 'ADMIN' || user?.roleCodes?.includes('ADMIN')) && (
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              style={{ color: '#d93025' }}
                              onClick={async () => {
                                await fetch(`/api/records/${tr.id}/permanent`, {
                                  method: 'DELETE',
                                  credentials: 'include',
                                });
                                await loadTrash();
                                toast('Đã xóa vĩnh viễn!');
                              }}
                            >
                              Xóa hẳn
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>
      )}

      {showImportModal && activeTable && (
        <ExcelImportDialog key={activeTable.id} appId={tree.app.id} moduleId={activeTable.moduleId}
          tableId={activeTable.id} tableName={activeTable.displayName} columns={activeColumns}
          onClose={() => setShowImportModal(false)} onImported={(inserted, updated) => {
            setShowImportModal(false);
            void loadRecords();
            void refreshApps();
            toast(`Nhập thành công: ${inserted} dòng mới, ${updated} dòng cập nhật.`);
          }} />
      )}

      {showSaveViewModal && activeTable && (
        <div className="modal-overlay" onClick={() => setShowSaveViewModal(false)}>
          <div className="modal-dialog" style={{ maxWidth: 420 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16, fontWeight: 700, marginBottom: 6 }}>Lưu chế độ xem nhanh</h3>
            <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginBottom: 12 }}>
              Lưu bộ lọc và thứ tự sắp xếp hiện tại để mở lại chỉ với 1 lần nhấp chuột.
            </p>
            <form
              onSubmit={async (e) => {
                e.preventDefault();
                if (!saveViewName.trim() || !tree?.app) return;
                const res = await fetch('/api/records/views', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  credentials: 'include',
                  body: JSON.stringify({
                    appId: tree.app.id,
                    tableId: activeTable.id,
                    name: saveViewName.trim(),
                    filters: effectiveFilters || {},
                    sortField: sortField || null,
                    sortDir,
                  }),
                }).then((r) => r.json());
                if (res.success) {
                  await loadSavedViews();
                  setShowSaveViewModal(false);
                  toast('Đã lưu Chế độ xem!');
                } else {
                  toast(res.message || 'Không thể lưu chế độ xem', 'error');
                }
              }}
              style={{ display: 'flex', flexDirection: 'column', gap: 12 }}
            >
              <input
                className="form-input"
                placeholder="Ví dụ: Nhân sự khối IT đang làm việc"
                value={saveViewName}
                onChange={(e) => setSaveViewName(e.target.value)}
                autoFocus
                required
              />
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowSaveViewModal(false)}>
                  Hủy
                </button>
                <button type="submit" className="btn btn-primary btn-sm" style={{ backgroundColor: tree.app.color }}>
                  Lưu chế độ xem
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {rejectTarget && (
        <div className="modal-overlay" onClick={() => setRejectTarget(null)}>
          <div className="modal-dialog" style={{ maxWidth: 440 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16, fontWeight: 700, marginBottom: 6 }}>Từ chối phê duyệt hồ sơ</h3>
            <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginBottom: 12 }}>
              Nhập lý do từ chối (tùy chọn) để người tạo dễ dàng chỉnh sửa và trình duyệt lại.
            </p>
            <textarea
              className="form-input"
              rows={3}
              placeholder="Ví dụ: Thiếu chữ ký xác nhận hoặc sai đơn giá..."
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
                  await submitApprovalDecision(target, false, reason);
                }}
              >
                Xác nhận từ chối
              </button>
            </div>
          </div>
        </div>
      )}

      {conflictState && (
        <div className="modal-overlay" onClick={() => !resolvingConflict && setConflictState(null)}>
          <div className="modal-dialog" style={{ maxWidth: 480 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16, fontWeight: 700, marginBottom: 6, color: 'var(--warning-text)' }}>
              Xung đột phiên bản dữ liệu (409 Conflict)
            </h3>
            <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 10 }}>
              {conflictState.serverMessage}
            </p>
            <div
              style={{
                padding: '10px 12px',
                borderRadius: 8,
                backgroundColor: 'var(--bg-secondary)',
                border: '1px solid var(--border-subtle)',
                fontSize: 12.5,
                marginBottom: 14,
              }}
            >
              <div>
                Bản ghi: <strong>{conflictState.recordKey || conflictState.recordId}</strong>
              </div>
              <div>
                Trường đang sửa: <strong>{conflictState.fieldLabel}</strong> → Giá trị của bạn:{' '}
                <code>{String(conflictState.attemptedValue ?? '(trống)')}</code>
              </div>
              <div style={{ marginTop: 4, color: 'var(--text-muted)', fontSize: 11.5 }}>
                Bảng dữ liệu đã được làm mới theo phiên bản mới nhất trên máy chủ.
              </div>
            </div>
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, flexWrap: 'wrap' }}>
              <button
                type="button"
                className="btn btn-outline btn-sm"
                disabled={resolvingConflict}
                onClick={() => setConflictState(null)}
              >
                Giữ dữ liệu mới từ Server
              </button>
              <button
                type="button"
                className="btn btn-primary btn-sm"
                style={{ backgroundColor: '#b06000', borderColor: '#b06000' }}
                disabled={resolvingConflict}
                onClick={async () => {
                  const target = conflictState;
                  setResolvingConflict(true);
                  try {
                    const latestRes = await fetch(`/api/records/${target.recordId}`, { credentials: 'include' })
                      .then((r) => r.json())
                      .catch(() => null);
                    const latestVer = latestRes?.success ? latestRes.data?.version : undefined;
                    setConflictState(null);
                    await patchCellWithConflict(target.recordId, target.field, target.attemptedValue, latestVer);
                  } finally {
                    setResolvingConflict(false);
                  }
                }}
              >
                {resolvingConflict ? 'Đang ghi đè...' : 'Ghi đè giá trị của tôi'}
              </button>
            </div>
          </div>
        </div>
      )}
      {activeTable && viewMode === 'table' && selectedIds.length > 0 && (
        <FloatingBulkActionBar
          tableId={activeTable.id}
          columns={activeColumns}
          selectedRecords={records.filter((r) => selectedIds.includes(r.id))}
          canEdit={canEdit}
          canDelete={canDelete}
          approvalEnabled={Boolean(activeTable.approvalEnabled)}
          canApprove={(() => {
            if (!user) return false;
            if (user.isAdmin) return true;
            const uRoles = user.roleCodes?.length ? user.roleCodes : [user.roleCode];
            if (uRoles.some((r) => r.toUpperCase() === 'ADMIN')) return true;
            const approvers = activeTable.approverRoles || [];
            if (approvers.length === 0) return canEdit;
            return uRoles.some((ur) => approvers.some((ar) => ar.toUpperCase() === ur.toUpperCase()));
          })()}
          onClearSelection={() => setSelectedIds([])}
          onBatchDelete={(ids) => {
            setConfirm({
              open: true,
              title: `Xóa ${ids.length} bản ghi`,
              message: `Xác nhận xóa ${ids.length} bản ghi đã chọn?`,
              onConfirm: async () => {
                setConfirm((c) => ({ ...c, open: false }));
                await fetch('/api/records/batch-delete', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({ ids }),
                });
                setSelectedIds([]);
                await loadRecords();
                await refreshApps();
                toast(`Đã chuyển ${ids.length} bản ghi vào Thùng rác`);
              },
            });
          }}
          onCompleted={async (summary, severity = 'success') => {
            setSelectedIds([]);
            await loadRecords();
            await refreshApps();
            toast(summary, severity);
          }}
        />
      )}
    </div>
  );
}
