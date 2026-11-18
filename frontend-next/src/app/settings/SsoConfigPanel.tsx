'use client';

import React, { useState, useEffect, useCallback } from 'react';
import KeyRoundedIcon from '@mui/icons-material/KeyRounded';
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded';
import AutoFixHighRoundedIcon from '@mui/icons-material/AutoFixHighRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import { BooleanSwitch } from '@/app/components/ui';
import { SsoProviderAdmin, SysRole } from '@/lib/types';

export default function SsoConfigPanel({
  roles,
  onToast,
}: {
  roles: SysRole[];
  onToast: (msg: string, severity?: 'success' | 'error' | 'info') => void;
}) {
  const [providers, setProviders] = useState<SsoProviderAdmin[]>([]);
  const [loading, setLoading] = useState(true);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [discoveringId, setDiscoveringId] = useState<string | null>(null);
  const [secrets, setSecrets] = useState<Record<string, string>>({});
  const [domainsText, setDomainsText] = useState<Record<string, string>>({});
  const [origin, setOrigin] = useState('http://localhost:4100');

  const loadProviders = useCallback(async () => {
    setLoading(true);
    try {
      const res = await fetch('/api/auth/sso/admin/providers', { credentials: 'include' }).then((r) => r.json());
      if (res.success && Array.isArray(res.data)) {
        setProviders(res.data);
        const domMap: Record<string, string> = {};
        for (const p of res.data as SsoProviderAdmin[]) {
          domMap[p.id] = (p.allowedDomains || []).join(', ');
        }
        setDomainsText(domMap);
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (typeof window !== 'undefined') setOrigin(window.location.origin);
    loadProviders();
  }, [loadProviders]);

  const updateLocal = (id: string, patch: Partial<SsoProviderAdmin>) => {
    setProviders((prev) => prev.map((p) => (p.id === id ? { ...p, ...patch } : p)));
  };

  const handleSave = async (p: SsoProviderAdmin) => {
    setSavingId(p.id);
    try {
      const domains = (domainsText[p.id] || '')
        .split(/[\s,;]+/)
        .map((d) => d.trim().replace(/^@/, '').toLowerCase())
        .filter(Boolean);

      const res = await fetch('/api/auth/sso/admin/providers', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          id: p.id,
          code: p.code,
          providerType: p.providerType,
          displayName: p.displayName,
          isEnabled: p.isEnabled,
          clientId: p.clientId || null,
          clientSecret: secrets[p.id]?.trim() ? secrets[p.id].trim() : null,
          tenantId: p.tenantId || null,
          issuerUrl: p.issuerUrl || null,
          authorizationUrl: p.authorizationUrl || null,
          tokenUrl: p.tokenUrl || null,
          userinfoUrl: p.userinfoUrl || null,
          scopes: p.scopes || 'openid email profile',
          allowedDomains: domains,
          autoProvision: p.autoProvision,
          defaultRoleCode: p.defaultRoleCode || 'STAFF',
          buttonColor: p.buttonColor || '#1a73e8',
          orderIndex: p.orderIndex,
        }),
      }).then((r) => r.json());

      if (res.success && res.data) {
        setProviders((prev) => prev.map((x) => (x.id === p.id ? res.data : x)));
        setSecrets((s) => ({ ...s, [p.id]: '' }));
        onToast(`Đã lưu cấu hình SSO "${res.data.displayName}"!`);
      } else {
        onToast(res.message || 'Lỗi lưu cấu hình SSO', 'error');
      }
    } finally {
      setSavingId(null);
    }
  };

  const handleDiscover = async (p: SsoProviderAdmin) => {
    if (!p.issuerUrl?.trim()) {
      onToast('Vui lòng nhập Issuer URL trước khi dò OIDC Discovery.', 'error');
      return;
    }
    setDiscoveringId(p.id);
    try {
      const res = await fetch('/api/auth/sso/admin/discover', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ issuerUrl: p.issuerUrl.trim() }),
      }).then((r) => r.json());

      if (res.success && res.data) {
        updateLocal(p.id, {
          issuerUrl: res.data.issuer || p.issuerUrl,
          authorizationUrl: res.data.authorizationUrl,
          tokenUrl: res.data.tokenUrl,
          userinfoUrl: res.data.userinfoUrl || p.userinfoUrl,
        });
        onToast('Đã tự động điền Endpoint từ OIDC Discovery! Nhấn Lưu để áp dụng.');
      } else {
        onToast(res.message || 'Không dò được OIDC Discovery', 'error');
      }
    } finally {
      setDiscoveringId(null);
    }
  };

  const handleAddCustomProvider = async () => {
    const suffix = Math.floor(100 + Math.random() * 900);
    const code = `CORP_SSO_${suffix}`;
    const res = await fetch('/api/auth/sso/admin/providers', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({
        code,
        providerType: 'CUSTOM_OIDC',
        displayName: `SSO Riêng (${code})`,
        isEnabled: false,
        scopes: 'openid email profile',
        autoProvision: true,
        defaultRoleCode: 'STAFF',
        buttonColor: '#00897b',
        orderIndex: providers.length + 1,
      }),
    }).then((r) => r.json());

    if (res.success) {
      await loadProviders();
      onToast(`Đã tạo nhà cung cấp SSO mới: ${code}`);
    } else {
      onToast(res.message || 'Không thể thêm SSO', 'error');
    }
  };

  const handleDelete = async (p: SsoProviderAdmin) => {
    const res = await fetch(`/api/auth/sso/admin/providers/${encodeURIComponent(p.id)}`, {
      method: 'DELETE',
      credentials: 'include',
    }).then((r) => r.json());
    if (res.success) {
      setProviders((prev) => prev.filter((x) => x.id !== p.id));
      onToast(`Đã xóa nhà cung cấp ${p.displayName}`);
    } else {
      onToast(res.message || 'Không thể xóa', 'error');
    }
  };

  const copyCallback = (code: string) => {
    const uri = `${origin}/api/auth/sso/callback/${code}`;
    navigator.clipboard?.writeText(uri);
    onToast(`Đã sao chép Redirect URI: ${uri}`, 'info');
  };

  if (loading) {
    return (
      <div style={{ padding: 36, textAlign: 'center', color: 'var(--text-muted)' }}>
        Đang tải cấu hình Đăng nhập SSO...
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
      <div
        style={{
          borderBottom: '1px solid var(--border-light)',
          paddingBottom: 14,
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 12,
        }}
      >
        <div>
          <h2 style={{ fontSize: 17, fontWeight: 700, margin: 0 }}>
            Đăng nhập Một lần — SSO (Google, Microsoft &amp; OIDC Riêng)
          </h2>
          <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 4, lineHeight: 1.5 }}>
            Hỗ trợ OAuth 2.0 + OIDC + PKCE (S256). Tài khoản SSO tự động liên kết theo{' '}
            <code style={{ padding: '1px 5px', borderRadius: 4, backgroundColor: 'var(--bg-primary)', fontSize: 11.5 }}>
              email
            </code>{' '}
            vào bảng{' '}
            <code style={{ padding: '1px 5px', borderRadius: 4, backgroundColor: 'var(--bg-primary)', fontSize: 11.5 }}>
              sys_users
            </code>{' '}
            dùng chung và áp dụng đúng Vai trò &amp; Quyền đa App.
          </p>
        </div>
        <button type="button" className="btn btn-primary btn-sm" onClick={handleAddCustomProvider}>
          <AddRoundedIcon sx={{ fontSize: 16 }} />
          Thêm Nhà cung cấp SSO riêng
        </button>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(380px, 1fr))', gap: 16 }}>
        {providers.map((p) => {
          const callbackUrl = `${origin}/api/auth/sso/callback/${p.code}`;
          const isBuiltInPreset = p.id === 'sso_google' || p.id === 'sso_microsoft' || p.id === 'sso_custom';
          return (
            <div
              key={p.id}
              style={{
                padding: 18,
                borderRadius: 12,
                backgroundColor: 'var(--bg-primary)',
                border: '1px solid var(--border-light)',
                borderTop: `4px solid ${p.buttonColor || '#1a73e8'}`,
                display: 'flex',
                flexDirection: 'column',
                gap: 14,
                minWidth: 0,
              }}
            >
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 10 }}>
                <div style={{ minWidth: 0 }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                    <KeyRoundedIcon sx={{ fontSize: 18, color: p.buttonColor || '#1a73e8' }} />
                    <strong style={{ fontSize: 15 }}>{p.displayName}</strong>
                    <span className="badge badge-info">{p.code}</span>
                    {p.isEnabled && p.isConfigured ? (
                      <span className="badge" style={{ backgroundColor: '#e6f4ea', color: '#137333' }}>
                        <CheckCircleRoundedIcon sx={{ fontSize: 12 }} /> Đang hoạt động
                      </span>
                    ) : (
                      <span className="badge" style={{ backgroundColor: 'var(--bg-secondary)', color: 'var(--text-muted)' }}>
                        {p.isEnabled ? 'Thiếu Client ID' : 'Đang tắt'}
                      </span>
                    )}
                  </div>
                </div>
                <BooleanSwitch
                  checked={p.isEnabled}
                  onChange={(v) => updateLocal(p.id, { isEnabled: v })}
                  trueLabel="Bật"
                  falseLabel="Tắt"
                  size="sm"
                />
              </div>

              {/* Callback Redirect URI */}
              <div
                style={{
                  padding: '8px 10px',
                  borderRadius: 10,
                  backgroundColor: 'var(--bg-surface)',
                  border: '1px solid var(--border-light)',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  gap: 8,
                }}
              >
                <div style={{ minWidth: 0 }}>
                  <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-muted)' }}>
                    Redirect URI (dán vào Google Cloud / Azure Portal / IdP):
                  </div>
                  <code style={{ fontSize: 11.5, wordBreak: 'break-all', color: '#1a73e8' }}>{callbackUrl}</code>
                </div>
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  onClick={() => copyCallback(p.code)}
                  title="Sao chép Redirect URI"
                  aria-label="Sao chép Redirect URI"
                >
                  <ContentCopyRoundedIcon sx={{ fontSize: 13 }} />
                </button>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 10 }}>
                <div>
                  <label className="form-label" style={{ fontSize: 11.5 }}>Tên hiển thị nút đăng nhập</label>
                  <input
                    className="form-input"
                    value={p.displayName}
                    onChange={(e) => updateLocal(p.id, { displayName: e.target.value })}
                  />
                </div>
                <div>
                  <label className="form-label" style={{ fontSize: 11.5 }}>Client ID</label>
                  <input
                    className="form-input"
                    placeholder="Nhập Client ID..."
                    value={p.clientId || ''}
                    onChange={(e) => updateLocal(p.id, { clientId: e.target.value })}
                  />
                </div>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: p.providerType === 'MICROSOFT' ? 'repeat(auto-fit, minmax(170px, 1fr))' : '1fr', gap: 10 }}>
                <div>
                  <label className="form-label" style={{ fontSize: 11.5 }}>
                    Client Secret {p.hasClientSecret && <span style={{ color: '#137333' }}>(Đã lưu Secret)</span>}
                  </label>
                  <input
                    type="password"
                    className="form-input"
                    placeholder={p.hasClientSecret ? '•••••••• (Để trống nếu giữ nguyên)' : 'Nhập Client Secret...'}
                    value={secrets[p.id] || ''}
                    onChange={(e) => setSecrets((s) => ({ ...s, [p.id]: e.target.value }))}
                  />
                </div>
                {p.providerType === 'MICROSOFT' && (
                  <div>
                    <label className="form-label" style={{ fontSize: 11.5 }}>Tenant ID (common hoặc GUID tổ chức)</label>
                    <input
                      className="form-input"
                      placeholder="common"
                      value={p.tenantId || 'common'}
                      onChange={(e) => updateLocal(p.id, { tenantId: e.target.value })}
                    />
                  </div>
                )}
              </div>

              {p.providerType === 'CUSTOM_OIDC' && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 10, padding: 12, borderRadius: 10, backgroundColor: 'var(--bg-surface)', border: '1px solid var(--border-light)' }}>
                  <div style={{ display: 'flex', gap: 8, alignItems: 'flex-end', flexWrap: 'wrap' }}>
                    <div style={{ flex: '1 1 220px' }}>
                      <label className="form-label" style={{ fontSize: 11.5 }}>
                        Issuer URL (Keycloak / Okta / Authentik / Zitadel / ADFS)
                      </label>
                      <input
                        className="form-input"
                        placeholder="https://sso.company.vn/realms/corp"
                        value={p.issuerUrl || ''}
                        onChange={(e) => updateLocal(p.id, { issuerUrl: e.target.value })}
                      />
                    </div>
                    <button
                      type="button"
                      className="btn btn-outline btn-sm"
                      disabled={discoveringId === p.id}
                      onClick={() => handleDiscover(p)}
                      title="Tự động tải authorization_endpoint, token_endpoint, userinfo_endpoint"
                    >
                      <AutoFixHighRoundedIcon sx={{ fontSize: 15 }} />
                      {discoveringId === p.id ? 'Đang dò...' : 'Dò Discovery'}
                    </button>
                  </div>

                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 8 }}>
                    <div>
                      <label className="form-label" style={{ fontSize: 11 }}>Authorization URL</label>
                      <input
                        className="form-input"
                        placeholder="https://.../authorize"
                        value={p.authorizationUrl || ''}
                        onChange={(e) => updateLocal(p.id, { authorizationUrl: e.target.value })}
                      />
                    </div>
                    <div>
                      <label className="form-label" style={{ fontSize: 11 }}>Token URL</label>
                      <input
                        className="form-input"
                        placeholder="https://.../token"
                        value={p.tokenUrl || ''}
                        onChange={(e) => updateLocal(p.id, { tokenUrl: e.target.value })}
                      />
                    </div>
                    <div>
                      <label className="form-label" style={{ fontSize: 11 }}>UserInfo URL</label>
                      <input
                        className="form-input"
                        placeholder="https://.../userinfo"
                        value={p.userinfoUrl || ''}
                        onChange={(e) => updateLocal(p.id, { userinfoUrl: e.target.value })}
                      />
                    </div>
                    <div>
                      <label className="form-label" style={{ fontSize: 11 }}>Scopes</label>
                      <input
                        className="form-input"
                        value={p.scopes || 'openid email profile'}
                        onChange={(e) => updateLocal(p.id, { scopes: e.target.value })}
                      />
                    </div>
                  </div>
                </div>
              )}

              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 10 }}>
                <div>
                  <label className="form-label" style={{ fontSize: 11.5 }}>
                    Giới hạn tên miền Email (để trống = mọi domain)
                  </label>
                  <input
                    className="form-input"
                    placeholder="company.vn, subsidiary.com"
                    value={domainsText[p.id] || ''}
                    onChange={(e) => setDomainsText((d) => ({ ...d, [p.id]: e.target.value }))}
                  />
                </div>
                <div>
                  <label className="form-label" style={{ fontSize: 11.5 }}>Vai trò mặc định khi tự tạo User</label>
                  <select
                    className="form-select"
                    value={p.defaultRoleCode || 'STAFF'}
                    onChange={(e) => updateLocal(p.id, { defaultRoleCode: e.target.value })}
                  >
                    {roles.length === 0 ? (
                      <option value="STAFF">STAFF</option>
                    ) : (
                      roles.map((r) => (
                        <option key={r.id} value={r.code}>
                          {r.name} ({r.code})
                        </option>
                      ))
                    )}
                  </select>
                </div>
              </div>

              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10, paddingTop: 8, borderTop: '1px solid var(--border-light)' }}>
                <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, cursor: 'pointer', fontSize: 12.5 }}>
                  <BooleanSwitch
                    checked={p.autoProvision}
                    onChange={(v) => updateLocal(p.id, { autoProvision: v })}
                    size="sm"
                  />
                  <span>Tự tạo tài khoản công ty nếu email chưa tồn tại</span>
                </label>

                <div style={{ display: 'flex', gap: 8 }}>
                  {!isBuiltInPreset && (
                    <button
                      type="button"
                      className="btn btn-outline btn-sm"
                      style={{ color: '#d93025' }}
                      onClick={() => handleDelete(p)}
                    >
                      <DeleteOutlineRoundedIcon sx={{ fontSize: 16 }} />
                      Xóa
                    </button>
                  )}
                  <button
                    type="button"
                    className="btn btn-primary btn-sm"
                    disabled={savingId === p.id}
                    onClick={() => handleSave(p)}
                  >
                    <SaveRoundedIcon sx={{ fontSize: 16 }} />
                    {savingId === p.id ? 'Đang lưu...' : 'Lưu cấu hình'}
                  </button>
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
