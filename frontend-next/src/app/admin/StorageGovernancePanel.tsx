'use client';

import React, { useCallback, useEffect, useState } from 'react';
import CloudQueueRoundedIcon from '@mui/icons-material/CloudQueueRounded';
import CleaningServicesRoundedIcon from '@mui/icons-material/CleaningServicesRounded';
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import DownloadRoundedIcon from '@mui/icons-material/DownloadRounded';
import ShareRoundedIcon from '@mui/icons-material/ShareRounded';
import DnsRoundedIcon from '@mui/icons-material/DnsRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import { ConfirmDialog } from '@/app/components/ui';
import { AttachmentInfo, ClusterStatusResponse, StorageStats, SysApp } from '@/lib/types';

function formatBytes(bytes: number): string {
  if (!bytes || bytes <= 0) return '0 B';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

export default function StorageGovernancePanel({
  apps,
  toast,
}: {
  apps: SysApp[];
  toast: (message: string, severity?: 'success' | 'error' | 'info') => void;
}) {
  const [stats, setStats] = useState<StorageStats | null>(null);
  const [clusterStatus, setClusterStatus] = useState<ClusterStatusResponse | null>(null);
  const [files, setFiles] = useState<AttachmentInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [filterAppId, setFilterAppId] = useState('');
  const [searchQ, setSearchQ] = useState('');

  const [scopeType, setScopeType] = useState<'GLOBAL' | 'APP' | 'USER'>('APP');
  const [scopeId, setScopeId] = useState('');
  const [quotaMb, setQuotaMb] = useState(500);
  const [savingQuota, setSavingQuota] = useState(false);

  const [minAgeHours, setMinAgeHours] = useState(24);
  const [cleaning, setCleaning] = useState(false);
  const [confirmCleanup, setConfirmCleanup] = useState(false);
  const [sharingId, setSharingId] = useState<string | null>(null);

  const loadAll = useCallback(async () => {
    const qs = new URLSearchParams();
    if (filterAppId) qs.set('appId', filterAppId);
    if (searchQ.trim()) qs.set('q', searchQ.trim());
    qs.set('limit', '100');
    try {
      const [stRes, listRes, clRes] = await Promise.all([
        fetch('/api/attachments/stats', { credentials: 'include' }).then((r) => r.json()),
        fetch(`/api/attachments/list?${qs.toString()}`, { credentials: 'include' }).then((r) => r.json()),
        fetch('/api/settings/cluster-status', { credentials: 'include' }).then((r) => r.json()).catch(() => ({ success: false })),
      ]);
      if (stRes.success && stRes.data) setStats(stRes.data);
      if (listRes.success && Array.isArray(listRes.data)) setFiles(listRes.data);
      if (clRes.success && clRes.data) setClusterStatus(clRes.data);
    } finally {
      setLoading(false);
    }
  }, [filterAppId, searchQ]);

  useEffect(() => {
    let active = true;
    const qs = new URLSearchParams();
    if (filterAppId) qs.set('appId', filterAppId);
    if (searchQ.trim()) qs.set('q', searchQ.trim());
    qs.set('limit', '100');
    Promise.all([
      fetch('/api/attachments/stats', { credentials: 'include' }).then((r) => r.json()),
      fetch(`/api/attachments/list?${qs.toString()}`, { credentials: 'include' }).then((r) => r.json()),
      fetch('/api/settings/cluster-status', { credentials: 'include' }).then((r) => r.json()).catch(() => ({ success: false })),
    ])
      .then(([stRes, listRes, clRes]) => {
        if (!active) return;
        if (stRes.success && stRes.data) setStats(stRes.data);
        if (listRes.success && Array.isArray(listRes.data)) setFiles(listRes.data);
        if (clRes.success && clRes.data) setClusterStatus(clRes.data);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, [filterAppId, searchQ]);

  const handleSaveQuota = async (e: React.FormEvent) => {
    e.preventDefault();
    const targetScopeId = scopeType === 'GLOBAL' ? '*' : scopeId.trim();
    if (!targetScopeId) {
      toast('Vui lòng chọn App hoặc nhập Username áp dụng Quota.', 'error');
      return;
    }
    setSavingQuota(true);
    try {
      const res = await fetch('/api/attachments/quotas', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          scopeType,
          scopeId: targetScopeId,
          quotaMb: Math.max(1, Number(quotaMb) || 100),
        }),
      }).then((r) => r.json());
      if (res.success) {
        toast('Đã lưu hạn mức lưu trữ!');
        await loadAll();
      } else {
        toast(res.message || 'Không thể lưu hạn mức.', 'error');
      }
    } finally {
      setSavingQuota(false);
    }
  };

  const handleCleanupOrphans = async () => {
    setConfirmCleanup(false);
    setCleaning(true);
    try {
      const res = await fetch(`/api/attachments/cleanup-orphans?minAgeHours=${Math.max(0, Number(minAgeHours) || 0)}`, {
        method: 'POST',
        credentials: 'include',
      }).then((r) => r.json());
      if (res.success && res.data) {
        toast(
          `Đã dọn ${res.data.cleanedCount} tệp mồ côi (${formatBytes(res.data.freedBytes)}) trên ${res.data.storageProvider}.`
        );
        await loadAll();
      } else {
        toast(res.message || 'Dọn tệp thất bại.', 'error');
      }
    } finally {
      setCleaning(false);
    }
  };

  const handleCreateShare = async (fileId: string) => {
    setSharingId(fileId);
    try {
      const res = await fetch(`/api/attachments/${encodeURIComponent(fileId)}/share`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ expiresInHours: 168, allowDownload: true }),
      }).then((r) => r.json());
      if (res.success && res.data?.shareUrl) {
        const fullUrl = typeof window !== 'undefined' ? `${window.location.origin}${res.data.shareUrl}` : res.data.shareUrl;
        if (typeof navigator !== 'undefined' && navigator.clipboard) {
          await navigator.clipboard.writeText(fullUrl).catch(() => {});
        }
        toast(`Đã tạo link chia sẻ (hạn 7 ngày) & sao chép: ${res.data.shareUrl}`);
      } else {
        toast(res.message || 'Không thể tạo liên kết chia sẻ.', 'error');
      }
    } finally {
      setSharingId(null);
    }
  };

  const appLabel = (id?: string | null) => {
    if (!id || id === 'global') return 'Hệ thống / Chung';
    const found = apps.find((a) => a.id === id || a.code === id);
    return found ? `${found.name} (${found.code})` : id;
  };

  const appEntries = Object.entries(stats?.usedByApp || {}).sort((a, b) => b[1] - a[1]);
  const userEntries = Object.entries(stats?.usedByUser || {}).sort((a, b) => b[1] - a[1]).slice(0, 8);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {/* Multi-Instance HA Cluster Health */}
      {clusterStatus && (
        <div className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
            <div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <DnsRoundedIcon sx={{ fontSize: 20, color: 'var(--accent-primary)' }} />
                <strong style={{ fontSize: 15 }}>Trạng thái Cụm HA (Multi-Instance Cluster Health)</strong>
                <span className="badge badge-success" style={{ fontSize: 11 }}>
                  {clusterStatus.nodes.length} API Node hoạt động
                </span>
              </div>
              <div style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 2 }}>
                Đồng bộ thời gian thực qua PostgreSQL LISTEN/NOTIFY, Advisory Locks cho Scheduled Jobs và SKIP LOCKED cho Outbox.
              </div>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, fontSize: 12 }}>
              <span>
                Độ trễ DB: <strong>{clusterStatus.metrics.dbProbeLatencyMs} ms</strong>
              </span>
              <span>
                SSE Streams: <strong>{clusterStatus.metrics.activeSseSubscribers}</strong>
              </span>
              <span>
                Outbox Pending: <strong>{clusterStatus.metrics.outboxPending}</strong>
              </span>
            </div>
          </div>

          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))',
              gap: 10,
            }}
          >
            {clusterStatus.nodes.map((node) => (
              <div
                key={node.instanceId}
                style={{
                  padding: '10px 12px',
                  borderRadius: 10,
                  border: `1px solid ${node.isCurrent ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
                  backgroundColor: 'var(--bg-primary)',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 4,
                  fontSize: 12,
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <strong>
                    Node #{node.instanceId} {node.isCurrent ? '(Hiện tại)' : ''}
                  </strong>
                  <span className="badge badge-info" style={{ fontSize: 10.5 }}>
                    v{node.appVersion}
                  </span>
                </div>
                <div style={{ color: 'var(--text-muted)' }}>
                  Host: {node.hostName} • PID {node.processId}
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 2 }}>
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, color: '#10b981', fontWeight: 600 }}>
                    <CheckCircleRoundedIcon sx={{ fontSize: 13 }} />
                    {node.listenerConnected ? 'LISTEN/NOTIFY Online' : 'Poller Active'}
                  </span>
                  <span>• SSE: {node.activeSse}</span>
                  <span>• Users: {node.onlineUsers.length}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Overview + Orphan Cleanup */}
      <div className="card" style={{ padding: 18, display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <CloudQueueRoundedIcon sx={{ fontSize: 20, color: '#1a73e8' }} />
            <strong style={{ fontSize: 15 }}>Quản trị Lưu trữ, Quota &amp; Tệp đính kèm</strong>
          </div>
          <div style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 2 }}>
            Theo dõi dung lượng theo App/User, giới hạn Quota upload và dọn dẹp tệp mồ côi trên Local / S3.
          </div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <label style={{ fontSize: 12, color: 'var(--text-secondary)', display: 'flex', alignItems: 'center', gap: 6 }}>
            Tuổi tệp mồ côi ≥
            <input
              type="number"
              className="form-input"
              style={{ width: 70, height: 32 }}
              min={0}
              value={minAgeHours}
              onChange={(e) => setMinAgeHours(Number(e.target.value) || 0)}
            />
            giờ
          </label>
          <button
            type="button"
            className="btn btn-outline btn-sm"
            disabled={cleaning}
            onClick={() => setConfirmCleanup(true)}
          >
            <CleaningServicesRoundedIcon sx={{ fontSize: 15 }} />
            {cleaning ? 'Đang dọn...' : 'Dọn tệp mồ côi'}
          </button>
          <button type="button" className="btn btn-outline btn-sm" onClick={() => void loadAll()}>
            <RefreshRoundedIcon sx={{ fontSize: 15 }} /> Làm mới
          </button>
        </div>
      </div>

      {/* Summary metrics */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(190px, 1fr))', gap: 12 }}>
        <div className="card" style={{ padding: 16 }}>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Tổng dung lượng đang dùng</div>
          <div style={{ fontSize: 22, fontWeight: 700, marginTop: 4 }}>{formatBytes(stats?.totalUsedBytes || 0)}</div>
        </div>
        <div className="card" style={{ padding: 16 }}>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Tổng số tệp đang lưu</div>
          <div style={{ fontSize: 22, fontWeight: 700, marginTop: 4 }}>{stats?.totalFiles ?? 0}</div>
        </div>
        <div className="card" style={{ padding: 16 }}>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Số hạn mức Quota</div>
          <div style={{ fontSize: 22, fontWeight: 700, marginTop: 4 }}>{stats?.quotas?.length ?? 0}</div>
        </div>
        <div className="card" style={{ padding: 16 }}>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Ứng dụng có tệp</div>
          <div style={{ fontSize: 22, fontWeight: 700, marginTop: 4 }}>{appEntries.length}</div>
        </div>
      </div>

      {/* Quota Config & Breakdown */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: 16 }}>
        <div className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 12 }}>
          <strong style={{ fontSize: 14 }}>Thiết lập Hạn mức (Storage Quota)</strong>
          <form onSubmit={handleSaveQuota} style={{ display: 'grid', gridTemplateColumns: '1fr 1.2fr 0.8fr auto', gap: 8, alignItems: 'end' }}>
            <div>
              <label className="form-label">Phạm vi</label>
              <select
                className="form-select"
                value={scopeType}
                onChange={(e) => {
                  const val = e.target.value as 'GLOBAL' | 'APP' | 'USER';
                  setScopeType(val);
                  setScopeId(val === 'GLOBAL' ? '*' : val === 'APP' ? (apps[0]?.id || '') : '');
                }}
              >
                <option value="APP">Theo App</option>
                <option value="USER">Theo User</option>
                <option value="GLOBAL">Toàn hệ thống (*)</option>
              </select>
            </div>
            <div>
              <label className="form-label">Đối tượng (scopeId)</label>
              {scopeType === 'GLOBAL' ? (
                <input className="form-input" value="*" disabled />
              ) : scopeType === 'APP' ? (
                <select className="form-select" value={scopeId} onChange={(e) => setScopeId(e.target.value)}>
                  <option value="">-- Chọn App --</option>
                  {apps.map((a) => (
                    <option key={a.id} value={a.id}>{a.name} ({a.code})</option>
                  ))}
                </select>
              ) : (
                <input
                  className="form-input"
                  value={scopeId}
                  onChange={(e) => setScopeId(e.target.value)}
                  placeholder="username"
                />
              )}
            </div>
            <div>
              <label className="form-label">Giới hạn (MB)</label>
              <input
                type="number"
                className="form-input"
                min={1}
                value={quotaMb}
                onChange={(e) => setQuotaMb(Number(e.target.value) || 1)}
              />
            </div>
            <button type="submit" className="btn btn-primary btn-sm" disabled={savingQuota}>
              <SaveRoundedIcon sx={{ fontSize: 15 }} /> Lưu
            </button>
          </form>

          {stats?.quotas?.length ? (
            <div style={{ overflowX: 'auto' }}>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Phạm vi</th>
                    <th>Đối tượng</th>
                    <th>Hạn mức</th>
                    <th>Cập nhật</th>
                  </tr>
                </thead>
                <tbody>
                  {stats.quotas.map((q) => (
                    <tr key={q.id}>
                      <td><span className="badge badge-info">{q.scopeType}</span></td>
                      <td>{q.scopeType === 'APP' ? appLabel(q.scopeId) : <code>{q.scopeId}</code>}</td>
                      <td><strong>{q.quotaMb.toLocaleString('vi-VN')} MB</strong></td>
                      <td style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>@{q.updatedBy}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Chưa có cấu hình Quota nào (đang không giới hạn).</div>
          )}
        </div>

        <div className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 12 }}>
          <strong style={{ fontSize: 14 }}>Phân bổ Dung lượng theo App &amp; Người dùng</strong>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {appEntries.length === 0 ? (
              <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Chưa có dữ liệu tệp theo App.</div>
            ) : (
              appEntries.map(([aid, bytes]) => (
                <div key={aid} style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12.5, padding: '6px 10px', borderRadius: 8, backgroundColor: 'var(--bg-secondary)' }}>
                  <span>{appLabel(aid)}</span>
                  <strong>{formatBytes(bytes)}</strong>
                </div>
              ))
            )}
          </div>
          {userEntries.length > 0 && (
            <>
              <div style={{ fontSize: 12.5, fontWeight: 700, color: 'var(--text-secondary)', marginTop: 4 }}>Top người tải lên</div>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                {userEntries.map(([uname, bytes]) => (
                  <span key={uname} className="badge" style={{ backgroundColor: 'var(--bg-secondary)', border: '1px solid var(--border-subtle)' }}>
                    @{uname}: {formatBytes(bytes)}
                  </span>
                ))}
              </div>
            </>
          )}
        </div>
      </div>

      {/* Attachment Explorer */}
      <div className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 12 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
          <strong style={{ fontSize: 14 }}>Danh sách Tệp đính kèm ({files.length})</strong>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <div style={{ position: 'relative', width: 220 }}>
              <SearchRoundedIcon sx={{ fontSize: 16, color: 'var(--text-muted)', position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)' }} />
              <input
                className="form-input"
                style={{ paddingLeft: 30, height: 34 }}
                placeholder="Tìm tên tệp, người tải..."
                value={searchQ}
                onChange={(e) => setSearchQ(e.target.value)}
              />
            </div>
            <select
              className="form-select"
              style={{ width: 190, height: 34 }}
              value={filterAppId}
              onChange={(e) => setFilterAppId(e.target.value)}
            >
              <option value="">Tất cả Ứng dụng</option>
              {apps.map((a) => (
                <option key={a.id} value={a.id}>{a.name} ({a.code})</option>
              ))}
            </select>
          </div>
        </div>

        {loading ? (
          <div style={{ padding: 24, textAlign: 'center', color: 'var(--text-muted)' }}>Đang tải danh sách tệp...</div>
        ) : files.length === 0 ? (
          <div style={{ padding: 24, textAlign: 'center', color: 'var(--text-muted)', fontSize: 13 }}>Không có tệp nào khớp điều kiện.</div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table className="data-table">
              <thead>
                <tr>
                  <th>Tên tệp</th>
                  <th>Ứng dụng</th>
                  <th>Dung lượng</th>
                  <th>Người tải</th>
                  <th>Thời điểm</th>
                  <th style={{ width: 170 }}>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {files.map((f) => (
                  <tr key={f.id}>
                    <td>
                      <div style={{ fontWeight: 600 }}>{f.fileName}</div>
                      <div style={{ fontSize: 11, color: 'var(--text-muted)' }}><code>{f.id}</code></div>
                    </td>
                    <td>{appLabel(f.appId)}</td>
                    <td>{formatBytes(f.sizeBytes)}</td>
                    <td><code>@{f.uploadedBy}</code></td>
                    <td style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                      {new Date(f.createdAt).toLocaleString('vi-VN')}
                    </td>
                    <td>
                      <div style={{ display: 'inline-flex', gap: 6 }}>
                        <a
                          href={`/api/attachments/${encodeURIComponent(f.id)}`}
                          target="_blank"
                          rel="noreferrer"
                          className="btn btn-outline btn-xs"
                        >
                          <DownloadRoundedIcon sx={{ fontSize: 13 }} /> Tải
                        </a>
                        <button
                          type="button"
                          className="btn btn-outline btn-xs"
                          disabled={sharingId === f.id}
                          onClick={() => void handleCreateShare(f.id)}
                        >
                          <ShareRoundedIcon sx={{ fontSize: 13 }} /> {sharingId === f.id ? '...' : 'Chia sẻ'}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <ConfirmDialog
        open={confirmCleanup}
        title="Dọn tệp đính kèm mồ côi"
        message={`Hệ thống sẽ xóa vĩnh viễn các tệp tải lên quá ${minAgeHours} giờ nhưng không còn gắn với bản ghi hợp lệ nào. Bạn chắc chắn muốn thực hiện?`}
        onConfirm={() => void handleCleanupOrphans()}
        onCancel={() => setConfirmCleanup(false)}
      />
    </div>
  );
}
