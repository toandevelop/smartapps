'use client';

import React, { useState, useEffect, useCallback, useRef, useMemo, Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import {
  PageHeader,
  ClusterIcon,
  ConfirmDialog,
  NotificationSnackbar,
  DynamicDataTable,
  EmptyState,
  StatusBadge,
} from '@/app/components/ui';
import {
  AppModal,
  AppFormState,
  ModuleModal,
  ModuleFormState,
  TableModal,
  TableFormState,
  ColumnModal,
  ColumnFormState,
  RecordEditorModal,
  ExcelToAppModal,
  TemplateInstallModal,
  AutomationModal,
} from './StudioModals';
import StudioPlatformResourcesPanel, { PlatformSubTab } from './StudioPlatformResourcesPanel';
import { usePlatform } from '@/lib/app-context';
import { useAuth } from '@/lib/auth-context';
import { getColumnTypeMeta } from '@/lib/monday-fields';
import {
  SysApp,
  SysModule,
  SysTable,
  SysColumn,
  SysCategory,
  SysCategoryItem,
  AppRecord,
  AppAutomation,
  AppAutomationLog,
  SysUser,
  SysRole,
  PermissionCatalog,
  AppAuditLog,
  SysApiKey,
} from '@/lib/types';
import UserFormModal from './UserFormModal';
import RoleFormModal from './RoleFormModal';
import AppPermissionsModal from './AppPermissionsModal';
import AiAppModal from './AiAppModal';
import { AppPublishModal, AppPluginBindingsPanel, AppUpgradeV2Modal, OutboxMonitorPanel, PrintTemplateDesignerModal } from './StudioPlatformPanels';
import RocketLaunchRoundedIcon from '@mui/icons-material/RocketLaunchRounded';
import StorefrontRoundedIcon from '@mui/icons-material/StorefrontRounded';

import DesignServicesRoundedIcon from '@mui/icons-material/DesignServicesRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded';
import ViewColumnRoundedIcon from '@mui/icons-material/ViewColumnRounded';
import TableViewRoundedIcon from '@mui/icons-material/TableViewRounded';
import CodeRoundedIcon from '@mui/icons-material/CodeRounded';
import ExtensionRoundedIcon from '@mui/icons-material/ExtensionRounded';
import FunctionsRoundedIcon from '@mui/icons-material/FunctionsRounded';
import LinkRoundedIcon from '@mui/icons-material/LinkRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import CloudUploadRoundedIcon from '@mui/icons-material/CloudUploadRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import ShieldRoundedIcon from '@mui/icons-material/ShieldRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import DownloadRoundedIcon from '@mui/icons-material/DownloadRounded';
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import KeyboardArrowDownRoundedIcon from '@mui/icons-material/KeyboardArrowDownRounded';
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded';
import LayersRoundedIcon from '@mui/icons-material/LayersRounded';
import ApiRoundedIcon from '@mui/icons-material/ApiRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import VisibilityOffRoundedIcon from '@mui/icons-material/VisibilityOffRounded';

type StudioWorkspace = 'builder' | 'platform';
type BuilderSubTab = 'columns' | 'preview' | 'compile' | 'automations' | 'plugins' | 'integrations' | 'publicForms';

export default function AppBuilderStudioPage() {
  return (
    <Suspense fallback={<div className="page-content"><div className="card" style={{ padding: 36, textAlign: 'center', color: 'var(--text-muted)' }}>Đang khởi tạo App Builder Studio...</div></div>}>
      <AppBuilderStudioContent />
    </Suspense>
  );
}

function AppBuilderStudioContent() {
  const searchParams = useSearchParams();
  const requestedAppId = searchParams?.get('appId') || '';
  const { apps, refreshApps } = usePlatform();
  const { hasPermission, refreshAuth, user: currentUser } = useAuth();
  const canStudio = hasPermission('studio.manage');
  const canManageUsers = canStudio || hasPermission('user.manage');
  const canManageRoles = canStudio || hasPermission('role.manage');
  const canManageAuto = canStudio || hasPermission('automation.manage');
  const canViewAudit = canStudio || hasPermission('user.manage') || hasPermission('audit.view');
  const canManageInteg = canStudio || hasPermission('integration.manage');
  const canAccessStudio = canStudio || canManageUsers || canManageRoles || canManageAuto || canViewAudit || canManageInteg;

  const defaultWorkspace: StudioWorkspace =
    !canStudio && (canManageRoles || canManageUsers || canViewAudit) ? 'platform' : 'builder';
  const defaultBuilderTab: BuilderSubTab =
    !canStudio && !canManageRoles && !canManageUsers && !canViewAudit
      ? canManageAuto
        ? 'automations'
        : canManageInteg
        ? 'integrations'
        : 'columns'
      : 'columns';
  const defaultPlatformTab: PlatformSubTab =
    canStudio ? 'categories' : canManageRoles ? 'roles' : canManageUsers ? 'users' : canViewAudit ? 'audit' : 'categories';

  const [workspaceOverride, setWorkspaceOverride] = useState<StudioWorkspace | null>(null);
  const [builderTabOverride, setBuilderTabOverride] = useState<BuilderSubTab | null>(null);
  const [studioAdvancedMode, setStudioAdvancedMode] = useState<boolean>(() => {
    if (typeof window === 'undefined') return false;
    return localStorage.getItem('smartapps_studio_advanced') === 'true';
  });
  const [platformTabOverride, setPlatformTabOverride] = useState<PlatformSubTab | null>(null);
  const workspace = workspaceOverride ?? defaultWorkspace;
  const setWorkspace = setWorkspaceOverride;
  const builderTab = builderTabOverride ?? defaultBuilderTab;
  const setBuilderTab = setBuilderTabOverride;
  const platformTab = platformTabOverride ?? defaultPlatformTab;
  const setPlatformTab = setPlatformTabOverride;
  const [appQuery, setAppQuery] = useState('');

  const [selectedApp, setSelectedApp] = useState<SysApp | null>(null);
  const [modules, setModules] = useState<SysModule[]>([]);
  const [selectedModule, setSelectedModule] = useState<SysModule | null>(null);
  const [tables, setTables] = useState<SysTable[]>([]);
  const [allTables, setAllTables] = useState<SysTable[]>([]);
  const [selectedTable, setSelectedTable] = useState<SysTable | null>(null);
  const [columns, setColumns] = useState<SysColumn[]>([]);
  const [records, setRecords] = useState<AppRecord[]>([]);
  const [categories, setCategories] = useState<SysCategory[]>([]);
  const [selectedCat, setSelectedCat] = useState<SysCategory | null>(null);
  const [catItems, setCatItems] = useState<SysCategoryItem[]>([]);

  const [automations, setAutomations] = useState<AppAutomation[]>([]);
  const [autoLogs, setAutoLogs] = useState<AppAutomationLog[]>([]);
  const [users, setUsers] = useState<SysUser[]>([]);
  const [roles, setRoles] = useState<SysRole[]>([]);
  const [permCatalog, setPermCatalog] = useState<PermissionCatalog>({ systemPermissions: [], appPermissions: [] });
  const [showUserModal, setShowUserModal] = useState(false);
  const [editingUser, setEditingUser] = useState<SysUser | null>(null);
  const [showRoleModal, setShowRoleModal] = useState(false);
  const [editingRole, setEditingRole] = useState<SysRole | null>(null);
  const [appPermTarget, setAppPermTarget] = useState<SysApp | null>(null);
  const [globalAuditLogs, setGlobalAuditLogs] = useState<AppAuditLog[]>([]);
  const [auditAppFilter, setAuditAppFilter] = useState('');
  const [apiKeys, setApiKeys] = useState<SysApiKey[]>([]);
  const [newKeyName, setNewKeyName] = useState('');
  const [newKeyTableId, setNewKeyTableId] = useState('');
  const [createdRawKey, setCreatedRawKey] = useState<string | null>(null);
  const [showAiModal, setShowAiModal] = useState(false);

  const [compiledSchema, setCompiledSchema] = useState<{ engine: string; sqlDdl?: string; tsInterface?: string } | null>(null);
  const [showBlueprintMenu, setShowBlueprintMenu] = useState(false);
  const blueprintMenuRef = useRef<HTMLDivElement>(null);

  // Modals
  const [showAppModal, setShowAppModal] = useState(false);
  const [editingApp, setEditingApp] = useState<SysApp | null>(null);
  const [showModuleModal, setShowModuleModal] = useState(false);
  const [editingModule, setEditingModule] = useState<SysModule | null>(null);
  const [showTableModal, setShowTableModal] = useState(false);
  const [editingTable, setEditingTable] = useState<SysTable | null>(null);
  const [showPublishModal, setShowPublishModal] = useState(false);
  const [showUpgradeV2Modal, setShowUpgradeV2Modal] = useState(false);
  const [showPrintDesignerModal, setShowPrintDesignerModal] = useState(false);
  const [showColumnModal, setShowColumnModal] = useState(false);
  const [editingColumn, setEditingColumn] = useState<SysColumn | null>(null);
  const [showRecordModal, setShowRecordModal] = useState(false);
  const [editingRecord, setEditingRecord] = useState<AppRecord | null>(null);
  const [showExcelModal, setShowExcelModal] = useState(false);
  const [showTemplateModal, setShowTemplateModal] = useState(false);
  const [showAutoModal, setShowAutoModal] = useState(false);
  const [editingAuto, setEditingAuto] = useState<AppAutomation | null>(null);
  const [saving, setSaving] = useState(false);

  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });
  const [snack, setSnack] = useState<{ open: boolean; message: string; severity: 'success' | 'error' | 'info' }>({
    open: false,
    message: '',
    severity: 'info',
  });

  const toast = useCallback((message: string, severity: 'success' | 'error' | 'info' = 'success') => {
    setSnack({ open: true, message, severity });
  }, []);

  const copyText = (text: string, label: string) => {
    navigator.clipboard?.writeText(text);
    toast(`Đã sao chép ${label}!`, 'info');
  };

  useEffect(() => {
    const onOutside = (e: MouseEvent) => {
      if (blueprintMenuRef.current && !blueprintMenuRef.current.contains(e.target as Node)) {
        setShowBlueprintMenu(false);
      }
    };
    document.addEventListener('mousedown', onOutside);
    return () => document.removeEventListener('mousedown', onOutside);
  }, []);

  const loadCategories = useCallback(async () => {
    const res = await fetch('/api/categories').then((r) => r.json());
    if (res.success) {
      setCategories(res.data || []);
      setSelectedCat((prev) => prev || res.data?.[0] || null);
    }
  }, []);

  const loadAllTables = useCallback(async () => {
    const res = await fetch('/api/tables').then((r) => r.json());
    if (res.success) setAllTables(res.data || []);
  }, []);

  const loadAutomations = useCallback(async () => {
    const [aRes, lRes] = await Promise.all([
      fetch('/api/automations').then((r) => r.json()),
      fetch('/api/automations/logs?limit=15').then((r) => r.json()),
    ]);
    if (aRes.success) setAutomations(aRes.data || []);
    if (lRes.success) setAutoLogs(lRes.data || []);
  }, []);

  const loadUsers = useCallback(async () => {
    const res = await fetch('/api/auth/users', { credentials: 'include' }).then((r) => r.json());
    if (res.success) setUsers(res.data || []);
  }, []);

  const loadRoles = useCallback(async () => {
    const [rRes, cRes] = await Promise.all([
      fetch('/api/roles', { credentials: 'include' }).then((r) => r.json()),
      fetch('/api/roles/catalog', { credentials: 'include' }).then((r) => r.json()),
    ]);
    if (rRes.success) setRoles(rRes.data || []);
    if (cRes.success && cRes.data) setPermCatalog(cRes.data);
  }, []);

  useEffect(() => {
    let cancelled = false;
    void Promise.resolve().then(() => {
      if (cancelled) return;
      void loadCategories();
      void loadAllTables();
      void loadAutomations();
      void loadUsers();
      void loadRoles();
    });
    return () => {
      cancelled = true;
    };
  }, [loadCategories, loadAllTables, loadAutomations, loadUsers, loadRoles]);

  const loadGlobalAuditLogs = useCallback(async (appFilter?: string) => {
    const qs = appFilter ? `?appId=${encodeURIComponent(appFilter)}&limit=80` : '?limit=80';
    const res = await fetch(`/api/records/audit-logs${qs}`, { credentials: 'include' }).then((r) => r.json());
    if (res.success) setGlobalAuditLogs(res.data || []);
  }, []);

  const loadApiKeys = useCallback(async () => {
    const res = await fetch('/api/integrations/keys', { credentials: 'include' }).then((r) => r.json());
    if (res.success) setApiKeys(res.data || []);
  }, []);

  useEffect(() => {
    let cancelled = false;
    void Promise.resolve().then(() => {
      if (cancelled) return;
      if (workspace === 'platform' && platformTab === 'audit') void loadGlobalAuditLogs(auditAppFilter);
      if (workspace === 'builder' && builderTab === 'integrations') void loadApiKeys();
    });
    return () => {
      cancelled = true;
    };
  }, [workspace, platformTab, builderTab, auditAppFilter, loadGlobalAuditLogs, loadApiKeys]);

  useEffect(() => {
    if (!selectedCat) return;
    fetch(`/api/categories/items?categoryCode=${encodeURIComponent(selectedCat.categoryCode)}`)
      .then((r) => r.json())
      .then((res) => res.success && setCatItems(res.data || []));
  }, [selectedCat]);

  const appliedQueryAppRef = useRef(false);
  useEffect(() => {
    if (apps.length === 0) return;
    let cancelled = false;
    void Promise.resolve().then(() => {
      if (cancelled) return;
      if (requestedAppId && !appliedQueryAppRef.current) {
        const target = apps.find((a) => a.id === requestedAppId || a.code.toUpperCase() === requestedAppId.toUpperCase());
        if (target) {
          appliedQueryAppRef.current = true;
          setSelectedApp(target);
          setWorkspaceOverride('builder');
          return;
        }
      }
      setSelectedApp((prev) => {
        if (!prev) return apps[0];
        return apps.find((a) => a.id === prev.id) || apps[0];
      });
    });
    return () => {
      cancelled = true;
    };
  }, [apps, requestedAppId]);

  const loadModules = useCallback(async (appId: string) => {
    const res = await fetch(`/api/modules?appId=${encodeURIComponent(appId)}`).then((r) => r.json());
    if (res.success) {
      const list: SysModule[] = res.data || [];
      setModules(list);
      setSelectedModule((prev) => (prev && list.some((m) => m.id === prev.id) ? list.find((m) => m.id === prev.id)! : list[0] || null));
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    void Promise.resolve().then(() => {
      if (cancelled) return;
      if (selectedApp) void loadModules(selectedApp.id);
      else {
        setModules([]);
        setSelectedModule(null);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [selectedApp, loadModules]);

  const loadTables = useCallback(async (appId: string, moduleId: string) => {
    const res = await fetch(`/api/tables?appId=${encodeURIComponent(appId)}&moduleId=${encodeURIComponent(moduleId)}`).then((r) => r.json());
    if (res.success) {
      const list: SysTable[] = res.data || [];
      setTables(list);
      setSelectedTable((prev) => (prev && list.some((t) => t.id === prev.id) ? list.find((t) => t.id === prev.id)! : list[0] || null));
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    void Promise.resolve().then(() => {
      if (cancelled) return;
      if (selectedApp && selectedModule) void loadTables(selectedApp.id, selectedModule.id);
      else {
        setTables([]);
        setSelectedTable(null);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [selectedApp, selectedModule, loadTables]);

  const loadColumnsAndPreview = useCallback(async (tableId: string) => {
    const [colRes, recRes] = await Promise.all([
      fetch(`/api/columns?tableId=${encodeURIComponent(tableId)}`).then((r) => r.json()),
      fetch(`/api/records?tableId=${encodeURIComponent(tableId)}&limit=10`).then((r) => r.json()),
    ]);
    if (colRes.success) setColumns(colRes.data || []);
    if (recRes.success) setRecords(recRes.data?.items || []);
  }, []);

  useEffect(() => {
    let cancelled = false;
    void Promise.resolve().then(() => {
      if (cancelled) return;
      if (selectedTable) {
        void loadColumnsAndPreview(selectedTable.id);
        setCompiledSchema(null);
      } else {
        setColumns([]);
        setRecords([]);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [selectedTable, loadColumnsAndPreview]);

  useEffect(() => {
    if (workspace === 'builder' && builderTab === 'compile' && selectedTable) {
      fetch(`/api/tables/${encodeURIComponent(selectedTable.id)}/compile`)
        .then((r) => r.json())
        .then((res) => {
          if (res.success) {
            setCompiledSchema({
              engine: res.engine,
              sqlDdl: res.data?.sqlDdl,
              tsInterface: res.data?.tsInterface,
            });
          }
        });
    }
  }, [workspace, builderTab, selectedTable]);

  // Handlers
  const saveApp = async (form: AppFormState) => {
    setSaving(true);
    try {
      const res = await fetch('/api/apps', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(form),
      }).then((r) => r.json());
      if (res.success) {
        await refreshApps();
        setSelectedApp(res.data);
        setShowAppModal(false);
        toast('Đã lưu ứng dụng thành công!');
      } else {
        toast(res.message || 'Lỗi lưu App', 'error');
      }
    } finally {
      setSaving(false);
    }
  };

  const saveModule = async (form: ModuleFormState) => {
    setSaving(true);
    try {
      const res = await fetch('/api/modules', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(form),
      }).then((r) => r.json());
      if (res.success && selectedApp) {
        await loadModules(selectedApp.id);
        await refreshApps();
        setShowModuleModal(false);
        toast('Đã lưu phân hệ!');
      }
    } finally {
      setSaving(false);
    }
  };

  const saveTable = async (form: TableFormState) => {
    setSaving(true);
    try {
      const res = await fetch('/api/tables', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(form),
      }).then((r) => r.json());
      if (res.success && selectedApp && selectedModule) {
        await loadTables(selectedApp.id, selectedModule.id);
        await loadAllTables();
        await refreshApps();
        setShowTableModal(false);
        toast('Đã lưu bảng động!');
      } else { toast(res.message || 'Không thể lưu cấu hình bảng.', 'error'); }
    } finally {
      setSaving(false);
    }
  };

  const saveColumn = async (form: ColumnFormState) => {
    setSaving(true);
    try {
      const res = await fetch('/api/columns', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(form),
      }).then((r) => r.json());
      if (res.success && selectedTable) {
        await loadColumnsAndPreview(selectedTable.id);
        setShowColumnModal(false);
        toast('Đã cập nhật trường dữ liệu động!');
      }
    } finally {
      setSaving(false);
    }
  };

  const saveRecord = async (data: Record<string, unknown>) => {
    if (!selectedApp || !selectedModule || !selectedTable) return;
    setSaving(true);
    try {
      const res = await fetch('/api/records', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: editingRecord?.id,
          appId: selectedApp.id,
          moduleId: selectedModule.id,
          tableId: selectedTable.id,
          data,
        }),
      }).then((r) => r.json());
      if (res.success) {
        await loadColumnsAndPreview(selectedTable.id);
        await loadAutomations();
        await refreshApps();
        setShowRecordModal(false);
        const autos: string[] = res.triggeredAutomations || [];
        toast(autos.length > 0 ? `Đã lưu & chạy tự động: ${autos.join(', ')}` : 'Đã lưu bản ghi!');
      } else {
        toast(res.message || res.error || 'Lỗi lưu bản ghi', 'error');
      }
    } finally {
      setSaving(false);
    }
  };

  const handleExportBlueprint = async () => {
    if (!selectedApp) return;
    try {
      const res = await fetch(`/api/apps/${encodeURIComponent(selectedApp.code)}/export`, {
        credentials: 'include',
      });
      const json = await res.json();
      if (!res.ok || !json.success) {
        toast(json.message || 'Lỗi xuất App Blueprint', 'error');
        return;
      }
      const blob = new Blob([JSON.stringify(json.data, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${selectedApp.code.toLowerCase()}-blueprint.json`;
      a.click();
      URL.revokeObjectURL(url);
      toast(`Đã xuất Blueprint ${selectedApp.code}.json!`);
    } catch {
      toast('Không thể xuất App Blueprint', 'error');
    }
  };

  const handleImportBlueprint = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    setSaving(true);
    try {
      const text = await file.text();
      const blueprint = JSON.parse(text);
      const res = await fetch('/api/apps/import-blueprint', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(blueprint),
      });
      const json = await res.json();
      if (!res.ok || !json.success) {
        toast(json.message || json.error || 'Lỗi nhập App Blueprint', 'error');
      } else {
        await refreshApps();
        await loadAllTables();
        setSelectedApp(json.data);
        toast(`Đã nhập App Blueprint: ${json.data.name} (${json.data.code})!`);
      }
    } catch {
      toast('File Blueprint JSON không hợp lệ', 'error');
    } finally {
      setSaving(false);
    }
  };

  const handleUpgradeBlueprint = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file || !selectedApp) return;
    setSaving(true);
    try {
      const text = await file.text();
      const blueprint = JSON.parse(text);
      const res = await fetch(`/api/apps/${encodeURIComponent(selectedApp.code)}/upgrade-blueprint`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(blueprint),
      });
      const json = await res.json();
      if (!res.ok || !json.success) {
        toast(json.message || 'Lỗi nâng cấp Blueprint', 'error');
      } else {
        await loadModules(selectedApp.id);
        await loadAllTables();
        await refreshApps();
        toast(`Đã nâng cấp ${selectedApp.code}: ${json.data.tablesUpserted} bảng, ${json.data.columnsUpserted} cột (giữ nguyên dữ liệu)!`);
      }
    } catch {
      toast('File Blueprint JSON không hợp lệ', 'error');
    } finally {
      setSaving(false);
    }
  };

  const filteredApps = useMemo(() => {
    const q = appQuery.trim().toLowerCase();
    if (!q) return apps;
    return apps.filter(
      (a) =>
        a.name.toLowerCase().includes(q) ||
        a.code.toLowerCase().includes(q) ||
        (a.department || '').toLowerCase().includes(q)
    );
  }, [apps, appQuery]);

  const tableModalInitial = useMemo<TableFormState>(() => (
    editingTable
      ? {
          id: editingTable.id,
          appId: editingTable.appId,
          moduleId: editingTable.moduleId,
          tableName: editingTable.tableName,
          displayName: editingTable.displayName,
          description: editingTable.description || '',
          primaryView: editingTable.primaryView,
          rowScope: editingTable.rowScope || 'ALL',
          approvalEnabled: Boolean(editingTable.approvalEnabled),
          approvalField: editingTable.approvalField || 'trang_thai_duyet',
          approverRoles: editingTable.approverRoles || [],
          publicFormEnabled: Boolean(editingTable.publicFormEnabled),
          publicFormExpiresAt: editingTable.publicFormExpiresAt || '',
          publicFormMaxSubmissions: editingTable.publicFormMaxSubmissions ?? '',
          formRules: editingTable.formRules || [],
          formLayout: editingTable.formLayout ?? null,
          orderIndex: editingTable.orderIndex,
        }
      : {
          id: '',
          appId: selectedApp?.id || '',
          moduleId: selectedModule?.id || '',
          tableName: '',
          displayName: '',
          description: '',
          primaryView: 'table',
          rowScope: 'ALL',
          approvalEnabled: false,
          approvalField: 'trang_thai_duyet',
          approverRoles: [],
          publicFormEnabled: false,
          publicFormExpiresAt: '',
          publicFormMaxSubmissions: '',
          orderIndex: tables.length + 1,
        }
  ), [editingTable, selectedApp?.id, selectedModule?.id, tables.length]);

  const columnModalInitial = useMemo<ColumnFormState>(() => (
    editingColumn
      ? {
          id: editingColumn.id,
          tableId: editingColumn.tableId,
          columnCode: editingColumn.columnCode,
          columnName: editingColumn.columnName,
          excelHeader: editingColumn.excelHeader,
          dataType: editingColumn.dataType,
          isRequired: editingColumn.isRequired,
          isIdentityKey: editingColumn.isIdentityKey,
          showInTable: editingColumn.showInTable,
          showInForm: editingColumn.showInForm,
          showInParentForm: editingColumn.showInParentForm ?? false,
          width: editingColumn.width,
          validationRegex: editingColumn.validationRegex || '',
          refCategoryCode: editingColumn.refCategoryCode || '',
          allowQuickCreate: Boolean(editingColumn.allowQuickCreate),
          refAppId: editingColumn.refAppId || '',
          refTableId: editingColumn.refTableId || '',
          refValueField: editingColumn.refValueField || '',
          refDisplayField: editingColumn.refDisplayField || '',
          lookupFilterField: editingColumn.lookupFilterField || '',
          lookupSourceField: editingColumn.lookupSourceField || '',
          isIndexed: Boolean(editingColumn.isIndexed),
          formula: editingColumn.formula || '',
          defaultValue: editingColumn.defaultValue || '',
          hiddenForRoles: editingColumn.hiddenForRoles || [],
          readOnlyForRoles: editingColumn.readOnlyForRoles || [],
          orderIndex: editingColumn.orderIndex,
        }
      : {
          id: '',
          tableId: selectedTable?.id || '',
          columnCode: '',
          columnName: '',
          excelHeader: '',
          dataType: 'STRING',
          isRequired: false,
          isIdentityKey: false,
          showInTable: true,
          showInForm: true,
          showInParentForm: false,
          width: 170,
          validationRegex: '',
          refCategoryCode: '',
          allowQuickCreate: false,
          refAppId: '',
          refTableId: '',
          refValueField: '',
          refDisplayField: '',
          formula: '',
          defaultValue: '',
          hiddenForRoles: [],
          readOnlyForRoles: [],
          orderIndex: columns.length + 1,
        }
  ), [editingColumn, selectedTable?.id, columns.length]);

  if (!canAccessStudio) {
    return (
      <div className="page-content">
        <EmptyState
          title="Khu vực dành riêng cho Quản trị & Thiết kế App"
          description={`Tài khoản "${currentUser?.fullName || currentUser?.username}" (${currentUser?.roleCode}) chưa được cấp quyền quản trị Studio, User hoặc Vai trò.`}
          icon={<LockRoundedIcon sx={{ fontSize: 28, color: '#d93025' }} />}
          action={
            <Link href="/" className="btn btn-primary">
              Về Cổng Ứng dụng
            </Link>
          }
        />
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title="App Builder Studio"
        subtitle="Thiết kế cấu trúc App • Phân hệ • Bảng động • Công thức & Liên kết chéo • Tài nguyên hệ sinh thái"
        icon={<DesignServicesRoundedIcon sx={{ fontSize: 24 }} />}
        breadcrumbs={[{ label: 'Cổng Ứng dụng', href: '/' }, { label: 'App Builder Studio' }]}
        actions={
          canStudio ? (
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <button
                type="button"
                className="btn btn-outline btn-sm"
                style={{ color: '#0f766e', borderColor: '#99f6e4' }}
                onClick={() => setShowUpgradeV2Modal(true)}
              >
                <RocketLaunchRoundedIcon sx={{ fontSize: 16 }} />
                Nâng cấp Apps 2.0
              </button>

              <button
                type="button"
                className="btn btn-outline btn-sm"
                style={{ color: '#7b1fa2', borderColor: '#d8b4fe' }}
                onClick={() => setShowAiModal(true)}
              >
                <AutoAwesomeRoundedIcon sx={{ fontSize: 16 }} />
                Sinh App AI
              </button>

              <div ref={blueprintMenuRef} style={{ position: 'relative' }}>
                <button
                  type="button"
                  className="btn btn-outline btn-sm"
                  onClick={() => setShowBlueprintMenu((v) => !v)}
                  aria-expanded={showBlueprintMenu}
                >
                  <TuneRoundedIcon sx={{ fontSize: 16 }} />
                  <span>Khởi tạo &amp; Blueprint</span>
                  <KeyboardArrowDownRoundedIcon sx={{ fontSize: 16 }} />
                </button>

                {showBlueprintMenu && (
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
                      zIndex: 1200,
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 2,
                    }}
                  >
                    <Link
                      href="/store"
                      className="utility-menu-item"
                      onClick={() => setShowBlueprintMenu(false)}
                      style={{ display: 'flex', alignItems: 'center', gap: 10, width: '100%', textAlign: 'left', padding: '9px 12px', borderRadius: 8, fontSize: 13, color: 'var(--text-primary)', textDecoration: 'none' }}
                    >
                      <StorefrontRoundedIcon sx={{ fontSize: 17, color: '#ea580c' }} />
                      <span>Mở Kho Apps Online (Tải &amp; Cài đặt)</span>
                    </Link>
                    <button
                      type="button"
                      className="utility-menu-item"
                      onClick={() => {
                        setShowBlueprintMenu(false);
                        setShowTemplateModal(true);
                      }}
                      style={{ display: 'flex', alignItems: 'center', gap: 10, width: '100%', textAlign: 'left', padding: '9px 12px', border: 'none', borderRadius: 8, background: 'transparent', cursor: 'pointer', fontSize: 13, color: 'var(--text-primary)' }}
                    >
                      <AutoAwesomeRoundedIcon sx={{ fontSize: 17, color: '#1a73e8' }} />
                      <span>Cài đặt App Mẫu (Template)</span>
                    </button>
                    <button
                      type="button"
                      className="utility-menu-item"
                      onClick={() => {
                        setShowBlueprintMenu(false);
                        setShowExcelModal(true);
                      }}
                      style={{ display: 'flex', alignItems: 'center', gap: 10, width: '100%', textAlign: 'left', padding: '9px 12px', border: 'none', borderRadius: 8, background: 'transparent', cursor: 'pointer', fontSize: 13, color: 'var(--text-primary)' }}
                    >
                      <CloudUploadRoundedIcon sx={{ fontSize: 17, color: '#00897b' }} />
                      <span>Sinh App từ file Excel</span>
                    </button>
                    <label
                      className="utility-menu-item"
                      style={{ display: 'flex', alignItems: 'center', gap: 10, width: '100%', padding: '9px 12px', borderRadius: 8, cursor: 'pointer', fontSize: 13, color: 'var(--text-primary)', margin: 0 }}
                    >
                      <UploadFileRoundedIcon sx={{ fontSize: 17, color: '#ea8600' }} />
                      <span>Nhập App Blueprint (.json)</span>
                      <input
                        type="file"
                        accept=".json,application/json"
                        onChange={(e) => {
                          setShowBlueprintMenu(false);
                          handleImportBlueprint(e);
                        }}
                        style={{ display: 'none' }}
                      />
                    </label>

                    {selectedApp && (
                      <>
                        <div style={{ height: 1, backgroundColor: 'var(--border-light)', margin: '4px 6px' }} />
                        <button
                          type="button"
                          className="utility-menu-item"
                          onClick={() => {
                            setShowBlueprintMenu(false);
                            handleExportBlueprint();
                          }}
                          style={{ display: 'flex', alignItems: 'center', gap: 10, width: '100%', textAlign: 'left', padding: '9px 12px', border: 'none', borderRadius: 8, background: 'transparent', cursor: 'pointer', fontSize: 13, color: 'var(--text-primary)' }}
                        >
                          <DownloadRoundedIcon sx={{ fontSize: 17, color: '#1e8e3e' }} />
                          <span>Xuất Blueprint {selectedApp.code}</span>
                        </button>
                        <a
                          href={`/api/store/export-package/${encodeURIComponent(selectedApp.code)}`}
                          className="utility-menu-item"
                          onClick={() => setShowBlueprintMenu(false)}
                          style={{ display: 'flex', alignItems: 'center', gap: 10, width: '100%', textAlign: 'left', padding: '9px 12px', borderRadius: 8, fontSize: 13, color: 'var(--text-primary)', textDecoration: 'none' }}
                        >
                          <DownloadRoundedIcon sx={{ fontSize: 17, color: '#1a73e8' }} />
                          <span>Đóng gói Store {selectedApp.code} (.smartapp.json)</span>
                        </a>
                        <label
                          className="utility-menu-item"
                          title="Cập nhật thêm cột/bảng từ Blueprint vào App hiện tại mà không mất dữ liệu"
                          style={{ display: 'flex', alignItems: 'center', gap: 10, width: '100%', padding: '9px 12px', borderRadius: 8, cursor: 'pointer', fontSize: 13, color: 'var(--text-primary)', margin: 0 }}
                        >
                          <UploadFileRoundedIcon sx={{ fontSize: 17, color: '#7b1fa2' }} />
                          <span>Nâng cấp {selectedApp.code} từ Blueprint</span>
                          <input
                            type="file"
                            accept=".json,application/json"
                            onChange={(e) => {
                              setShowBlueprintMenu(false);
                              handleUpgradeBlueprint(e);
                            }}
                            style={{ display: 'none' }}
                          />
                        </label>
                      </>
                    )}
                  </div>
                )}
              </div>

              {selectedApp && selectedApp.publishStatus === 'DRAFT' && (
                <Link href={`/apps/${selectedApp.code}?previewDraft=true`} className="btn btn-outline btn-sm" title="Xem thử cấu trúc Nháp trước khi phát hành">
                  <OpenInNewRoundedIcon sx={{ fontSize: 16 }} />
                  Xem thử Nháp
                </Link>
              )}

              {selectedApp && (
                <Link href={`/apps/${selectedApp.code}`} className="btn btn-outline btn-sm">
                  <OpenInNewRoundedIcon sx={{ fontSize: 16 }} />
                  Chạy App {selectedApp.code}
                </Link>
              )}

              <button
                type="button"
                className="btn btn-primary btn-sm"
                onClick={() => {
                  setEditingApp(null);
                  setShowAppModal(true);
                }}
              >
                <AddRoundedIcon sx={{ fontSize: 17 }} />
                Tạo App Mới
              </button>
            </div>
          ) : undefined
        }
      />

      <div className="page-content">
        {/* Top-Level Workspace Switcher + Summary Badges */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 12 }}>
          <div className="segmented-tabs">
            {(canStudio || canManageAuto || canManageInteg) && (
              <button
                type="button"
                className={`segmented-tab ${workspace === 'builder' ? 'active' : ''}`}
                onClick={() => setWorkspace('builder')}
              >
                <LayersRoundedIcon sx={{ fontSize: 16 }} />
                Thiết kế Ứng dụng &amp; Dữ liệu ({apps.length})
              </button>
            )}
            {(canStudio || canManageRoles || canManageUsers || canViewAudit) && (
              <button
                type="button"
                className={`segmented-tab ${workspace === 'platform' ? 'active' : ''}`}
                onClick={() => setWorkspace('platform')}
              >
                <ShieldRoundedIcon sx={{ fontSize: 16 }} />
                Danh mục Dùng chung ({categories.length})
              </button>
            )}
          </div>

          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
            <span className="badge badge-info">{apps.length} Ứng dụng</span>
            <span className="badge" style={{ backgroundColor: 'var(--bg-secondary)', color: 'var(--text-secondary)' }}>
              {allTables.length} Bảng động
            </span>
            {(canManageUsers || canManageRoles || canViewAudit) && (
              <Link href="/admin" className="btn btn-outline btn-xs" title="Mở Trung tâm Quản trị Người dùng, Vai trò, Quy trình & Kiểm toán">
                Quản trị &amp; Phân quyền ({users.length} Users • {roles.length} Roles) ↗
              </Link>
            )}
          </div>
        </div>

        {/* WORKSPACE 1: APP BUILDER */}
        {workspace === 'builder' && (
          <>
            {/* Tier 1: App Selector */}
            <div className="card" style={{ padding: 16 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12, flexWrap: 'wrap', gap: 10 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <strong style={{ fontSize: 13.5, color: 'var(--text-primary)' }}>1. Chọn Ứng dụng cần thiết kế</strong>
                  <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>({filteredApps.length} ứng dụng)</span>
                </div>

                {apps.length > 4 && (
                  <div style={{ position: 'relative', width: 240 }}>
                    <SearchRoundedIcon sx={{ fontSize: 16, color: 'var(--text-muted)', position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)' }} />
                    <input
                      className="form-input"
                      style={{ height: 32, paddingLeft: 30, fontSize: 12.5 }}
                      placeholder="Lọc theo tên hoặc mã App..."
                      value={appQuery}
                      onChange={(e) => setAppQuery(e.target.value)}
                    />
                  </div>
                )}
              </div>

              <div className="studio-app-grid">
                {filteredApps.map((app) => {
                  const active = selectedApp?.id === app.id;
                  return (
                    <div
                      key={app.id}
                      onClick={() => setSelectedApp(app)}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 10,
                        padding: '10px 12px',
                        borderRadius: 12,
                        border: `1.5px solid ${active ? app.color : 'var(--border-subtle)'}`,
                        backgroundColor: active ? app.bgColor : 'var(--bg-surface)',
                        cursor: 'pointer',
                        transition: 'all 0.15s ease',
                        minWidth: 0,
                      }}
                    >
                      <div
                        style={{
                          width: 36,
                          height: 36,
                          borderRadius: 10,
                          backgroundColor: app.color,
                          color: '#fff',
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          flexShrink: 0,
                        }}
                      >
                        <ClusterIcon icon={app.icon} fontSize={19} />
                      </div>
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={{ fontSize: 13.5, fontWeight: 700, color: active ? '#202124' : 'var(--text-primary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                          {app.name}
                        </div>
                        <div style={{ fontSize: 11, color: active ? '#3c4043' : 'var(--text-muted)' }}>
                          {app.code} • v{app.version || '1.0.0'} • {app.moduleCount} phân hệ • {app.tableCount} bảng
                        </div>
                      </div>
                      <div style={{ display: 'flex', gap: 4, flexShrink: 0 }}>
                        {canManageRoles && (
                          <button
                            type="button"
                            className="btn btn-outline btn-xs"
                            onClick={(e) => {
                              e.stopPropagation();
                              setAppPermTarget(app);
                            }}
                            title={`Phân quyền vai trò vào App ${app.code}`}
                          >
                            <ShieldRoundedIcon sx={{ fontSize: 13, color: '#00897b' }} />
                          </button>
                        )}
                        {canStudio && (
                          <>
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              onClick={(e) => {
                                e.stopPropagation();
                                setSelectedApp(app);
                                setShowPublishModal(true);
                              }}
                              title="Kiểm tra & Phát hành phiên bản App"
                            >
                              {app.publishStatus === 'DRAFT' ? 'Nháp • Phát hành' : `v${app.publishedVersion || 1}`}
                            </button>
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              onClick={(e) => {
                                e.stopPropagation();
                                setEditingApp(app);
                                setShowAppModal(true);
                              }}
                              title="Sửa thông tin App"
                            >
                              <EditRoundedIcon sx={{ fontSize: 13 }} />
                            </button>
                          </>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Tier 2 + 3: Master-Detail Split */}
            {selectedApp && (
              <div className="studio-split-grid">
                {/* Left Rail: Modules & Tables */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                  <div className="card" style={{ padding: 16 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
                      <strong style={{ fontSize: 13 }}>2. Phân hệ ({modules.length})</strong>
                      {canStudio && (
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          onClick={() => {
                            setEditingModule(null);
                            setShowModuleModal(true);
                          }}
                        >
                          <AddRoundedIcon sx={{ fontSize: 14 }} /> Thêm
                        </button>
                      )}
                    </div>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                      {modules.map((m) => {
                        const active = selectedModule?.id === m.id;
                        return (
                          <div
                            key={m.id}
                            onClick={() => setSelectedModule(m)}
                            style={{
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'space-between',
                              gap: 8,
                              padding: '9px 10px',
                              borderRadius: 10,
                              border: `1px solid ${active ? selectedApp.color : 'var(--border-light)'}`,
                              borderLeftWidth: active ? 4 : 1,
                              backgroundColor: active ? selectedApp.bgColor : 'var(--bg-surface)',
                              cursor: 'pointer',
                            }}
                          >
                            <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
                              <ClusterIcon icon={m.icon} fontSize={17} color={active ? selectedApp.color : 'var(--text-muted)'} />
                              <div style={{ minWidth: 0 }}>
                                <div style={{ fontSize: 13, fontWeight: active ? 700 : 500, color: active ? '#202124' : 'var(--text-primary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                  {m.name}
                                </div>
                                <div style={{ fontSize: 11, color: active ? '#3c4043' : 'var(--text-muted)' }}>{m.tableCount} bảng</div>
                              </div>
                            </div>
                            {canStudio && (
                              <button
                                type="button"
                                className="btn btn-outline btn-xs"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  setEditingModule(m);
                                  setShowModuleModal(true);
                                }}
                                title="Sửa phân hệ"
                              >
                                <EditRoundedIcon sx={{ fontSize: 12 }} />
                              </button>
                            )}
                          </div>
                        );
                      })}
                      {modules.length === 0 && (
                        <div style={{ fontSize: 12.5, color: 'var(--text-muted)', padding: 8 }}>Chưa có phân hệ.</div>
                      )}
                    </div>
                  </div>

                  <div className="card" style={{ padding: 16 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
                      <strong style={{ fontSize: 13 }}>3. Bảng động ({tables.length})</strong>
                      {selectedModule && canStudio && (
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          onClick={() => {
                            setEditingTable(null);
                            setShowTableModal(true);
                          }}
                        >
                          <AddRoundedIcon sx={{ fontSize: 14 }} /> Thêm
                        </button>
                      )}
                    </div>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                      {tables.map((t) => {
                        const active = selectedTable?.id === t.id;
                        return (
                          <div
                            key={t.id}
                            onClick={() => setSelectedTable(t)}
                            style={{
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'space-between',
                              gap: 8,
                              padding: '9px 10px',
                              borderRadius: 10,
                              border: `1px solid ${active ? 'var(--accent-primary)' : 'var(--border-light)'}`,
                              borderLeftWidth: active ? 4 : 1,
                              backgroundColor: active ? 'var(--accent-primary-container)' : 'var(--bg-surface)',
                              cursor: 'pointer',
                            }}
                          >
                            <div style={{ minWidth: 0, flex: 1 }}>
                              <div style={{ fontSize: 13, fontWeight: active ? 700 : 500, color: active ? '#1967d2' : 'var(--text-primary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                {t.displayName}
                              </div>
                              <div style={{ fontSize: 11, color: active ? '#3c4043' : 'var(--text-muted)', display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap', marginTop: 2 }}>
                                <code>{t.tableName}</code>
                                <span>• {t.columnCount} cột</span>
                                {t.rowScope === 'OWNER_ONLY' && (
                                  <span style={{ color: '#00695c', fontWeight: 700 }}>• Cá nhân</span>
                                )}
                                {t.rowScope === 'DEPARTMENT' && (
                                  <span style={{ color: '#1d4ed8', fontWeight: 700 }}>• Phòng ban</span>
                                )}
                              </div>
                            </div>
                            {canStudio && (
                              <button
                                type="button"
                                className="btn btn-outline btn-xs"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  setEditingTable(t);
                                  setShowTableModal(true);
                                }}
                                title="Sửa cấu hình bảng"
                              >
                                <EditRoundedIcon sx={{ fontSize: 12 }} />
                              </button>
                            )}
                          </div>
                        );
                      })}
                      {tables.length === 0 && (
                        <div style={{ fontSize: 12.5, color: 'var(--text-muted)', padding: 8 }}>Chưa có bảng động.</div>
                      )}
                    </div>
                  </div>
                </div>

                {/* Right Design Canvas */}
                <div className="card" style={{ padding: 20, display: 'flex', flexDirection: 'column', gap: 16, minWidth: 0 }}>
                  {/* Selected Table Context Header */}
                  {selectedTable && (
                    <div
                      style={{
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        flexWrap: 'wrap',
                        gap: 10,
                        padding: '12px 14px',
                        borderRadius: 12,
                        backgroundColor: 'var(--bg-primary)',
                        border: '1px solid var(--border-light)',
                      }}
                    >
                      <div style={{ minWidth: 0 }}>
                        <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginBottom: 2 }}>
                          {selectedApp.name} ({selectedApp.code}) › {selectedModule?.name || 'Phân hệ'}
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                          <strong style={{ fontSize: 15, color: 'var(--text-primary)' }}>{selectedTable.displayName}</strong>
                          <code style={{ fontSize: 11.5 }}>{selectedTable.tableName}</code>
                          <span className="badge badge-info">View: {selectedTable.primaryView}</span>
                          <span
                            className="badge"
                            style={{
                              backgroundColor: selectedTable.rowScope === 'OWNER_ONLY' ? '#e0f2f1' : selectedTable.rowScope === 'DEPARTMENT' ? '#dbeafe' : '#f1f3f4',
                              color: selectedTable.rowScope === 'OWNER_ONLY' ? '#00695c' : selectedTable.rowScope === 'DEPARTMENT' ? '#1d4ed8' : '#5f6368',
                            }}
                          >
                            Phạm vi: {selectedTable.rowScope === 'OWNER_ONLY' ? 'Cá nhân (OWNER_ONLY)' : selectedTable.rowScope === 'DEPARTMENT' ? 'Phòng ban (DEPARTMENT)' : 'Toàn bảng (ALL)'}
                          </span>
                          {selectedTable.approvalEnabled && (
                            <span className="badge badge-warning">Bật Phê duyệt</span>
                          )}
                          {selectedTable.publicFormEnabled && (
                            <Link href={`/forms/${selectedTable.id}`} target="_blank" className="badge badge-success" style={{ textDecoration: 'none' }}>
                              Public Form ↗
                            </Link>
                          )}
                        </div>
                      </div>

                      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                        {canManageRoles && (
                          <button
                            type="button"
                            className="btn btn-outline btn-xs"
                            onClick={() => setAppPermTarget(selectedApp)}
                          >
                            <ShieldRoundedIcon sx={{ fontSize: 13, color: '#00897b' }} />
                            Phân quyền App
                          </button>
                        )}
                        {canStudio && (
                          <>
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              onClick={() => setShowPrintDesignerModal(true)}
                            >
                              Mẫu in
                            </button>
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              onClick={() => {
                                setEditingTable(selectedTable);
                                setShowTableModal(true);
                              }}
                            >
                              <EditRoundedIcon sx={{ fontSize: 13 }} />
                              Sửa Bảng
                            </button>
                          </>
                        )}
                      </div>
                    </div>
                  )}

                  {/* Builder Sub-Tabs + Primary Contextual Action */}
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
                    <div className="segmented-tabs subtle">
                      {canStudio && (
                        <>
                          <button
                            type="button"
                            className={`segmented-tab ${builderTab === 'columns' ? 'active' : ''}`}
                            onClick={() => setBuilderTab('columns')}
                          >
                            <ViewColumnRoundedIcon sx={{ fontSize: 15 }} />
                            Cấu trúc Cột ({columns.length})
                          </button>
                          <button
                            type="button"
                            className={`segmented-tab ${builderTab === 'preview' ? 'active' : ''}`}
                            onClick={() => setBuilderTab('preview')}
                          >
                            <TableViewRoundedIcon sx={{ fontSize: 15 }} />
                            Xem thử Dữ liệu ({records.length})
                          </button>
                          {(studioAdvancedMode || !canStudio) && (
                            <button
                              type="button"
                              className={`segmented-tab ${builderTab === 'compile' ? 'active' : ''}`}
                              onClick={() => setBuilderTab('compile')}
                            >
                              <CodeRoundedIcon sx={{ fontSize: 15 }} />
                              Biên dịch Schema
                            </button>
                          )}
                        </>
                      )}
                      {canManageAuto && (studioAdvancedMode || !canStudio) && (
                        <button
                          type="button"
                          className={`segmented-tab ${builderTab === 'automations' ? 'active' : ''}`}
                          onClick={() => setBuilderTab('automations')}
                        >
                          <BoltRoundedIcon sx={{ fontSize: 15 }} />
                          Tự động hóa ({automations.length})
                        </button>
                      )}
                      {canManageInteg && (
                        <>
                          {(studioAdvancedMode || !canStudio) && (
                            <>
                              <button
                                type="button"
                                className={`segmented-tab ${builderTab === 'plugins' ? 'active' : ''}`}
                                onClick={() => setBuilderTab('plugins')}
                              >
                                <ExtensionRoundedIcon sx={{ fontSize: 15 }} />
                                Plugin
                              </button>
                              <button
                                type="button"
                                className={`segmented-tab ${builderTab === 'integrations' ? 'active' : ''}`}
                                onClick={() => setBuilderTab('integrations')}
                              >
                                <ApiRoundedIcon sx={{ fontSize: 15 }} />
                                API
                              </button>
                            </>
                          )}
                          <button
                            type="button"
                            className={`segmented-tab ${builderTab === 'publicForms' ? 'active' : ''}`}
                            onClick={() => setBuilderTab('publicForms')}
                          >
                            <OpenInNewRoundedIcon sx={{ fontSize: 15 }} />
                            Public Form ({allTables.filter((t) => t.publicFormEnabled).length})
                          </button>
                        </>
                      )}
                      {canStudio && (
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          style={{ marginLeft: 'auto', whiteSpace: 'nowrap' }}
                          title={studioAdvancedMode ? 'Chuyển về chế độ Thiết kế Cơ bản' : 'Mở khóa tab Nâng cao (Schema, Automation, Plugin, API)'}
                          onClick={() => {
                            const next = !studioAdvancedMode;
                            setStudioAdvancedMode(next);
                            localStorage.setItem('smartapps_studio_advanced', String(next));
                            if (!next && ['compile', 'automations', 'plugins', 'integrations'].includes(builderTab)) {
                              setBuilderTab('columns');
                            }
                          }}
                        >
                          {studioAdvancedMode ? 'Chế độ: Nâng cao' : 'Chế độ: Cơ bản'}
                        </button>
                      )}
                    </div>

                    {builderTab === 'columns' && selectedTable && canStudio && (
                      <button
                        type="button"
                        className="btn btn-primary btn-sm"
                        onClick={() => {
                          setEditingColumn(null);
                          setShowColumnModal(true);
                        }}
                      >
                        <AddRoundedIcon sx={{ fontSize: 16 }} />
                        Thêm Cột Động
                      </button>
                    )}

                    {builderTab === 'automations' && canManageAuto && (
                      <button
                        type="button"
                        className="btn btn-primary btn-sm"
                        onClick={() => {
                          setEditingAuto(null);
                          setShowAutoModal(true);
                        }}
                      >
                        <AddRoundedIcon sx={{ fontSize: 16 }} />
                        Tạo Quy tắc Tự động
                      </button>
                    )}
                  </div>

                  {/* Builder Tab Content */}
                  {!selectedTable && !['automations', 'integrations'].includes(builderTab) ? (
                    <EmptyState
                      title="Chọn hoặc tạo một Bảng động"
                      description="Hãy chọn Phân hệ và Bảng động ở cột bên trái để thiết kế cột dữ liệu và giao diện."
                      compact
                    />
                  ) : builderTab === 'columns' ? (
                    columns.length === 0 ? (
                      <EmptyState
                        title="Bảng này chưa có cột dữ liệu nào"
                        description="Nhấn 'Thêm Cột Động' để khai báo các trường thông tin, công thức tự động hoặc liên kết LOOKUP sang App khác."
                        compact
                        action={
                          canStudio ? (
                            <button
                              type="button"
                              className="btn btn-primary btn-sm"
                              onClick={() => {
                                setEditingColumn(null);
                                setShowColumnModal(true);
                              }}
                            >
                              <AddRoundedIcon sx={{ fontSize: 16 }} />
                              Thêm Cột Đầu Tiên
                            </button>
                          ) : undefined
                        }
                      />
                    ) : (
                      <div className="table-responsive">
                        <table className="data-table">
                          <thead>
                            <tr>
                              <th style={{ width: 42 }}>#</th>
                              <th>Mã trường</th>
                              <th>Tên hiển thị</th>
                              <th>Kiểu</th>
                              <th>Ràng buộc / Công thức</th>
                              <th>Liên kết</th>
                              <th>Bảo mật FLS</th>
                              <th style={{ textAlign: 'right' }}>Thao tác</th>
                            </tr>
                          </thead>
                          <tbody>
                            {columns.map((col, idx) => (
                              <tr key={col.id}>
                                <td>{idx + 1}</td>
                                <td><code>{col.columnCode}</code></td>
                                <td style={{ fontWeight: 600 }}>
                                  {col.columnName} {col.isRequired && <span style={{ color: '#d93025' }}>*</span>}
                                </td>
                                <td>
                                  {(() => {
                                    const meta = getColumnTypeMeta(col.dataType);
                                    return (
                                      <span
                                        className="badge"
                                        title={meta.description || meta.label}
                                        style={{
                                          backgroundColor: `color-mix(in srgb, ${meta.accent} 14%, var(--bg-surface))`,
                                          color: meta.accent,
                                          border: `1px solid color-mix(in srgb, ${meta.accent} 32%, transparent)`,
                                          display: 'inline-flex',
                                          alignItems: 'center',
                                          gap: 5,
                                          fontWeight: 700,
                                        }}
                                      >
                                        <span>{meta.shortBadge}</span>
                                        <span>{col.dataType}</span>
                                      </span>
                                    );
                                  })()}
                                </td>
                                <td>
                                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                                    {col.isIdentityKey && <span className="badge badge-warning">Key</span>}
                                    {col.formula && (
                                      <span className="badge badge-success">
                                        <FunctionsRoundedIcon sx={{ fontSize: 12 }} /> {col.formula}
                                      </span>
                                    )}
                                    {col.validationRegex && <code>{col.validationRegex}</code>}
                                    {!col.isIdentityKey && !col.formula && !col.validationRegex && (
                                      <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>—</span>
                                    )}
                                  </div>
                                </td>
                                <td>
                                  {col.refTableId ? (
                                    <span className="badge badge-info">
                                      <LinkRoundedIcon sx={{ fontSize: 12 }} /> {col.refTableId}.{col.refValueField}
                                    </span>
                                  ) : col.refCategoryCode ? (
                                    <code>{col.refCategoryCode}</code>
                                  ) : (
                                    <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>—</span>
                                  )}
                                </td>
                                <td>
                                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                                    {col.hiddenForRoles && col.hiddenForRoles.length > 0 && (
                                      <span className="badge badge-danger" title={`Ẩn cột này với vai trò: ${col.hiddenForRoles.join(', ')}`}>
                                        <VisibilityOffRoundedIcon sx={{ fontSize: 12 }} />
                                        Ẩn: {col.hiddenForRoles.join(', ')}
                                      </span>
                                    )}
                                    {col.readOnlyForRoles && col.readOnlyForRoles.length > 0 && (
                                      <span className="badge badge-warning" title={`Chỉ đọc với vai trò: ${col.readOnlyForRoles.join(', ')}`}>
                                        Chỉ đọc: {col.readOnlyForRoles.join(', ')}
                                      </span>
                                    )}
                                    {(!col.hiddenForRoles || col.hiddenForRoles.length === 0) && (!col.readOnlyForRoles || col.readOnlyForRoles.length === 0) && (
                                      <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>Công khai</span>
                                    )}
                                  </div>
                                </td>
                                <td style={{ textAlign: 'right' }}>
                                  <div style={{ display: 'inline-flex', gap: 6 }}>
                                    <button
                                      type="button"
                                      className="btn btn-outline btn-xs"
                                      onClick={() => {
                                        setEditingColumn(col);
                                        setShowColumnModal(true);
                                      }}
                                      title="Sửa cột"
                                    >
                                      <EditRoundedIcon sx={{ fontSize: 13 }} /> Sửa
                                    </button>
                                    <button
                                      type="button"
                                      className="btn btn-outline btn-xs"
                                      style={{ color: '#d93025' }}
                                      onClick={() =>
                                        setConfirm({
                                          open: true,
                                          title: 'Xóa cột động',
                                          message: `Xóa cột "${col.columnName}" (${col.columnCode})?`,
                                          onConfirm: async () => {
                                            setConfirm((c) => ({ ...c, open: false }));
                                            await fetch(`/api/columns/${col.id}`, { method: 'DELETE' });
                                            if (selectedTable) await loadColumnsAndPreview(selectedTable.id);
                                            toast('Đã xóa cột động');
                                          },
                                        })
                                      }
                                      title="Xóa cột"
                                    >
                                      <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} /> Xóa
                                    </button>
                                  </div>
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    )
                  ) : builderTab === 'preview' && selectedTable ? (
                    <DynamicDataTable
                      title={`Xem thử: ${selectedTable.displayName}`}
                      subtitle="Sửa trực tiếp trên ô hoặc nhấn Thêm mới để kiểm tra Form động"
                      columns={columns}
                      records={records}
                      storageKey={`studio_${selectedTable.id}`}
                      headerActions={
                        <button
                          type="button"
                          className="btn btn-primary btn-sm"
                          onClick={() => {
                            setEditingRecord(null);
                            setShowRecordModal(true);
                          }}
                        >
                          <AddRoundedIcon sx={{ fontSize: 16 }} /> Thêm bản ghi thử
                        </button>
                      }
                      onEditRecord={(rec) => {
                        setEditingRecord(rec);
                        setShowRecordModal(true);
                      }}
                      onDirectSave={async (rowId, field, newVal) => {
                        const res = await fetch(`/api/records/${rowId}/cell`, {
                          method: 'PATCH',
                          headers: { 'Content-Type': 'application/json' },
                          body: JSON.stringify({ field, value: newVal }),
                        }).then((r) => r.json());
                        if (res.success) {
                          await loadColumnsAndPreview(selectedTable.id);
                          await loadAutomations();
                          toast('Đã lưu trực tiếp trên ô!');
                          return true;
                        }
                        return false;
                      }}
                    />
                  ) : builderTab === 'compile' ? (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                      <div style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
                        Engine biên dịch: <span className="badge badge-info">{compiledSchema?.engine || 'loading...'}</span>
                      </div>
                      <div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
                          <div className="form-label" style={{ marginBottom: 0 }}>PostgreSQL DDL (Sinh tự động từ metadata)</div>
                          {compiledSchema?.sqlDdl && (
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              onClick={() => copyText(compiledSchema.sqlDdl!, 'SQL DDL')}
                            >
                              <ContentCopyRoundedIcon sx={{ fontSize: 13 }} /> Sao chép SQL
                            </button>
                          )}
                        </div>
                        <pre className="code-box">{compiledSchema?.sqlDdl || 'Đang biên dịch...'}</pre>
                      </div>
                      <div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
                          <div className="form-label" style={{ marginBottom: 0 }}>TypeScript Interface (Dùng cho Next.js App)</div>
                          {compiledSchema?.tsInterface && (
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              onClick={() => copyText(compiledSchema.tsInterface!, 'TypeScript Interface')}
                            >
                              <ContentCopyRoundedIcon sx={{ fontSize: 13 }} /> Sao chép TS
                            </button>
                          )}
                        </div>
                        <pre className="code-box">{compiledSchema?.tsInterface || 'Đang biên dịch...'}</pre>
                      </div>
                    </div>
                  ) : builderTab === 'automations' ? (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
                      <div>
                        <h4 style={{ fontSize: 14, fontWeight: 700, marginBottom: 10 }}>Quy tắc Tự động hóa Liên App</h4>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                          {automations.map((a) => (
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
                                <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4 }}>
                                  Trigger: <code>{a.triggerEvent}</code> trên <code>{a.sourceTableId}</code>
                                  {a.conditionField && <> nếu <code>{a.conditionField} == {a.conditionValue}</code></>}
                                  {' → '}<strong>{
                                    ({
                                      CREATE_USER: 'Tạo User hệ thống',
                                      UPDATE_USER_STATUS: 'Khóa / Mở khóa User',
                                      CREATE_TASK: 'Giao việc (Tasks)',
                                      CREATE_RECORD: 'Tạo bản ghi đích',
                                      UPDATE_RECORD: 'Cập nhật bảng đích',
                                      UPDATE_SELF: 'Tự cập nhật nguồn',
                                      SEND_NOTIFICATION: 'Gửi Thông báo',
                                      CALL_WEBHOOK: 'Gọi Webhook',
                                      RUN_PLUGIN: 'Chạy Plugin Tích hợp',
                                    } as Record<string, string>)[a.actionType] || a.actionType
                                  }</strong>
                                  {a.targetTableId && <> tại <code>{a.targetTableId}</code></>}
                                </div>
                              </div>
                              <div style={{ display: 'flex', gap: 8 }}>
                                <button
                                  type="button"
                                  className="btn btn-outline btn-xs"
                                  onClick={() => {
                                    setEditingAuto(a);
                                    setShowAutoModal(true);
                                  }}
                                >
                                  Sửa
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-outline btn-xs"
                                  onClick={async () => {
                                    const res = await fetch(`/api/automations/${a.id}/test`, {
                                      method: 'POST',
                                      credentials: 'include',
                                    }).then((r) => r.json());
                                    if (res.success && res.data) {
                                      toast(res.data.reason || 'Đã chạy thử quy tắc!', res.data.matched ? 'success' : 'info');
                                    }
                                  }}
                                  title="Chạy thử không ghi DB trên bản ghi mới nhất"
                                >
                                  Chạy thử
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-outline btn-xs"
                                  onClick={async () => {
                                    await fetch(`/api/automations/${a.id}/toggle`, {
                                      method: 'PATCH',
                                      headers: { 'Content-Type': 'application/json' },
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
                                  onClick={async () => {
                                    await fetch(`/api/automations/${a.id}`, { method: 'DELETE' });
                                    await loadAutomations();
                                  }}
                                >
                                  <DeleteOutlineRoundedIcon sx={{ fontSize: 14 }} />
                                </button>
                              </div>
                            </div>
                          ))}
                        </div>
                      </div>

                      <div>
                        <h4 style={{ fontSize: 14, fontWeight: 700, marginBottom: 8 }}>Lịch sử Thực thi Tự động ({autoLogs.length})</h4>
                        {autoLogs.length === 0 ? (
                          <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                            Chưa có lượt chạy nào. Hãy thử thêm Nhân viên mới ở App <strong>HRM</strong> để xem Automation tự sinh Ticket bên App <strong>OPS</strong>!
                          </div>
                        ) : (
                          <div className="table-responsive">
                            <table className="data-table">
                              <thead>
                                <tr><th>Quy tắc</th><th>Chi tiết</th><th>Thời gian</th></tr>
                              </thead>
                              <tbody>
                                {autoLogs.map((l) => (
                                  <tr key={l.id}>
                                    <td style={{ fontWeight: 600 }}>{l.automationName || l.automationId}</td>
                                    <td>{l.message}</td>
                                    <td style={{ fontSize: 12, color: 'var(--text-muted)' }}>{new Date(l.createdAt).toLocaleString('vi-VN')}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        )}
                      </div>
                      <OutboxMonitorPanel toast={toast} />
                    </div>
                  ) : builderTab === 'plugins' ? (
                    <AppPluginBindingsPanel app={selectedApp} tables={tables} toast={toast} />
                  ) : builderTab === 'publicForms' ? (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                      <h4 style={{ fontSize: 14, fontWeight: 700, margin: 0 }}>Biểu mẫu Công khai (Public Forms đang bật)</h4>
                      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10 }}>
                        {allTables.filter((t) => t.publicFormEnabled).map((t) => (
                          <div key={t.id} className="card" style={{ padding: '10px 14px', display: 'flex', alignItems: 'center', gap: 10 }}>
                            <div>
                              <div style={{ fontSize: 13, fontWeight: 700 }}>{t.displayName}</div>
                              <code style={{ fontSize: 11.5 }}>/forms/{t.id}</code>
                            </div>
                            <Link href={`/forms/${t.id}`} target="_blank" className="btn btn-outline btn-xs">
                              Mở Form
                            </Link>
                          </div>
                        ))}
                        {allTables.filter((t) => t.publicFormEnabled).length === 0 && (
                          <span style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                            Chưa có bảng nào bật Public Form. Hãy sửa bảng động và tích chọn &ldquo;Mở Biểu mẫu Công khai&rdquo;.
                          </span>
                        )}
                      </div>
                    </div>
                  ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
                      <div style={{ padding: 14, borderRadius: 12, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-light)' }}>
                        <h4 style={{ fontSize: 14, fontWeight: 700, marginBottom: 8 }}>
                          Tạo Inbound API Key (Kết nối ERP / Máy chấm công / Webhook ngoài)
                        </h4>
                        <form
                          onSubmit={async (e) => {
                            e.preventDefault();
                            if (!newKeyName.trim() || !selectedApp) return;
                            const res = await fetch('/api/integrations/keys', {
                              method: 'POST',
                              headers: { 'Content-Type': 'application/json' },
                              credentials: 'include',
                              body: JSON.stringify({
                                name: newKeyName.trim(),
                                appId: selectedApp.id,
                                tableId: newKeyTableId || null,
                                scopes: ['records:read', 'records:write'],
                              }),
                            }).then((r) => r.json());
                            if (res.success && res.data) {
                              setCreatedRawKey(res.data.rawKey);
                              setNewKeyName('');
                              await loadApiKeys();
                              toast('Đã cấp API Key mới! Hãy sao chép ngay.');
                            }
                          }}
                          style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}
                        >
                          <input
                            className="form-input"
                            style={{ maxWidth: 260 }}
                            placeholder="Tên kết nối (VD: ERP Kế toán)"
                            value={newKeyName}
                            onChange={(e) => setNewKeyName(e.target.value)}
                          />
                          <select
                            className="form-select"
                            style={{ maxWidth: 260 }}
                            value={newKeyTableId}
                            onChange={(e) => setNewKeyTableId(e.target.value)}
                          >
                            <option value="">Toàn bộ bảng thuộc {selectedApp?.code || 'App'}</option>
                            {tables.map((t) => (
                              <option key={t.id} value={t.id}>Chỉ bảng: {t.displayName}</option>
                            ))}
                          </select>
                          <button type="submit" className="btn btn-primary btn-sm" disabled={!selectedApp}>
                            Cấp API Key
                          </button>
                        </form>

                        {createdRawKey && (
                          <div style={{ marginTop: 10, padding: 10, borderRadius: 10, backgroundColor: '#e6f4ea', border: '1px solid #a8dab5' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                              <div style={{ fontSize: 12, fontWeight: 700, color: '#137333' }}>
                                API Key vừa cấp (chỉ hiển thị 1 lần): <code>{createdRawKey}</code>
                              </div>
                              <button type="button" className="btn btn-outline btn-xs" onClick={() => copyText(createdRawKey, 'API Key')}>
                                <ContentCopyRoundedIcon sx={{ fontSize: 13 }} /> Sao chép Key
                              </button>
                            </div>
                            <div style={{ fontSize: 11.5, color: '#3c4043', marginTop: 4 }}>
                              Ví dụ gọi ngoài: <code>POST /api/external/tables/{newKeyTableId || tables[0]?.id || '<tableId>'}/records</code> với header <code>X-Api-Key</code>.
                            </div>
                          </div>
                        )}
                      </div>

                      <div>
                        <h4 style={{ fontSize: 14, fontWeight: 700, marginBottom: 8 }}>Danh sách API Keys ({apiKeys.length})</h4>
                        <div className="table-responsive">
                          <table className="data-table">
                            <thead>
                              <tr><th>Tên kết nối</th><th>Key Prefix</th><th>App</th><th>Phạm vi</th><th>Lần dùng cuối</th><th>Thao tác</th></tr>
                            </thead>
                            <tbody>
                              {apiKeys.map((k) => (
                                <tr key={k.id}>
                                  <td style={{ fontWeight: 600 }}>{k.name}</td>
                                  <td><code>{k.keyPrefix}</code></td>
                                  <td><span className="badge badge-info">{k.appCode || k.appId}</span></td>
                                  <td style={{ fontSize: 12 }}>{k.tableId || 'All tables in App'}</td>
                                  <td style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                                    {k.lastUsedAt ? new Date(k.lastUsedAt).toLocaleString('vi-VN') : 'Chưa gọi'}
                                  </td>
                                  <td>
                                    <button
                                      type="button"
                                      className="btn btn-outline btn-xs"
                                      style={{ color: '#d93025' }}
                                      onClick={async () => {
                                        await fetch(`/api/integrations/keys/${k.id}`, { method: 'DELETE', credentials: 'include' });
                                        await loadApiKeys();
                                        toast('Đã thu hồi API Key');
                                      }}
                                    >
                                      <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} /> Thu hồi
                                    </button>
                                  </td>
                                </tr>
                              ))}
                              {apiKeys.length === 0 && (
                                <tr><td colSpan={6} style={{ textAlign: 'center', color: 'var(--text-muted)', padding: 14 }}>Chưa có API Key nào.</td></tr>
                              )}
                            </tbody>
                          </table>
                        </div>
                      </div>
                    </div>
                  )}
                </div>
              </div>
            )}
          </>
        )}

        {/* WORKSPACE 2: SHARED PLATFORM RESOURCES (FULL-WIDTH) */}
        {workspace === 'platform' && (
          <StudioPlatformResourcesPanel
            activeSubTab={platformTab}
            onChangeSubTab={setPlatformTab}
            canStudio={canStudio}
            canManageRoles={canManageRoles}
            canManageUsers={canManageUsers}
            canViewAudit={canViewAudit}
            apps={apps}
            roles={roles}
            users={users}
            categories={categories}
            selectedCat={selectedCat}
            catItems={catItems}
            globalAuditLogs={globalAuditLogs}
            auditAppFilter={auditAppFilter}
            currentUserId={currentUser?.id}
            onSelectCategory={setSelectedCat}
            onRefreshCategory={async () => {
              await loadCategories();
              setSelectedCat((prev) => (prev ? { ...prev } : prev));
            }}
            toast={toast}
            onCreateRole={() => {
              setEditingRole(null);
              setShowRoleModal(true);
            }}
            onEditRole={(role) => {
              setEditingRole(role);
              setShowRoleModal(true);
            }}
            onDeleteRole={(role) =>
              setConfirm({
                open: true,
                title: 'Xóa Vai trò',
                message: `Xóa vai trò "${role.name}" (${role.code})?`,
                onConfirm: async () => {
                  setConfirm((c) => ({ ...c, open: false }));
                  const r = await fetch(`/api/roles/${role.id}`, {
                    method: 'DELETE',
                    credentials: 'include',
                  }).then((x) => x.json());
                  if (r.success) {
                    await loadRoles();
                    toast('Đã xóa vai trò!');
                  } else {
                    toast(r.message || 'Không thể xóa vai trò', 'error');
                  }
                },
              })
            }
            onOpenAppPerms={(app) => setAppPermTarget(app)}
            onCreateUser={() => {
              setEditingUser(null);
              setShowUserModal(true);
            }}
            onEditUser={(u) => {
              setEditingUser(u);
              setShowUserModal(true);
            }}
            onResetUserPw={async (u) => {
              const r = await fetch(`/api/auth/users/${u.id}/reset-password`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify({ newPassword: '123456' }),
              }).then((x) => x.json());
              if (r.success) toast(r.message || 'Đã đặt lại mật khẩu 123456');
            }}
            onDeleteUser={(u) =>
              setConfirm({
                open: true,
                title: 'Xóa tài khoản',
                message: `Xóa tài khoản "${u.fullName}" (${u.username})?`,
                onConfirm: async () => {
                  setConfirm((c) => ({ ...c, open: false }));
                  const r = await fetch(`/api/auth/users/${u.id}`, {
                    method: 'DELETE',
                    credentials: 'include',
                  }).then((x) => x.json());
                  if (r.success) {
                    await loadUsers();
                    await loadRoles();
                    toast('Đã xóa tài khoản!');
                  } else {
                    toast(r.message || 'Không thể xóa tài khoản', 'error');
                  }
                },
              })
            }
            onChangeAuditAppFilter={setAuditAppFilter}
          />
        )}
      </div>

      {/* Modals */}
      <AppModal
        open={showAppModal}
        editing={editingApp}
        saving={saving}
        onClose={() => setShowAppModal(false)}
        onSave={saveApp}
        initial={
          editingApp
            ? {
                id: editingApp.id,
                code: editingApp.code,
                name: editingApp.name,
                description: editingApp.description || '',
                department: editingApp.department,
                icon: editingApp.icon,
                color: editingApp.color,
                bgColor: editingApp.bgColor,
                version: editingApp.version,
                orderIndex: editingApp.orderIndex,
              }
            : {
                id: '',
                code: '',
                name: '',
                description: '',
                department: 'Toàn công ty',
                icon: 'BusinessCenter',
                color: '#1a73e8',
                bgColor: '#e8f0fe',
                version: '1.0.0',
                orderIndex: apps.length + 1,
              }
        }
      />

      <ModuleModal
        open={showModuleModal}
        editing={editingModule}
        saving={saving}
        onClose={() => setShowModuleModal(false)}
        onSave={saveModule}
        initial={
          editingModule
            ? {
                id: editingModule.id,
                appId: editingModule.appId,
                code: editingModule.code,
                name: editingModule.name,
                icon: editingModule.icon,
                orderIndex: editingModule.orderIndex,
                description: editingModule.description || '',
              }
            : {
                id: '',
                appId: selectedApp?.id || '',
                code: '',
                name: '',
                icon: 'Layers',
                orderIndex: modules.length + 1,
                description: '',
              }
        }
      />

      <TableModal
        open={showTableModal}
        editing={editingTable}
        allRoles={roles}
        saving={saving}
        onClose={() => setShowTableModal(false)}
        onSave={saveTable}
        initial={tableModalInitial}
      />

      <ColumnModal
        open={showColumnModal}
        editing={editingColumn}
        categories={categories}
        allApps={apps}
        allTables={allTables}
        allRoles={roles}
        saving={saving}
        onClose={() => setShowColumnModal(false)}
        onSave={saveColumn}
        initial={columnModalInitial}
      />

      {selectedTable && (
        <RecordEditorModal
          open={showRecordModal}
          record={editingRecord}
          tableName={selectedTable.displayName}
          columns={columns}
          formRules={selectedTable.formRules}
          formLayout={selectedTable.formLayout}
          saving={saving}
          onClose={() => setShowRecordModal(false)}
          onSave={saveRecord}
        />
      )}

      <ExcelToAppModal
        open={showExcelModal}
        onClose={() => setShowExcelModal(false)}
        onCreated={async (app) => {
          setShowExcelModal(false);
          await refreshApps();
          await loadAllTables();
          setSelectedApp(app);
          toast(`Đã sinh App "${app.name}" từ file Excel!`);
        }}
      />

      <TemplateInstallModal
        open={showTemplateModal}
        onClose={() => setShowTemplateModal(false)}
        onInstalled={async (app) => {
          setShowTemplateModal(false);
          await refreshApps();
          await loadAllTables();
          setSelectedApp(app);
          toast(`Đã cài đặt App mẫu "${app.name}" (liên kết sẵn HRM)!`);
        }}
        onInstalledMany={async (installedApps) => {
          setShowTemplateModal(false);
          await refreshApps();
          await loadAllTables();
          if (installedApps.length > 0) setSelectedApp(installedApps[installedApps.length - 1]);
          toast(`Đã cài đặt thành công ${installedApps.length} App mẫu doanh nghiệp!`);
        }}
      />

      <AutomationModal
        open={showAutoModal}
        editing={editingAuto}
        allApps={apps}
        allTables={allTables}
        allRoles={roles}
        onClose={() => setShowAutoModal(false)}
        onSaved={async () => {
          setShowAutoModal(false);
          await loadAutomations();
          toast(editingAuto ? 'Đã cập nhật quy tắc tự động hóa!' : 'Đã tạo quy tắc tự động hóa đa App!');
        }}
      />

      <UserFormModal
        open={showUserModal}
        editing={editingUser}
        allApps={apps}
        allRoles={roles}
        onClose={() => setShowUserModal(false)}
        onSaved={async () => {
          setShowUserModal(false);
          await loadUsers();
          await loadRoles();
          await refreshAuth();
          toast(editingUser ? 'Đã cập nhật tài khoản!' : 'Đã tạo tài khoản mới!');
        }}
      />

      <RoleFormModal
        open={showRoleModal}
        editing={editingRole}
        allApps={apps}
        catalog={permCatalog}
        onClose={() => setShowRoleModal(false)}
        onSaved={async () => {
          setShowRoleModal(false);
          await loadRoles();
          await loadUsers();
          await refreshAuth();
          toast(editingRole ? 'Đã cập nhật vai trò & phân quyền App!' : 'Đã tạo Custom Role mới!');
        }}
      />

      <AppPermissionsModal
        open={Boolean(appPermTarget)}
        app={appPermTarget}
        catalog={permCatalog}
        onClose={() => setAppPermTarget(null)}
        onSaved={async () => {
          setAppPermTarget(null);
          await loadRoles();
          await loadUsers();
          await refreshAuth();
          toast('Đã cập nhật phân quyền vai trò vào App!');
        }}
      />

      <AiAppModal
        open={showAiModal}
        onClose={() => setShowAiModal(false)}
        onGenerated={async (app) => {
          setShowAiModal(false);
          await refreshApps();
          await loadAllTables();
          setSelectedApp(app);
          toast(`AI đã sinh thành công App "${app.name}" (${app.code})!`);
        }}
      />

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

      <AppPublishModal
        open={showPublishModal}
        app={selectedApp}
        onClose={() => setShowPublishModal(false)}
        onPublished={async () => {
          await refreshApps();
        }}
        toast={toast}
      />

      <AppUpgradeV2Modal
        open={showUpgradeV2Modal}
        onClose={() => setShowUpgradeV2Modal(false)}
        onUpgraded={async () => {
          await refreshApps();
          if (selectedApp) await loadModules(selectedApp.id);
        }}
        toast={toast}
      />

      <PrintTemplateDesignerModal
        open={showPrintDesignerModal}
        app={selectedApp}
        table={selectedTable}
        columns={columns}
        sampleRecord={records[0] || null}
        onClose={() => setShowPrintDesignerModal(false)}
        toast={toast}
      />
    </div>
  );
}
