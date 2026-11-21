'use client';

import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import StorefrontRoundedIcon from '@mui/icons-material/StorefrontRounded';
import CloudDownloadRoundedIcon from '@mui/icons-material/CloudDownloadRounded';
import DownloadRoundedIcon from '@mui/icons-material/DownloadRounded';
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded';
import LinkRoundedIcon from '@mui/icons-material/LinkRounded';
import SyncRoundedIcon from '@mui/icons-material/SyncRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import VerifiedUserRoundedIcon from '@mui/icons-material/VerifiedUserRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import Inventory2RoundedIcon from '@mui/icons-material/Inventory2Rounded';
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded';
import PauseRoundedIcon from '@mui/icons-material/PauseRounded';
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded';
import { ClusterIcon, EmptyState, NotificationSnackbar, PageHeader } from '@/app/components/ui';
import { usePlatform } from '@/lib/app-context';
import { useAuth } from '@/lib/auth-context';
import type {
  InstallSuiteResult,
  StoreAppDetail,
  StoreAppSummary,
  StoreInstallHistoryItem,
  StoreRegistry,
  StoreSolutionSuite,
  SysApp,
} from '@/lib/types';
import { useEscapeKey } from '@/lib/ui-hooks';

type SourceFilter = 'ALL' | 'OFFICIAL' | 'REMOTE' | 'INSTALLED';

export default function OnlineAppStorePage() {
  const { apps: workspaceApps, refreshApps } = usePlatform();
  const { hasPermission } = useAuth();
  const canStudio = hasPermission('studio.manage');

  const [storeApps, setStoreApps] = useState<StoreAppSummary[]>([]);
  const [suites, setSuites] = useState<StoreSolutionSuite[]>([]);
  const [busySuiteCode, setBusySuiteCode] = useState<string | null>(null);
  const [registries, setRegistries] = useState<StoreRegistry[]>([]);
  const [loaded, setLoaded] = useState(false);

  const [search, setSearch] = useState('');
  const [sourceFilter, setSourceFilter] = useState<SourceFilter>('ALL');
  const [categoryFilter, setCategoryFilter] = useState('ALL');
  const [includeSampleData, setIncludeSampleData] = useState(true);

  const [busyCode, setBusyCode] = useState<string | null>(null);
  const [downloadingCode, setDownloadingCode] = useState<string | null>(null);
  const [lastInstalledApp, setLastInstalledApp] = useState<SysApp | null>(null);

  const [detailTarget, setDetailTarget] = useState<StoreAppDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [customCode, setCustomCode] = useState('');
  const [customName, setCustomName] = useState('');

  const [showUrlModal, setShowUrlModal] = useState(false);
  const [packageUrlInput, setPackageUrlInput] = useState('');
  const [urlCustomCode, setUrlCustomCode] = useState('');

  const [showRegistryModal, setShowRegistryModal] = useState(false);
  const [regName, setRegName] = useState('');
  const [regUrl, setRegUrl] = useState('');
  const [regDesc, setRegDesc] = useState('');
  const [regBusy, setRegBusy] = useState(false);

  const [showExportModal, setShowExportModal] = useState(false);
  const [exportingCode, setExportingCode] = useState<string | null>(null);
  const [showHistoryModal, setShowHistoryModal] = useState(false);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyItems, setHistoryItems] = useState<StoreInstallHistoryItem[]>([]);

  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [snack, setSnack] = useState<{ open: boolean; message: string; severity: 'success' | 'error' | 'info' }>({
    open: false,
    message: '',
    severity: 'success',
  });

  const toast = useCallback((message: string, severity: 'success' | 'error' | 'info' = 'success') => {
    setSnack({ open: true, message, severity });
  }, []);

  const loadStore = useCallback(async () => {
    try {
      const [appsRes, regsRes, suitesRes] = await Promise.all([
        fetch('/api/store/apps', { credentials: 'include' }).then((r) => r.json()),
        fetch('/api/store/registries', { credentials: 'include' }).then((r) => r.json()),
        fetch('/api/store/suites', { credentials: 'include' }).then((r) => r.json()).catch(() => ({ success: false })),
      ]);
      if (appsRes.success) setStoreApps(appsRes.data || []);
      if (regsRes.success) setRegistries(regsRes.data || []);
      if (suitesRes.success) setSuites(suitesRes.data || []);
    } finally {
      setLoaded(true);
    }
  }, []);

  const handleInstallSuite = async (suite: StoreSolutionSuite) => {
    if (!canStudio) return;
    setBusySuiteCode(suite.suiteCode);
    try {
      const res = await fetch(`/api/store/suites/${encodeURIComponent(suite.suiteCode)}/install`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ includeSampleData }),
      }).then((r) => r.json());
      if (res.success && res.data) {
        const result = res.data as InstallSuiteResult;
        await refreshApps();
        await loadStore();
        toast(
          `Đã triển khai "${result.suiteName}": Cài mới ${result.installedCount} ứng dụng, bỏ qua ${result.skippedCount} ứng dụng đã có!`
        );
      } else {
        toast(res.message || 'Không thể cài đặt Bộ giải pháp.', 'error');
      }
    } finally {
      setBusySuiteCode(null);
    }
  };

  useEffect(() => {
    let cancelled = false;
    Promise.resolve().then(async () => {
      if (!cancelled) await loadStore();
    });
    return () => {
      cancelled = true;
    };
  }, [loadStore]);

  useEscapeKey(Boolean(detailTarget), () => setDetailTarget(null));
  useEscapeKey(showUrlModal, () => setShowUrlModal(false));
  useEscapeKey(showRegistryModal, () => setShowRegistryModal(false));
  useEscapeKey(showExportModal, () => setShowExportModal(false));
  useEscapeKey(showHistoryModal, () => setShowHistoryModal(false));

  const openHistoryModal = async () => {
    setShowHistoryModal(true);
    setHistoryLoading(true);
    try {
      const res = await fetch('/api/store/history?limit=40', { credentials: 'include' }).then((r) => r.json());
      if (res.success) setHistoryItems(res.data || []);
    } finally {
      setHistoryLoading(false);
    }
  };

  const triggerPackageDownload = async (endpointUrl: string, fileName: string, successMsg: string, errorMsg: string): Promise<boolean> => {
    const res = await fetch(endpointUrl, { credentials: 'include' });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      toast(err.message || errorMsg, 'error');
      return false;
    }
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
    toast(successMsg);
    return true;
  };

  const handleExportInstalledPackage = async (appCode: string, appVersion = '2.0.0') => {
    setExportingCode(appCode);
    try {
      await triggerPackageDownload(
        `/api/store/export-package/${encodeURIComponent(appCode)}`,
        `smartapp-${appCode.toLowerCase()}-v${appVersion}.smartapp.json`,
        `Đã đóng gói xuất "${appCode}" (.smartapp.json)!`,
        `Không xuất được gói ${appCode}.`
      );
    } finally {
      setExportingCode(null);
    }
  };

  const categories = useMemo(() => {
    const set = new Set<string>(['ALL']);
    storeApps.forEach((a) => set.add(a.category));
    return Array.from(set);
  }, [storeApps]);

  const filteredApps = useMemo(() => {
    const q = search.trim().toLowerCase();
    return storeApps.filter((a) => {
      if (sourceFilter === 'OFFICIAL' && a.sourceType !== 'OFFICIAL') return false;
      if (sourceFilter === 'REMOTE' && a.sourceType !== 'REMOTE') return false;
      if (sourceFilter === 'INSTALLED' && !a.isInstalled) return false;
      if (categoryFilter !== 'ALL' && a.category !== categoryFilter) return false;
      if (!q) return true;
      return (
        a.name.toLowerCase().includes(q) ||
        a.packageCode.toLowerCase().includes(q) ||
        a.description.toLowerCase().includes(q) ||
        a.department.toLowerCase().includes(q) ||
        a.tags.some((t) => t.toLowerCase().includes(q))
      );
    });
  }, [storeApps, sourceFilter, categoryFilter, search]);

  const handleDownloadPackage = async (app: StoreAppSummary) => {
    setDownloadingCode(app.packageCode);
    try {
      const qs = app.registryId ? `?registryId=${encodeURIComponent(app.registryId)}` : '';
      const ok = await triggerPackageDownload(
        `/api/store/apps/${encodeURIComponent(app.packageCode)}/download${qs}`,
        `smartapp-${app.packageCode.toLowerCase()}-v${app.version}.smartapp.json`,
        `Đã tải về gói ${app.packageCode} (v${app.version}).`,
        'Không tải được gói ứng dụng.'
      );
      if (ok) await loadStore();
    } finally {
      setDownloadingCode(null);
    }
  };

  const handleInstallFromStore = async (app: StoreAppSummary, overrideCode?: string, overrideName?: string) => {
    setBusyCode(app.packageCode);
    try {
      const res = await fetch(`/api/store/apps/${encodeURIComponent(app.packageCode)}/install`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          registryId: app.registryId || undefined,
          customCode: overrideCode?.trim() || undefined,
          customName: overrideName?.trim() || undefined,
          includeSampleData,
        }),
      }).then((r) => r.json());

      if (res.success && res.data) {
        const installed: SysApp = res.data;
        setLastInstalledApp(installed);
        setDetailTarget(null);
        await refreshApps();
        await loadStore();
        toast(`Đã tải & cài đặt thành công "${installed.name}" (${installed.code} v${installed.version})!`);
      } else {
        toast(res.message || 'Cài đặt ứng dụng từ Kho Online thất bại.', 'error');
      }
    } finally {
      setBusyCode(null);
    }
  };

  const openDetailModal = async (app: StoreAppSummary) => {
    setDetailLoading(true);
    setCustomCode(app.isInstalled ? `${app.packageCode}_2` : app.packageCode);
    setCustomName(app.name);
    try {
      const qs = app.registryId ? `?registryId=${encodeURIComponent(app.registryId)}` : '';
      const res = await fetch(`/api/store/apps/${encodeURIComponent(app.packageCode)}${qs}`, {
        credentials: 'include',
      }).then((r) => r.json());
      if (res.success && res.data) {
        setDetailTarget(res.data);
      } else {
        toast(res.message || 'Không tải được chi tiết gói ứng dụng.', 'error');
      }
    } finally {
      setDetailLoading(false);
    }
  };

  const handleInstallFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    setBusyCode('__FILE__');
    try {
      const text = await file.text();
      const parsed = JSON.parse(text);
      const res = await fetch('/api/store/install-package', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          package: parsed,
          includeSampleData,
        }),
      }).then((r) => r.json());

      if (res.success && res.data) {
        const installed: SysApp = res.data;
        setLastInstalledApp(installed);
        await refreshApps();
        await loadStore();
        toast(`Đã cài App từ tệp "${file.name}": ${installed.name} (${installed.code})!`);
      } else {
        toast(res.message || 'Tệp .smartapp.json không hợp lệ hoặc bị lỗi.', 'error');
      }
    } catch {
      toast('Không đọc được JSON trong tệp vừa chọn.', 'error');
    } finally {
      setBusyCode(null);
    }
  };

  const handleInstallUrl = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!packageUrlInput.trim()) return;
    setBusyCode('__URL__');
    try {
      const res = await fetch('/api/store/install-from-url', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          packageUrl: packageUrlInput.trim(),
          customCode: urlCustomCode.trim() || undefined,
          includeSampleData,
        }),
      }).then((r) => r.json());

      if (res.success && res.data) {
        const installed: SysApp = res.data;
        setLastInstalledApp(installed);
        setShowUrlModal(false);
        setPackageUrlInput('');
        setUrlCustomCode('');
        await refreshApps();
        await loadStore();
        toast(`Đã tải & cài App từ URL: ${installed.name} (${installed.code})!`);
      } else {
        toast(res.message || 'Không thể tải hoặc cài đặt từ URL đã nhập.', 'error');
      }
    } finally {
      setBusyCode(null);
    }
  };

  const handleAddRegistry = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!regName.trim() || !regUrl.trim()) return;
    setRegBusy(true);
    try {
      const res = await fetch('/api/store/registries', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          name: regName.trim(),
          registryUrl: regUrl.trim(),
          description: regDesc.trim() || undefined,
          isEnabled: true,
        }),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setRegName('');
        setRegUrl('');
        setRegDesc('');
        await loadStore();
        toast(
          res.data.lastError
            ? `Đã lưu nguồn kho nhưng đồng bộ gặp lỗi: ${res.data.lastError}`
            : `Đã kết nối & đồng bộ ${res.data.appCount} ứng dụng từ "${res.data.name}"!`,
          res.data.lastError ? 'info' : 'success'
        );
      } else {
        toast(res.message || 'Không thể kết nối nguồn Kho Online.', 'error');
      }
    } finally {
      setRegBusy(false);
    }
  };

  const officialCount = storeApps.filter((a) => a.sourceType === 'OFFICIAL').length;
  const remoteCount = storeApps.filter((a) => a.sourceType === 'REMOTE').length;
  const installedCount = storeApps.filter((a) => a.isInstalled).length;

  return (
    <div>
      <PageHeader
        title="Kho Apps Online (SmartApps Marketplace)"
        subtitle="Khám phá, tải xuống gói ứng dụng (.smartapp.json) hoặc cài đặt trực tiếp từ Kho Chính thức & Kho Online từ xa."
        icon={<StorefrontRoundedIcon sx={{ fontSize: 24 }} />}
        accentColor="#ea580c"
        badge={<span className="badge badge-info">v2.0 Store</span>}
        breadcrumbs={[{ label: 'Cổng Ứng dụng', href: '/' }, { label: 'Kho Apps Online' }]}
        actions={
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            {canStudio && (
              <>
                <input
                  ref={fileInputRef}
                  type="file"
                  accept=".json,.smartapp.json"
                  style={{ display: 'none' }}
                  onChange={handleInstallFile}
                />
                <button
                  type="button"
                  className="btn btn-outline btn-sm"
                  disabled={busyCode !== null}
                  onClick={() => fileInputRef.current?.click()}
                >
                  <UploadFileRoundedIcon sx={{ fontSize: 16 }} />
                  {busyCode === '__FILE__' ? 'Đang cài từ file...' : 'Cài từ File (.smartapp.json)'}
                </button>
                <button
                  type="button"
                  className="btn btn-outline btn-sm"
                  disabled={busyCode !== null}
                  onClick={() => setShowUrlModal(true)}
                >
                  <LinkRoundedIcon sx={{ fontSize: 16 }} />
                  Cài từ URL
                </button>
                <button
                  type="button"
                  className="btn btn-outline btn-sm"
                  onClick={() => setShowExportModal(true)}
                >
                  <Inventory2RoundedIcon sx={{ fontSize: 16 }} />
                  Đóng gói Xuất App
                </button>
              </>
            )}
            <button
              type="button"
              className="btn btn-outline btn-sm"
              onClick={() => void openHistoryModal()}
            >
              <HistoryRoundedIcon sx={{ fontSize: 16 }} />
              Lịch sử Kho
            </button>
            <button
              type="button"
              className="btn btn-outline btn-sm"
              onClick={() => setShowRegistryModal(true)}
            >
              <SyncRoundedIcon sx={{ fontSize: 16 }} />
              Nguồn Kho Online ({registries.length})
            </button>
          </div>
        }
      />

      <div className="page-content" style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
        {lastInstalledApp && (
          <div
            className="card"
            style={{
              padding: '12px 16px',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              gap: 12,
              flexWrap: 'wrap',
              backgroundColor: 'var(--success-bg)',
              borderColor: 'var(--border-subtle)',
            }}
          >
            <div style={{ fontSize: 13.5, color: 'var(--text-primary)' }}>
              <strong>Cài đặt hoàn tất:</strong> {lastInstalledApp.name} ({lastInstalledApp.code} • v{lastInstalledApp.version}) đã sẵn sàng sử dụng.
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <Link href={`/apps/${lastInstalledApp.code}`} className="btn btn-primary btn-xs">
                <OpenInNewRoundedIcon sx={{ fontSize: 14 }} /> Mở App ngay
              </Link>
              <Link href={`/studio?appId=${encodeURIComponent(lastInstalledApp.id)}`} className="btn btn-outline btn-xs">
                Thiết kế trong Studio
              </Link>
            </div>
          </div>
        )}

        {/* Solution Suites 1-Click */}
        {suites.length > 0 && (
          <div className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 14 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
              <div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <VerifiedUserRoundedIcon sx={{ fontSize: 19, color: 'var(--accent-primary)' }} />
                  <strong style={{ fontSize: 15 }}>Bộ Giải pháp Doanh nghiệp 1-Click (Solution Suites)</strong>
                  <span className="badge badge-info" style={{ fontSize: 11 }}>Triển khai liên ứng dụng</span>
                </div>
                <div style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 2 }}>
                  Cài đặt trọn bộ các ứng dụng cốt lõi theo từng khối nghiệp vụ chỉ với 1 cú nhấp chuột (tự động bỏ qua các ứng dụng đã cài).
                </div>
              </div>
            </div>

            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit, minmax(290px, 1fr))',
                gap: 12,
              }}
            >
              {suites.map((suite) => {
                const allInstalled = suite.installedCount >= suite.totalCount && suite.totalCount > 0;
                const isBusy = busySuiteCode === suite.suiteCode;
                return (
                  <div
                    key={suite.suiteCode}
                    style={{
                      padding: 14,
                      borderRadius: 12,
                      border: '1px solid var(--border-subtle)',
                      backgroundColor: 'var(--bg-primary)',
                      display: 'flex',
                      flexDirection: 'column',
                      justifyContent: 'space-between',
                      gap: 10,
                    }}
                  >
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                          <span
                            style={{
                              width: 36,
                              height: 36,
                              borderRadius: 10,
                              backgroundColor: suite.bgColor,
                              color: suite.color,
                              display: 'inline-flex',
                              alignItems: 'center',
                              justifyContent: 'center',
                              flexShrink: 0,
                            }}
                          >
                            <ClusterIcon icon={suite.icon} fontSize={20} />
                          </span>
                          <div>
                            <div style={{ fontWeight: 700, fontSize: 13.5, lineHeight: 1.3 }}>{suite.name}</div>
                            <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 2 }}>{suite.targetAudience}</div>
                          </div>
                        </div>
                        <span
                          className={allInstalled ? 'badge badge-success' : 'badge badge-info'}
                          style={{ fontSize: 11, flexShrink: 0 }}
                        >
                          {suite.installedCount}/{suite.totalCount} đã cài
                        </span>
                      </div>

                      <div style={{ fontSize: 12, color: 'var(--text-secondary)', lineHeight: 1.45 }}>
                        {suite.description}
                      </div>

                      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                        {suite.apps.map((app) => (
                          <span
                            key={app.packageCode}
                            style={{
                              fontSize: 11,
                              padding: '3px 8px',
                              borderRadius: 6,
                              backgroundColor: 'var(--bg-surface)',
                              border: `1px solid ${app.isInstalled ? 'var(--success-text, #10b981)' : 'var(--border-subtle)'}`,
                              color: 'var(--text-primary)',
                              display: 'inline-flex',
                              alignItems: 'center',
                              gap: 4,
                            }}
                          >
                            {app.isInstalled && <CheckCircleRoundedIcon sx={{ fontSize: 12, color: '#10b981' }} />}
                            <strong>{app.packageCode}</strong>
                          </span>
                        ))}
                      </div>
                    </div>

                    <div style={{ display: 'flex', justifyContent: 'flex-end', paddingTop: 6, borderTop: '1px solid var(--border-subtle)' }}>
                      {canStudio && (
                        <button
                          type="button"
                          className={allInstalled ? 'btn btn-outline btn-xs' : 'btn btn-primary btn-xs'}
                          disabled={isBusy || allInstalled}
                          onClick={() => void handleInstallSuite(suite)}
                        >
                          <CloudDownloadRoundedIcon sx={{ fontSize: 15 }} />
                          {isBusy
                            ? 'Đang triển khai trọn bộ...'
                            : allInstalled
                            ? 'Đã cài đủ trọn bộ'
                            : `Cài trọn bộ (${suite.totalCount - suite.installedCount} App mới)`}
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        )}

        {/* Toolbar */}
        <div className="card" style={{ padding: 14, display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'center', justifyContent: 'space-between' }}>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center', flex: '1 1 360px' }}>
              <div style={{ position: 'relative', flex: '1 1 260px', maxWidth: 380 }}>
                <SearchRoundedIcon
                  sx={{ fontSize: 18, position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--text-muted)' }}
                />
                <input
                  className="form-input"
                  style={{ paddingLeft: 34, height: 36 }}
                  placeholder="Tìm kiếm App trên Kho Online (CRM, TMĐT, BĐS, HIS, MES...)"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                />
              </div>

              <label
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 6,
                  fontSize: 12.5,
                  color: 'var(--text-primary)',
                  cursor: 'pointer',
                  padding: '6px 10px',
                  borderRadius: 8,
                  backgroundColor: 'var(--bg-primary)',
                  border: '1px solid var(--border-subtle)',
                }}
              >
                <input
                  type="checkbox"
                  checked={includeSampleData}
                  onChange={(e) => setIncludeSampleData(e.target.checked)}
                />
                Kèm dữ liệu mẫu &amp; Dashboard
              </label>
            </div>

            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              {(
                [
                  ['ALL', `Tất cả (${storeApps.length})`],
                  ['OFFICIAL', `Kho Chính thức (${officialCount})`],
                  ['REMOTE', `Kho Từ xa (${remoteCount})`],
                  ['INSTALLED', `Đã cài (${installedCount})`],
                ] as const
              ).map(([key, label]) => (
                <button
                  key={key}
                  type="button"
                  className={sourceFilter === key ? 'btn btn-primary btn-xs' : 'btn btn-outline btn-xs'}
                  onClick={() => setSourceFilter(key)}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>

          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
            {categories.map((cat) => {
              const count = cat === 'ALL' ? storeApps.length : storeApps.filter((a) => a.category === cat).length;
              return (
                <button
                  key={cat}
                  type="button"
                  className={categoryFilter === cat ? 'btn btn-primary btn-xs' : 'btn btn-outline btn-xs'}
                  onClick={() => setCategoryFilter(cat)}
                >
                  {cat === 'ALL' ? `Tất cả danh mục (${count})` : `${cat} (${count})`}
                </button>
              );
            })}
          </div>
        </div>

        {/* Store Grid */}
        {!loaded ? (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(310px, 1fr))', gap: 16 }}>
            {[1, 2, 3, 4, 5, 6].map((n) => (
              <div key={n} className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 10 }}>
                <div className="skeleton-box" style={{ height: 42, width: '70%' }} />
                <div className="skeleton-box" style={{ height: 48, width: '100%' }} />
                <div className="skeleton-box" style={{ height: 34, width: '100%' }} />
              </div>
            ))}
          </div>
        ) : filteredApps.length === 0 ? (
          <EmptyState
            title="Không tìm thấy ứng dụng phù hợp trên Kho Online"
            description="Thử xóa bộ lọc tìm kiếm hoặc đồng bộ thêm nguồn Kho Apps Online từ xa."
          />
        ) : (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(315px, 1fr))', gap: 16 }}>
            {filteredApps.map((app) => {
              const isBusy = busyCode === app.packageCode;
              const isDl = downloadingCode === app.packageCode;
              return (
                <div
                  key={`${app.sourceType}:${app.registryId || 'official'}:${app.packageCode}`}
                  className="card"
                  style={{
                    padding: 16,
                    borderTop: `4px solid ${app.color || '#1a73e8'}`,
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'space-between',
                    gap: 12,
                  }}
                >
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 10, marginBottom: 10 }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
                        <div
                          style={{
                            width: 42,
                            height: 42,
                            borderRadius: 11,
                            backgroundColor: app.bgColor || '#e8f0fe',
                            color: app.color || '#1a73e8',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            flexShrink: 0,
                          }}
                        >
                          <ClusterIcon icon={app.icon} fontSize={22} />
                        </div>
                        <div style={{ minWidth: 0 }}>
                          <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-primary)', lineHeight: 1.25 }}>
                            {app.name}
                          </div>
                          <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 2 }}>
                            {app.department} • {app.publisher}
                          </div>
                        </div>
                      </div>

                      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 4, flexShrink: 0 }}>
                        <span className="badge badge-info" style={{ fontSize: 10.5 }}>
                          v{app.version}
                        </span>
                        {app.isInstalled && (
                          <span className="badge badge-success" style={{ fontSize: 10 }}>
                            <CheckCircleRoundedIcon sx={{ fontSize: 11 }} /> Đã cài
                          </span>
                        )}
                        {app.hasUpdateAvailable && (
                          <span className="badge badge-warning" style={{ fontSize: 10 }}>
                            Có bản mới v{app.version}
                          </span>
                        )}
                      </div>
                    </div>

                    <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', lineHeight: 1.5, margin: '0 0 8px' }}>
                      {app.description}
                    </p>

                    {app.highlights.length > 0 && (
                      <ul style={{ margin: '0 0 10px', paddingLeft: 16, fontSize: 11.5, color: 'var(--text-muted)', lineHeight: 1.45 }}>
                        {app.highlights.slice(0, 2).map((h, i) => (
                          <li key={i}>{h}</li>
                        ))}
                      </ul>
                    )}

                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 5 }}>
                      <span className="badge" style={{ backgroundColor: app.bgColor || '#e8f0fe', color: app.color || '#1a73e8', fontSize: 10.5 }}>
                        {app.packageCode}
                      </span>
                      <span
                        className="badge"
                        style={{
                          backgroundColor: app.sourceType === 'REMOTE' ? 'var(--warning-bg)' : 'var(--bg-secondary)',
                          color: 'var(--text-secondary)',
                          fontSize: 10.5,
                        }}
                      >
                        {app.sourceType === 'REMOTE' ? app.registryName || 'Kho Từ xa' : 'Official Store'}
                      </span>
                      {app.features.slice(0, 3).map((f) => (
                        <span
                          key={f}
                          className="badge"
                          style={{ backgroundColor: 'var(--bg-secondary)', color: 'var(--text-secondary)', fontSize: 10.5, fontWeight: 500 }}
                        >
                          {f}
                        </span>
                      ))}
                    </div>
                  </div>

                  <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                    <div
                      style={{
                        fontSize: 11.5,
                        color: 'var(--text-muted)',
                        backgroundColor: 'var(--bg-primary)',
                        padding: '7px 10px',
                        borderRadius: 8,
                        display: 'flex',
                        flexDirection: 'column',
                        gap: 3,
                      }}
                    >
                      <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                        <span><strong>{app.moduleCount}</strong> phân hệ • <strong>{app.tableCount}</strong> bảng</span>
                        <span>{(app.packageSizeBytes / 1024).toFixed(1)} KB • {app.downloadCount + app.installCount} lượt</span>
                      </div>
                      <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>
                        {app.formRuleCount} Rule • {app.workflowCount} WF • {app.printTemplateCount} Mẫu in • {app.savedViewCount} View
                      </div>
                    </div>

                    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        onClick={() => void openDetailModal(app)}
                        disabled={detailLoading}
                      >
                        <InfoOutlinedIcon sx={{ fontSize: 14 }} /> Chi tiết
                      </button>
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        disabled={isDl}
                        onClick={() => void handleDownloadPackage(app)}
                        title="Tải xuống gói cài đặt .smartapp.json về máy"
                      >
                        <DownloadRoundedIcon sx={{ fontSize: 14 }} />
                        {isDl ? 'Đang tải...' : 'Tải về (.json)'}
                      </button>
                      {app.isInstalled && app.installedAppCode ? (
                        <>
                          <Link
                            href={`/apps/${app.installedAppCode}`}
                            className="btn btn-outline btn-xs"
                            style={{ flex: 1, justifyContent: 'center' }}
                          >
                            <OpenInNewRoundedIcon sx={{ fontSize: 14 }} /> Mở App
                          </Link>
                          {canStudio && (
                            <button
                              type="button"
                              className="btn btn-outline btn-xs"
                              disabled={exportingCode === app.installedAppCode}
                              onClick={() => void handleExportInstalledPackage(app.installedAppCode!, app.installedVersion || app.version)}
                              title={`Đóng gói xuất ${app.installedAppCode} (.smartapp.json)`}
                            >
                              {exportingCode === app.installedAppCode ? '...' : 'Xuất gói'}
                            </button>
                          )}
                        </>
                      ) : canStudio ? (
                        <button
                          type="button"
                          className="btn btn-primary btn-xs"
                          style={{ flex: 1, justifyContent: 'center', backgroundColor: app.color || '#1a73e8' }}
                          disabled={busyCode !== null}
                          onClick={() => void handleInstallFromStore(app)}
                        >
                          <CloudDownloadRoundedIcon sx={{ fontSize: 14 }} />
                          {isBusy ? 'Đang cài đặt...' : 'Tải & Cài đặt'}
                        </button>
                      ) : null}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Store App Detail Modal */}
      {detailTarget && (
        <div className="modal-overlay" onClick={() => setDetailTarget(null)}>
          <div className="modal-dialog" style={{ maxWidth: 780, maxHeight: '88vh', display: 'flex', flexDirection: 'column' }} onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <div
                  style={{
                    width: 40,
                    height: 40,
                    borderRadius: 10,
                    backgroundColor: detailTarget.summary.bgColor,
                    color: detailTarget.summary.color,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                  }}
                >
                  <ClusterIcon icon={detailTarget.summary.icon} fontSize={20} />
                </div>
                <div>
                  <h3 style={{ margin: 0, fontSize: 16 }}>
                    {detailTarget.summary.name} <span className="badge badge-info">v{detailTarget.summary.version}</span>
                  </h3>
                  <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                    {detailTarget.summary.publisher} • Mã gói: {detailTarget.summary.packageCode}
                  </div>
                </div>
              </div>
              <button type="button" className="btn btn-outline btn-xs" onClick={() => setDetailTarget(null)}>
                <CloseRoundedIcon sx={{ fontSize: 16 }} />
              </button>
            </div>

            <div className="modal-body" style={{ overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 12 }}>
              <p style={{ margin: 0, fontSize: 13, color: 'var(--text-secondary)' }}>{detailTarget.summary.description}</p>

              {detailTarget.summary.isInstalled && (
                <div
                  style={{
                    padding: '8px 10px',
                    borderRadius: 8,
                    backgroundColor: detailTarget.summary.hasUpdateAvailable ? 'var(--warning-bg)' : 'var(--success-bg)',
                    color: detailTarget.summary.hasUpdateAvailable ? 'var(--warning-text)' : 'var(--success-text)',
                    fontSize: 12,
                    fontWeight: 600,
                  }}
                >
                  Đang cài trên Workspace: <code>{detailTarget.summary.installedAppCode}</code> (v{detailTarget.summary.installedVersion || detailTarget.summary.version})
                  {detailTarget.summary.hasUpdateAvailable ? ` → Bản mới trên Kho: v${detailTarget.summary.version}` : ' • Đang ở phiên bản mới nhất'}
                </div>
              )}

              <div style={{ padding: '8px 10px', borderRadius: 8, backgroundColor: 'var(--bg-primary)', fontSize: 11.5, color: 'var(--text-muted)', display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                <VerifiedUserRoundedIcon sx={{ fontSize: 15, color: '#10b981' }} />
                <span>SHA-256:</span>
                <code style={{ wordBreak: 'break-all' }}>{detailTarget.summary.checksumSha256 || 'Chưa gắn checksum'}</code>
              </div>

              <div>
                <strong style={{ fontSize: 13, display: 'block', marginBottom: 6 }}>
                  Cấu trúc Phân hệ &amp; Bảng dữ liệu ({detailTarget.modules.length} phân hệ)
                </strong>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                  {detailTarget.modules.map((mod) => (
                    <div key={mod.code} style={{ padding: 10, borderRadius: 8, border: '1px solid var(--border-subtle)', backgroundColor: 'var(--bg-primary)' }}>
                      <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 6 }}>{mod.name} ({mod.code})</div>
                      {mod.tables.map((tbl) => (
                        <div key={tbl.tableName} style={{ fontSize: 12, color: 'var(--text-secondary)', marginBottom: 4 }}>
                          • <strong>{tbl.displayName}</strong> (<code>{tbl.tableName}</code>) — {tbl.columns.length} cột, {tbl.formRules.length} FormRules
                          {tbl.approvalEnabled ? ' • Có phê duyệt' : ''}
                          {tbl.publicFormEnabled ? ' • Public Form' : ''}
                        </div>
                      ))}
                    </div>
                  ))}
                </div>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 10, fontSize: 12 }}>
                <div style={{ padding: 10, borderRadius: 8, border: '1px solid var(--border-subtle)' }}>
                  <strong>Quy trình &amp; Tự động hóa</strong>
                  <div style={{ color: 'var(--text-muted)', marginTop: 4 }}>
                    {detailTarget.workflows.length} Workflow • {detailTarget.automations.length} Automation
                  </div>
                </div>
                <div style={{ padding: 10, borderRadius: 8, border: '1px solid var(--border-subtle)' }}>
                  <strong>Mẫu in &amp; Saved Views</strong>
                  <div style={{ color: 'var(--text-muted)', marginTop: 4 }}>
                    {detailTarget.printTemplates.length} Mẫu in • {detailTarget.savedViews.length} Saved Views
                  </div>
                </div>
                <div style={{ padding: 10, borderRadius: 8, border: '1px solid var(--border-subtle)' }}>
                  <strong>Plugin Tích hợp VN</strong>
                  <div style={{ color: 'var(--text-muted)', marginTop: 4 }}>
                    {detailTarget.pluginBindings.map((p) => p.pluginCode).join(', ') || 'Sẵn sàng gắn thêm'}
                  </div>
                </div>
              </div>

              {canStudio && (
                <div style={{ padding: 12, borderRadius: 10, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-subtle)' }}>
                  <div style={{ fontSize: 12.5, fontWeight: 600, marginBottom: 8 }}>Tùy chỉnh khi cài đặt vào Workspace:</div>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1.6fr', gap: 8 }}>
                    <input
                      className="form-input"
                      placeholder="Mã App..."
                      value={customCode}
                      onChange={(e) => setCustomCode(e.target.value.toUpperCase().replace(/[^A-Z0-9_]/g, ''))}
                    />
                    <input
                      className="form-input"
                      placeholder="Tên hiển thị App..."
                      value={customName}
                      onChange={(e) => setCustomName(e.target.value)}
                    />
                  </div>
                </div>
              )}
            </div>

            <div className="modal-footer" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
              <button
                type="button"
                className="btn btn-outline btn-sm"
                onClick={() => void handleDownloadPackage(detailTarget.summary)}
              >
                <DownloadRoundedIcon sx={{ fontSize: 15 }} /> Tải gói .smartapp.json
              </button>
              <div style={{ display: 'flex', gap: 8 }}>
                <button type="button" className="btn btn-outline btn-sm" onClick={() => setDetailTarget(null)}>
                  Đóng
                </button>
                {canStudio && (
                  <button
                    type="button"
                    className="btn btn-primary btn-sm"
                    disabled={busyCode !== null}
                    onClick={() => void handleInstallFromStore(detailTarget.summary, customCode, customName)}
                  >
                    <CloudDownloadRoundedIcon sx={{ fontSize: 15 }} />
                    {busyCode === detailTarget.summary.packageCode ? 'Đang cài đặt...' : 'Tải & Cài đặt ngay'}
                  </button>
                )}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Install From Package URL Modal */}
      {showUrlModal && (
        <div className="modal-overlay" onClick={() => setShowUrlModal(false)}>
          <div className="modal-dialog" style={{ maxWidth: 520 }} onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3 style={{ margin: 0, fontSize: 16 }}>Cài App từ URL Gói (.smartapp.json)</h3>
              <button type="button" className="btn btn-outline btn-xs" onClick={() => setShowUrlModal(false)}>✕</button>
            </div>
            <form onSubmit={handleInstallUrl}>
              <div className="modal-body" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                <div>
                  <label className="form-label">URL tệp gói .smartapp.json *</label>
                  <input
                    required
                    className="form-input"
                    placeholder="https://example.com/api/store/apps/ECOMMERCE_VN/download"
                    value={packageUrlInput}
                    onChange={(e) => setPackageUrlInput(e.target.value)}
                  />
                </div>
                <div>
                  <label className="form-label">Mã App tùy chỉnh (tùy chọn)</label>
                  <input
                    className="form-input"
                    placeholder="VD: ECOM_2026"
                    value={urlCustomCode}
                    onChange={(e) => setUrlCustomCode(e.target.value.toUpperCase().replace(/[^A-Z0-9_]/g, ''))}
                  />
                </div>
              </div>
              <div className="modal-footer" style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowUrlModal(false)}>Hủy</button>
                <button type="submit" className="btn btn-primary btn-sm" disabled={busyCode === '__URL__'}>
                  {busyCode === '__URL__' ? 'Đang tải & cài...' : 'Tải & Cài đặt'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Store Registries Modal */}
      {showRegistryModal && (
        <div className="modal-overlay" onClick={() => setShowRegistryModal(false)}>
          <div className="modal-dialog" style={{ maxWidth: 720, maxHeight: '86vh', display: 'flex', flexDirection: 'column' }} onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div>
                <h3 style={{ margin: 0, fontSize: 16 }}>Nguồn Kho Apps Online Từ Xa (Store Registries)</h3>
                <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                  Kết nối tới endpoint manifest.json của các server SmartApps khác để đồng bộ, tải về &amp; cài đặt App trực tuyến.
                </div>
              </div>
              <button type="button" className="btn btn-outline btn-xs" onClick={() => setShowRegistryModal(false)}>✕</button>
            </div>
            <div className="modal-body" style={{ overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 14 }}>
              <div
                style={{
                  padding: '10px 12px',
                  borderRadius: 10,
                  border: '1px solid var(--info-border)',
                  backgroundColor: 'var(--info-bg)',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  gap: 10,
                  flexWrap: 'wrap',
                }}
              >
                <div style={{ fontSize: 12, color: 'var(--info-text)' }}>
                  <strong>Public Store Manifest Hub của hệ thống này:</strong>{' '}
                  <code>/api/store/manifest.json</code> (cho phép các node SmartApps khác đăng ký làm Kho từ xa)
                </div>
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  onClick={() => {
                    const url = typeof window !== 'undefined' ? `${window.location.origin}/api/store/manifest.json` : '/api/store/manifest.json';
                    void navigator.clipboard?.writeText(url);
                    toast(`Đã sao chép URL Manifest: ${url}`);
                  }}
                >
                  <ContentCopyRoundedIcon sx={{ fontSize: 13 }} /> Sao chép Manifest URL
                </button>
              </div>

              {canStudio && (
                <form onSubmit={handleAddRegistry} style={{ padding: 12, borderRadius: 10, backgroundColor: 'var(--bg-primary)', border: '1px solid var(--border-subtle)', display: 'flex', flexDirection: 'column', gap: 8 }}>
                  <strong style={{ fontSize: 13 }}>Thêm &amp; Đồng bộ Nguồn Kho mới</strong>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 8 }}>
                    <input
                      required
                      className="form-input"
                      placeholder="Tên kho (VD: Partner Hub)"
                      value={regName}
                      onChange={(e) => setRegName(e.target.value)}
                    />
                    <input
                      required
                      className="form-input"
                      placeholder="URL Manifest (VD: https://store.domain/api/store/manifest.json)"
                      value={regUrl}
                      onChange={(e) => setRegUrl(e.target.value)}
                    />
                  </div>
                  <div style={{ display: 'flex', gap: 8 }}>
                    <input
                      className="form-input"
                      placeholder="Mô tả ngắn (tùy chọn)"
                      value={regDesc}
                      onChange={(e) => setRegDesc(e.target.value)}
                    />
                    <button type="submit" className="btn btn-primary btn-sm" disabled={regBusy}>
                      <SyncRoundedIcon sx={{ fontSize: 15 }} /> {regBusy ? 'Đang đồng bộ...' : 'Kết nối & Đồng bộ'}
                    </button>
                  </div>
                </form>
              )}

              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                {registries.length === 0 ? (
                  <div style={{ fontSize: 12.5, color: 'var(--text-muted)', textAlign: 'center', padding: 16 }}>
                    Chưa có nguồn Kho Online từ xa nào được thêm. Hiện đang sử dụng SmartApps Official Store tích hợp sẵn.
                  </div>
                ) : (
                  registries.map((reg) => (
                    <div key={reg.id} style={{ padding: 10, borderRadius: 8, border: '1px solid var(--border-subtle)', display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 10 }}>
                      <div style={{ minWidth: 0 }}>
                        <div style={{ fontSize: 13, fontWeight: 700, display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                          <span>{reg.name}</span>
                          <span className="badge badge-info">{reg.appCount} apps</span>
                          <span className={reg.isEnabled ? 'badge badge-success' : 'badge badge-warning'} style={{ fontSize: 10 }}>
                            {reg.isEnabled ? 'Đang bật' : 'Tạm dừng'}
                          </span>
                        </div>
                        <div style={{ fontSize: 11.5, color: 'var(--text-muted)', wordBreak: 'break-all' }}>{reg.registryUrl}</div>
                        {reg.lastError && (
                          <div style={{ fontSize: 11.5, color: '#ef4444', marginTop: 2 }}>Lỗi: {reg.lastError}</div>
                        )}
                      </div>
                      {canStudio && (
                        <div style={{ display: 'flex', gap: 6, flexShrink: 0 }}>
                          <button
                            type="button"
                            className="btn btn-outline btn-xs"
                            disabled={regBusy}
                            onClick={async () => {
                              setRegBusy(true);
                              try {
                                await fetch(`/api/store/registries/${encodeURIComponent(reg.id)}/toggle`, { method: 'PATCH', credentials: 'include' });
                                await loadStore();
                                toast(reg.isEnabled ? `Đã tạm dừng nguồn "${reg.name}".` : `Đã kích hoạt nguồn "${reg.name}".`, 'info');
                              } finally {
                                setRegBusy(false);
                              }
                            }}
                            title={reg.isEnabled ? 'Tạm dừng đồng bộ nguồn này' : 'Kích hoạt lại nguồn này'}
                          >
                            {reg.isEnabled ? <PauseRoundedIcon sx={{ fontSize: 14 }} /> : <PlayArrowRoundedIcon sx={{ fontSize: 14 }} />}
                            {reg.isEnabled ? 'Tạm dừng' : 'Kích hoạt'}
                          </button>
                          <button
                            type="button"
                            className="btn btn-outline btn-xs"
                            disabled={regBusy}
                            onClick={async () => {
                              setRegBusy(true);
                              try {
                                await fetch(`/api/store/registries/${encodeURIComponent(reg.id)}/sync`, { method: 'POST', credentials: 'include' });
                                await loadStore();
                                toast(`Đã đồng bộ lại "${reg.name}"!`);
                              } finally {
                                setRegBusy(false);
                              }
                            }}
                          >
                            <SyncRoundedIcon sx={{ fontSize: 14 }} /> Đồng bộ
                          </button>
                          <button
                            type="button"
                            className="btn btn-outline btn-xs"
                            disabled={regBusy}
                            onClick={async () => {
                              setRegBusy(true);
                              try {
                                await fetch(`/api/store/registries/${encodeURIComponent(reg.id)}`, { method: 'DELETE', credentials: 'include' });
                                await loadStore();
                                toast(`Đã xóa nguồn "${reg.name}".`, 'info');
                              } finally {
                                setRegBusy(false);
                              }
                            }}
                          >
                            <DeleteOutlineRoundedIcon sx={{ fontSize: 14 }} />
                          </button>
                        </div>
                      )}
                    </div>
                  ))
                )}
              </div>
            </div>
            <div className="modal-footer" style={{ display: 'flex', justifyContent: 'flex-end' }}>
              <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowRegistryModal(false)}>Đóng</button>
            </div>
          </div>
        </div>
      )}

      {/* Export Workspace App Modal */}
      {showExportModal && (
        <div className="modal-overlay" onClick={() => setShowExportModal(false)}>
          <div className="modal-dialog" style={{ maxWidth: 640, maxHeight: '84vh', display: 'flex', flexDirection: 'column' }} onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div>
                <h3 style={{ margin: 0, fontSize: 16 }}>Đóng gói Ứng dụng thành Gói Store (.smartapp.json)</h3>
                <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                  Xuất toàn bộ cấu trúc bảng, công thức, FormRules, Workflow, Automation, Mẫu in, Saved Views &amp; checksum SHA-256.
                </div>
              </div>
              <button type="button" className="btn btn-outline btn-xs" onClick={() => setShowExportModal(false)}>✕</button>
            </div>
            <div className="modal-body" style={{ overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 8 }}>
              {workspaceApps.length === 0 ? (
                <div style={{ textAlign: 'center', color: 'var(--text-muted)', padding: 16, fontSize: 13 }}>
                  Chưa có ứng dụng nào trong Workspace để đóng gói.
                </div>
              ) : (
                workspaceApps.map((app) => (
                  <div
                    key={app.id}
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      gap: 10,
                      padding: '10px 12px',
                      borderRadius: 10,
                      border: '1px solid var(--border-subtle)',
                      backgroundColor: 'var(--bg-primary)',
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
                      <div
                        style={{
                          width: 34,
                          height: 34,
                          borderRadius: 8,
                          backgroundColor: app.bgColor || '#e8f0fe',
                          color: app.color || '#1a73e8',
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          flexShrink: 0,
                        }}
                      >
                        <ClusterIcon icon={app.icon} fontSize={18} />
                      </div>
                      <div style={{ minWidth: 0 }}>
                        <div style={{ fontWeight: 700, fontSize: 13.5 }}>
                          {app.name} <span className="badge badge-info">v{app.version}</span>
                        </div>
                        <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
                          <code>{app.code}</code> • {app.department || 'Doanh nghiệp'}
                        </div>
                      </div>
                    </div>
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      disabled={exportingCode === app.code}
                      onClick={() => void handleExportInstalledPackage(app.code, app.version || '2.0.0')}
                    >
                      <DownloadRoundedIcon sx={{ fontSize: 14 }} />
                      {exportingCode === app.code ? 'Đang xuất...' : 'Xuất .smartapp.json'}
                    </button>
                  </div>
                ))
              )}
            </div>
            <div className="modal-footer" style={{ display: 'flex', justifyContent: 'flex-end' }}>
              <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowExportModal(false)}>Đóng</button>
            </div>
          </div>
        </div>
      )}

      {/* Store Install & Download History Modal */}
      {showHistoryModal && (
        <div className="modal-overlay" onClick={() => setShowHistoryModal(false)}>
          <div className="modal-dialog" style={{ maxWidth: 680, maxHeight: '84vh', display: 'flex', flexDirection: 'column' }} onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div>
                <h3 style={{ margin: 0, fontSize: 16 }}>Lịch sử Tải &amp; Cài đặt Kho Apps Online</h3>
                <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                  Nhật ký kiểm toán các gói .smartapp.json đã tải xuống hoặc cài đặt vào Workspace.
                </div>
              </div>
              <button type="button" className="btn btn-outline btn-xs" onClick={() => setShowHistoryModal(false)}>✕</button>
            </div>
            <div className="modal-body" style={{ overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 8 }}>
              {historyLoading ? (
                <div style={{ textAlign: 'center', color: 'var(--text-muted)', padding: 20, fontSize: 13 }}>
                  Đang tải nhật ký Kho Apps...
                </div>
              ) : historyItems.length === 0 ? (
                <div style={{ textAlign: 'center', color: 'var(--text-muted)', padding: 20, fontSize: 13 }}>
                  Chưa có lượt tải xuống hoặc cài đặt nào được ghi nhận.
                </div>
              ) : (
                historyItems.map((h) => (
                  <div
                    key={h.id}
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      gap: 10,
                      padding: '8px 12px',
                      borderRadius: 8,
                      border: '1px solid var(--border-subtle)',
                      backgroundColor: 'var(--bg-primary)',
                      fontSize: 12.5,
                    }}
                  >
                    <div>
                      <strong>{h.packageCode}</strong> <span className="badge badge-info">v{h.installedVersion}</span>{' '}
                      <span className="badge">{h.actionType}</span>
                      <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 2 }}>
                        Thực hiện bởi @{h.actor} • {new Date(h.createdAt).toLocaleString('vi-VN')}
                      </div>
                    </div>
                    {h.packageChecksum && (
                      <code style={{ fontSize: 11, color: 'var(--text-muted)' }}>{h.packageChecksum.slice(0, 12)}...</code>
                    )}
                  </div>
                ))
              )}
            </div>
            <div className="modal-footer" style={{ display: 'flex', justifyContent: 'flex-end' }}>
              <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowHistoryModal(false)}>Đóng</button>
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
