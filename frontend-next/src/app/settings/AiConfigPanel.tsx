'use client';

import React, { useState, useEffect, useCallback } from 'react';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded';
import StarRoundedIcon from '@mui/icons-material/StarRounded';
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded';
import VisibilityRoundedIcon from '@mui/icons-material/VisibilityRounded';
import VisibilityOffRoundedIcon from '@mui/icons-material/VisibilityOffRounded';
import SmartToyRoundedIcon from '@mui/icons-material/SmartToyRounded';
import { BooleanSwitch, StatusBadge } from '@/app/components/ui';
import {
  AiCapability,
  AiProvider,
  AiSystemSettings,
  AiTestResult,
  AiUsageLog,
} from '@/lib/types';

const PROVIDER_META: Record<string, { label: string; color: string; defaultUrl: string; defaultModel: string }> = {
  GEMINI: {
    label: 'Google Gemini',
    color: '#1a73e8',
    defaultUrl: 'https://generativelanguage.googleapis.com/v1beta',
    defaultModel: 'gemini-2.5-flash',
  },
  OPENAI: {
    label: 'OpenAI (GPT)',
    color: '#10a37f',
    defaultUrl: 'https://api.openai.com/v1',
    defaultModel: 'gpt-4o-mini',
  },
  ANTHROPIC: {
    label: 'Anthropic Claude',
    color: '#d97706',
    defaultUrl: 'https://api.anthropic.com/v1',
    defaultModel: 'claude-3-5-haiku-latest',
  },
  OPENAI_COMPATIBLE: {
    label: 'OpenAI-Compatible / Local LLM',
    color: '#6366f1',
    defaultUrl: 'http://localhost:11434/v1',
    defaultModel: 'qwen2.5:7b',
  },
};

const BUILT_IN_IDS = new Set(['ai_gemini', 'ai_openai', 'ai_anthropic', 'ai_custom']);

const DEFAULT_AI_SYS: AiSystemSettings = {
  isEnabled: true,
  defaultProviderId: 'ai_gemini',
  fallbackToRuleEngine: true,
  temperature: 0.2,
  maxTokens: 2048,
  timeoutSeconds: 45,
  systemPromptPrefix:
    'Bạn là trợ lý AI doanh nghiệp của nền tảng SmartApps. Trả lời chính xác, súc tích, ưu tiên tiếng Việt chuẩn.',
  enabledFeatures: [
    'AI_APP_COPILOT',
    'AI_RECORD_ASSISTANT',
    'AI_CHAT_COPILOT',
    'AI_WORKFLOW_TRIAGE',
    'AI_DASHBOARD_INSIGHTS',
    'AI_IMPORT_MAPPER',
    'AI_DOC_OCR',
  ],
};

export default function AiConfigPanel({
  onToast,
}: {
  onToast: (msg: string, severity?: 'success' | 'error' | 'info') => void;
}) {
  const [sysSettings, setSysSettings] = useState<AiSystemSettings>(DEFAULT_AI_SYS);
  const [providers, setProviders] = useState<AiProvider[]>([]);
  const [capabilities, setCapabilities] = useState<AiCapability[]>([]);
  const [logs, setLogs] = useState<AiUsageLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [savingGlobal, setSavingGlobal] = useState(false);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [testingId, setTestingId] = useState<string | null>(null);
  const [apiKeyDrafts, setApiKeyDrafts] = useState<Record<string, string>>({});
  const [showKeyMap, setShowKeyMap] = useState<Record<string, boolean>>({});
  const [testResults, setTestResults] = useState<Record<string, AiTestResult>>({});

  const loadAll = useCallback(async () => {
    setLoading(true);
    try {
      const res = await fetch('/api/settings/ai', { credentials: 'include' }).then((r) => r.json());
      if (res.success && res.data) {
        if (res.data.settings) setSysSettings(res.data.settings);
        if (Array.isArray(res.data.providers)) setProviders(res.data.providers);
        if (Array.isArray(res.data.capabilities)) setCapabilities(res.data.capabilities);
        if (Array.isArray(res.data.logs)) setLogs(res.data.logs);
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  const refreshLogs = async () => {
    const res = await fetch('/api/settings/ai/logs?limit=25', { credentials: 'include' }).then((r) => r.json());
    if (res.success && Array.isArray(res.data)) setLogs(res.data);
  };

  const updateProviderLocal = (id: string, patch: Partial<AiProvider>) => {
    setProviders((prev) => prev.map((p) => (p.id === id ? { ...p, ...patch } : p)));
  };

  const handleProviderTypeChange = (p: AiProvider, nextType: string) => {
    const prevMeta = PROVIDER_META[p.providerType] || PROVIDER_META.OPENAI_COMPATIBLE;
    const nextMeta = PROVIDER_META[nextType] || PROVIDER_META.OPENAI_COMPATIBLE;
    const shouldSwapUrl = !p.baseUrl || p.baseUrl === prevMeta.defaultUrl;
    const shouldSwapModel = !p.model || p.model === prevMeta.defaultModel;
    updateProviderLocal(p.id, {
      providerType: nextType,
      baseUrl: shouldSwapUrl ? nextMeta.defaultUrl : p.baseUrl,
      model: shouldSwapModel ? nextMeta.defaultModel : p.model,
    });
  };

  const handleSaveGlobal = async (nextSettings: AiSystemSettings = sysSettings) => {
    setSavingGlobal(true);
    try {
      const res = await fetch('/api/settings/ai', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(nextSettings),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setSysSettings(res.data);
        await loadAll();
        onToast('Đã lưu chính sách & cấu hình AI dùng chung toàn hệ thống!');
      } else {
        onToast(res.message || 'Lỗi lưu cấu hình AI hệ thống', 'error');
      }
    } finally {
      setSavingGlobal(false);
    }
  };

  const handleSaveProvider = async (p: AiProvider, clearApiKey = false) => {
    setSavingId(p.id);
    try {
      const res = await fetch('/api/settings/ai/providers', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          id: p.id,
          code: p.code,
          providerType: p.providerType,
          displayName: p.displayName,
          isEnabled: p.isEnabled,
          isDefault: p.isDefault,
          apiKey: clearApiKey ? null : (apiKeyDrafts[p.id]?.trim() || null),
          clearApiKey,
          baseUrl: p.baseUrl,
          model: p.model,
          temperature: p.temperature ?? null,
          maxTokens: p.maxTokens ?? null,
          timeoutSeconds: p.timeoutSeconds || 45,
          organizationId: p.organizationId || null,
          orderIndex: p.orderIndex,
        }),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setApiKeyDrafts((prev) => ({ ...prev, [p.id]: '' }));
        await loadAll();
        onToast(
          clearApiKey
            ? `Đã xóa API Key của "${res.data.displayName}"`
            : `Đã lưu cấu hình AI "${res.data.displayName}"!`
        );
      } else {
        onToast(res.message || 'Lỗi lưu cấu hình nhà cung cấp AI', 'error');
      }
    } finally {
      setSavingId(null);
    }
  };

  const handleSetDefault = async (p: AiProvider) => {
    setSavingId(p.id);
    try {
      const res = await fetch(`/api/settings/ai/providers/${encodeURIComponent(p.id)}/default`, {
        method: 'POST',
        credentials: 'include',
      }).then((r) => r.json());
      if (res.success) {
        await loadAll();
        onToast(`Đã đặt "${p.displayName}" làm nhà cung cấp AI mặc định!`);
      } else {
        onToast(res.message || 'Không thể đặt mặc định', 'error');
      }
    } finally {
      setSavingId(null);
    }
  };

  const handleTestProvider = async (p: AiProvider) => {
    setTestingId(p.id);
    try {
      const res = await fetch(`/api/settings/ai/providers/${encodeURIComponent(p.id)}/test`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          apiKey: apiKeyDrafts[p.id]?.trim() || null,
          baseUrl: p.baseUrl,
          model: p.model,
          providerType: p.providerType,
        }),
      }).then((r) => r.json());
      if (res.data) {
        setTestResults((prev) => ({ ...prev, [p.id]: res.data }));
      }
      onToast(res.message || (res.success ? 'Kết nối AI API thành công!' : 'Kết nối AI API thất bại'), res.success ? 'success' : 'error');
      await refreshLogs();
    } finally {
      setTestingId(null);
    }
  };

  const handleAddCustomProvider = async () => {
    const suffix = Math.floor(100 + Math.random() * 900);
    const code = `CUSTOM_AI_${suffix}`;
    const res = await fetch('/api/settings/ai/providers', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({
        code,
        providerType: 'OPENAI_COMPATIBLE',
        displayName: `AI Endpoint Riêng (${code})`,
        isEnabled: false,
        isDefault: false,
        baseUrl: 'https://openrouter.ai/api/v1',
        model: 'openai/gpt-4o-mini',
        timeoutSeconds: 45,
        orderIndex: providers.length + 1,
      }),
    }).then((r) => r.json());
    if (res.success) {
      await loadAll();
      onToast(`Đã thêm cấu hình AI mới: ${code}`);
    } else {
      onToast(res.message || 'Không thể thêm cấu hình AI', 'error');
    }
  };

  const handleDeleteProvider = async (p: AiProvider) => {
    const res = await fetch(`/api/settings/ai/providers/${encodeURIComponent(p.id)}`, {
      method: 'DELETE',
      credentials: 'include',
    }).then((r) => r.json());
    if (res.success) {
      await loadAll();
      onToast(`Đã xóa cấu hình ${p.displayName}`);
    } else {
      onToast(res.message || 'Không thể xóa', 'error');
    }
  };

  const toggleFeature = (code: string) => {
    const current = new Set(sysSettings.enabledFeatures || []);
    if (current.has(code)) current.delete(code);
    else current.add(code);
    setSysSettings((s) => ({ ...s, enabledFeatures: Array.from(current) }));
  };

  if (loading) {
    return (
      <div style={{ padding: 36, textAlign: 'center', color: 'var(--text-muted)' }}>
        Đang tải cấu hình API AI toàn hệ thống...
      </div>
    );
  }

  const activeDefault =
    providers.find((p) => p.id === sysSettings.defaultProviderId && p.isEnabled && p.isConfigured) ||
    providers.find((p) => p.isDefault && p.isEnabled && p.isConfigured) ||
    providers.find((p) => p.isEnabled && p.isConfigured);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {/* Header */}
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
          <h2 style={{ fontSize: 17, fontWeight: 700, margin: 0, display: 'flex', alignItems: 'center', gap: 8 }}>
            <AutoAwesomeRoundedIcon sx={{ fontSize: 20, color: 'var(--accent-primary)' }} />
            Cấu hình API AI Dùng chung Toàn Hệ thống
          </h2>
          <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 4, lineHeight: 1.5 }}>
            Kết nối một lần tại đây (Google Gemini, OpenAI, Anthropic Claude hoặc Local/OpenAI-Compatible LLM) để cấp
            năng lực AI đồng bộ cho App Builder Studio, Hồ sơ 360°, Chat &amp; Giao việc, Phê duyệt, Dashboard và Import Excel.
          </p>
        </div>
        <button type="button" className="btn btn-primary btn-sm" onClick={handleAddCustomProvider}>
          <AddRoundedIcon sx={{ fontSize: 16 }} />
          Thêm Cấu hình AI riêng
        </button>
      </div>

      {/* Live Gateway Status Banner */}
      <div
        style={{
          padding: '12px 16px',
          borderRadius: 12,
          border: '1px solid var(--border-light)',
          backgroundColor: sysSettings.isEnabled && activeDefault ? '#e6f4ea' : 'var(--bg-primary)',
          color: sysSettings.isEnabled && activeDefault ? '#137333' : 'var(--text-primary)',
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 10,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <SmartToyRoundedIcon sx={{ fontSize: 22 }} />
          <div style={{ fontSize: 13 }}>
            {sysSettings.isEnabled && activeDefault ? (
              <>
                <strong>AI Gateway Đang Hoạt Động:</strong> {activeDefault.displayName} • Model{' '}
                <code>{activeDefault.model}</code>
              </>
            ) : !sysSettings.isEnabled ? (
              <>
                <strong>AI Gateway Đang Tắt:</strong> Các chức năng sẽ dùng Rule Engine nội bộ (nếu bật Fallback).
              </>
            ) : (
              <>
                <strong>Chưa có API AI nào sẵn sàng:</strong> Hãy nhập API Key (hoặc URL Ollama/vLLM), bật công tắc
                kích hoạt và nhấn <em>Lưu cấu hình</em> ở một nhà cung cấp bên dưới.
              </>
            )}
          </div>
        </div>
        <span className="badge badge-info">
          {(sysSettings.enabledFeatures || []).length} / {capabilities.length} chức năng AI đang bật
        </span>
      </div>

      {/* Global Shared AI Policy */}
      <div
        style={{
          padding: 18,
          borderRadius: 14,
          backgroundColor: 'var(--bg-primary)',
          border: '1px solid var(--border-light)',
          display: 'flex',
          flexDirection: 'column',
          gap: 14,
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
          <strong style={{ fontSize: 14.5 }}>1. Chính sách &amp; Tham số Mặc định Dùng chung</strong>
          <div style={{ display: 'flex', alignItems: 'center', gap: 16, flexWrap: 'wrap' }}>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, fontSize: 12.5, cursor: 'pointer' }}>
              <BooleanSwitch
                checked={sysSettings.isEnabled}
                onChange={(v) => setSysSettings((s) => ({ ...s, isEnabled: v }))}
                size="sm"
                trueLabel="Bật"
                falseLabel="Tắt"
              />
              <span>Bật AI toàn hệ thống</span>
            </label>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, fontSize: 12.5, cursor: 'pointer' }}>
              <BooleanSwitch
                checked={sysSettings.fallbackToRuleEngine}
                onChange={(v) => setSysSettings((s) => ({ ...s, fallbackToRuleEngine: v }))}
                size="sm"
                trueLabel="Bật"
                falseLabel="Tắt"
              />
              <span>Fallback Rule Engine khi API lỗi</span>
            </label>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(190px, 1fr))', gap: 12 }}>
          <div>
            <label className="form-label" style={{ fontSize: 11.5 }}>Nhà cung cấp AI mặc định</label>
            <select
              className="form-select"
              value={sysSettings.defaultProviderId || ''}
              onChange={(e) => setSysSettings((s) => ({ ...s, defaultProviderId: e.target.value }))}
            >
              {providers.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.displayName} ({p.model}) {p.isEnabled && p.isConfigured ? '✓' : '— Chưa bật'}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="form-label" style={{ fontSize: 11.5 }}>Temperature mặc định (0 – 2)</label>
            <input
              type="number"
              step="0.1"
              min={0}
              max={2}
              className="form-input"
              value={sysSettings.temperature}
              onChange={(e) => setSysSettings((s) => ({ ...s, temperature: Number(e.target.value) || 0.2 }))}
            />
          </div>
          <div>
            <label className="form-label" style={{ fontSize: 11.5 }}>Max Tokens mặc định</label>
            <input
              type="number"
              min={128}
              max={32768}
              className="form-input"
              value={sysSettings.maxTokens}
              onChange={(e) => setSysSettings((s) => ({ ...s, maxTokens: Number(e.target.value) || 2048 }))}
            />
          </div>
          <div>
            <label className="form-label" style={{ fontSize: 11.5 }}>Timeout (giây)</label>
            <input
              type="number"
              min={5}
              max={180}
              className="form-input"
              value={sysSettings.timeoutSeconds}
              onChange={(e) => setSysSettings((s) => ({ ...s, timeoutSeconds: Number(e.target.value) || 45 }))}
            />
          </div>
        </div>

        <div>
          <label className="form-label" style={{ fontSize: 11.5 }}>
            Ngữ cảnh Hệ thống (System Prompt nền tảng áp dụng cho mọi tính năng AI)
          </label>
          <textarea
            className="form-textarea"
            rows={2}
            value={sysSettings.systemPromptPrefix}
            onChange={(e) => setSysSettings((s) => ({ ...s, systemPromptPrefix: e.target.value }))}
          />
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={savingGlobal}
            onClick={() => handleSaveGlobal()}
          >
            <SaveRoundedIcon sx={{ fontSize: 16 }} />
            {savingGlobal ? 'Đang lưu...' : 'Lưu chính sách AI chung'}
          </button>
        </div>
      </div>

      {/* Providers Grid */}
      <div>
        <div style={{ fontSize: 14.5, fontWeight: 700, marginBottom: 10 }}>
          2. Nhà cung cấp &amp; API Key (Multi-Provider)
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(380px, 1fr))', gap: 16 }}>
          {providers.map((p) => {
            const meta = PROVIDER_META[p.providerType] || PROVIDER_META.OPENAI_COMPATIBLE;
            const isBuiltIn = BUILT_IN_IDS.has(p.id);
            const testRes = testResults[p.id];
            const showKey = Boolean(showKeyMap[p.id]);

            return (
              <div
                key={p.id}
                style={{
                  padding: 18,
                  borderRadius: 12,
                  backgroundColor: 'var(--bg-primary)',
                  border: '1px solid var(--border-light)',
                  borderTop: `4px solid ${meta.color}`,
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 12,
                  minWidth: 0,
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 10 }}>
                  <div style={{ minWidth: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                      <strong style={{ fontSize: 14.5 }}>{p.displayName}</strong>
                      <span className="badge badge-info">{p.code}</span>
                      {p.isDefault && (
                        <span className="badge" style={{ backgroundColor: '#fef7e0', color: '#b06000' }}>
                          <StarRoundedIcon sx={{ fontSize: 12 }} /> Mặc định
                        </span>
                      )}
                      {p.isEnabled && p.isConfigured ? (
                        <span className="badge" style={{ backgroundColor: '#e6f4ea', color: '#137333' }}>
                          <CheckCircleRoundedIcon sx={{ fontSize: 12 }} /> Sẵn sàng
                        </span>
                      ) : (
                        <span className="badge" style={{ backgroundColor: 'var(--bg-secondary)', color: 'var(--text-muted)' }}>
                          {p.isEnabled ? 'Thiếu API Key' : 'Đang tắt'}
                        </span>
                      )}
                    </div>
                  </div>
                  <BooleanSwitch
                    checked={p.isEnabled}
                    onChange={(v) => updateProviderLocal(p.id, { isEnabled: v })}
                    trueLabel="Bật"
                    falseLabel="Tắt"
                    size="sm"
                  />
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 10 }}>
                  <div>
                    <label className="form-label" style={{ fontSize: 11.5 }}>Tên hiển thị</label>
                    <input
                      className="form-input"
                      value={p.displayName}
                      onChange={(e) => updateProviderLocal(p.id, { displayName: e.target.value })}
                    />
                  </div>
                  <div>
                    <label className="form-label" style={{ fontSize: 11.5 }}>Giao thức API</label>
                    <select
                      className="form-select"
                      value={p.providerType}
                      onChange={(e) => handleProviderTypeChange(p, e.target.value)}
                    >
                      <option value="GEMINI">Google Gemini</option>
                      <option value="OPENAI">OpenAI (GPT)</option>
                      <option value="ANTHROPIC">Anthropic Claude</option>
                      <option value="OPENAI_COMPATIBLE">OpenAI-Compatible / Local LLM</option>
                    </select>
                  </div>
                </div>

                <div>
                  <label className="form-label" style={{ fontSize: 11.5 }}>Base URL Endpoint</label>
                  <input
                    className="form-input"
                    value={p.baseUrl}
                    onChange={(e) => updateProviderLocal(p.id, { baseUrl: e.target.value })}
                    placeholder={meta.defaultUrl}
                  />
                </div>

                <div>
                  <label className="form-label" style={{ fontSize: 11.5 }}>Model sử dụng</label>
                  <input
                    className="form-input"
                    value={p.model}
                    onChange={(e) => updateProviderLocal(p.id, { model: e.target.value })}
                    placeholder={meta.defaultModel}
                  />
                  {p.suggestedModels?.length > 0 && (
                    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginTop: 6 }}>
                      {p.suggestedModels.map((m) => (
                        <button
                          key={m}
                          type="button"
                          className={`btn btn-xs ${p.model === m ? 'btn-primary' : 'btn-outline'}`}
                          onClick={() => updateProviderLocal(p.id, { model: m })}
                        >
                          {m}
                        </button>
                      ))}
                    </div>
                  )}
                </div>

                <div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <label className="form-label" style={{ fontSize: 11.5 }}>
                      API Key{' '}
                      {p.hasApiKey ? (
                        <span style={{ color: '#137333' }}>
                          (Đã lưu: <code>{p.apiKeyPreview}</code>)
                        </span>
                      ) : p.providerType === 'OPENAI_COMPATIBLE' ? (
                        <span style={{ color: 'var(--text-muted)' }}>(Tùy chọn cho Local Ollama/vLLM)</span>
                      ) : null}
                    </label>
                    {p.hasApiKey && (
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        style={{ color: '#d93025', marginBottom: 4 }}
                        onClick={() => handleSaveProvider(p, true)}
                      >
                        Xóa Key
                      </button>
                    )}
                  </div>
                  <div style={{ display: 'flex', gap: 6 }}>
                    <input
                      type={showKey ? 'text' : 'password'}
                      className="form-input"
                      style={{ flex: 1 }}
                      placeholder={
                        p.hasApiKey
                          ? '•••••••• (Để trống nếu giữ nguyên key đã lưu)'
                          : 'Nhập API Key...'
                      }
                      value={apiKeyDrafts[p.id] || ''}
                      onChange={(e) => setApiKeyDrafts((d) => ({ ...d, [p.id]: e.target.value }))}
                    />
                    <button
                      type="button"
                      className="btn btn-outline btn-sm"
                      onClick={() => setShowKeyMap((m) => ({ ...m, [p.id]: !showKey }))}
                      title={showKey ? 'Ẩn API Key' : 'Hiện API Key'}
                      aria-label={showKey ? 'Ẩn API Key' : 'Hiện API Key'}
                    >
                      {showKey ? <VisibilityOffRoundedIcon sx={{ fontSize: 16 }} /> : <VisibilityRoundedIcon sx={{ fontSize: 16 }} />}
                    </button>
                  </div>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 8 }}>
                  <div>
                    <label className="form-label" style={{ fontSize: 11 }}>Temp riêng (tùy chọn)</label>
                    <input
                      type="number"
                      step="0.1"
                      min={0}
                      max={2}
                      className="form-input"
                      placeholder="Mặc định"
                      value={p.temperature ?? ''}
                      onChange={(e) =>
                        updateProviderLocal(p.id, {
                          temperature: e.target.value === '' ? null : Number(e.target.value),
                        })
                      }
                    />
                  </div>
                  <div>
                    <label className="form-label" style={{ fontSize: 11 }}>MaxTokens riêng</label>
                    <input
                      type="number"
                      min={128}
                      max={32768}
                      className="form-input"
                      placeholder="Mặc định"
                      value={p.maxTokens ?? ''}
                      onChange={(e) =>
                        updateProviderLocal(p.id, {
                          maxTokens: e.target.value === '' ? null : Number(e.target.value),
                        })
                      }
                    />
                  </div>
                  <div>
                    <label className="form-label" style={{ fontSize: 11 }}>Timeout (s)</label>
                    <input
                      type="number"
                      min={5}
                      max={180}
                      className="form-input"
                      value={p.timeoutSeconds}
                      onChange={(e) =>
                        updateProviderLocal(p.id, { timeoutSeconds: Number(e.target.value) || 45 })
                      }
                    />
                  </div>
                </div>

                {testRes && (
                  <div
                    style={{
                      padding: '10px 12px',
                      borderRadius: 10,
                      fontSize: 12,
                      backgroundColor: testRes.ok ? '#e6f4ea' : '#fce8e6',
                      color: testRes.ok ? '#137333' : '#c5221f',
                      lineHeight: 1.45,
                    }}
                  >
                    <div>
                      <strong>{testRes.ok ? '✓ Thành công' : '✕ Thất bại'}</strong> ({testRes.latencyMs} ms):{' '}
                      {testRes.message}
                    </div>
                    {testRes.replyPreview && (
                      <div style={{ marginTop: 4, fontStyle: 'italic' }}>
                        Phản hồi: &ldquo;{testRes.replyPreview}&rdquo;
                      </div>
                    )}
                  </div>
                )}

                <div
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    flexWrap: 'wrap',
                    gap: 8,
                    paddingTop: 8,
                    borderTop: '1px solid var(--border-light)',
                  }}
                >
                  <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                    {!p.isDefault && (
                      <button
                        type="button"
                        className="btn btn-outline btn-xs"
                        disabled={savingId === p.id}
                        onClick={() => handleSetDefault(p)}
                      >
                        <StarRoundedIcon sx={{ fontSize: 14 }} />
                        Đặt mặc định
                      </button>
                    )}
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      disabled={testingId === p.id}
                      onClick={() => handleTestProvider(p)}
                    >
                      <PlayArrowRoundedIcon sx={{ fontSize: 14 }} />
                      {testingId === p.id ? 'Đang kiểm tra...' : 'Kiểm tra API'}
                    </button>
                  </div>

                  <div style={{ display: 'flex', gap: 6 }}>
                    {!isBuiltIn && (
                      <button
                        type="button"
                        className="btn btn-outline btn-sm"
                        style={{ color: '#d93025' }}
                        onClick={() => handleDeleteProvider(p)}
                      >
                        <DeleteOutlineRoundedIcon sx={{ fontSize: 16 }} />
                        Xóa
                      </button>
                    )}
                    <button
                      type="button"
                      className="btn btn-primary btn-sm"
                      disabled={savingId === p.id}
                      onClick={() => handleSaveProvider(p)}
                    >
                      <SaveRoundedIcon sx={{ fontSize: 16 }} />
                      {savingId === p.id ? 'Đang lưu...' : 'Lưu'}
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* AI Capabilities Map & Proposals */}
      <div
        style={{
          padding: 18,
          borderRadius: 14,
          backgroundColor: 'var(--bg-primary)',
          border: '1px solid var(--border-light)',
          display: 'flex',
          flexDirection: 'column',
          gap: 14,
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
          <div>
            <strong style={{ fontSize: 14.5 }}>
              3. Bản đồ &amp; Đề xuất Các Chức năng AI Ứng dụng trong Hệ thống
            </strong>
            <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
              Tất cả các tính năng dưới đây đều dùng chung cấu hình AI Gateway phía trên. Bật/tắt quyền sử dụng từng
              chức năng rồi nhấn &ldquo;Lưu phân quyền tính năng AI&rdquo;.
            </div>
          </div>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={savingGlobal}
            onClick={() => handleSaveGlobal()}
          >
            <SaveRoundedIcon sx={{ fontSize: 15 }} />
            Lưu phân quyền tính năng AI
          </button>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(310px, 1fr))', gap: 12 }}>
          {capabilities.map((cap) => {
            const enabled = (sysSettings.enabledFeatures || []).includes(cap.code);
            return (
              <div
                key={cap.code}
                style={{
                  padding: 14,
                  borderRadius: 12,
                  backgroundColor: 'var(--bg-surface)',
                  border: '1px solid var(--border-light)',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 8,
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8 }}>
                  <div>
                    <div style={{ fontSize: 13.5, fontWeight: 700 }}>{cap.name}</div>
                    <div style={{ fontSize: 11.5, color: 'var(--accent-primary)', fontWeight: 600 }}>
                      {cap.module}
                    </div>
                  </div>
                  <BooleanSwitch
                    checked={enabled}
                    onChange={() => toggleFeature(cap.code)}
                    size="sm"
                    trueLabel="Bật"
                    falseLabel="Tắt"
                  />
                </div>

                <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                  {cap.status === 'INTEGRATED' ? (
                    <span className="badge" style={{ backgroundColor: '#e6f4ea', color: '#137333' }}>
                      Đã tích hợp API dùng chung
                    </span>
                  ) : (
                    <span className="badge badge-info">Đề xuất ứng dụng</span>
                  )}
                  <span className="badge" style={{ backgroundColor: 'var(--bg-primary)', color: 'var(--text-secondary)' }}>
                    Model gợi ý: {cap.recommendedTier}
                  </span>
                </div>

                <div style={{ fontSize: 12, color: 'var(--text-secondary)', lineHeight: 1.45 }}>
                  {cap.description}
                </div>
                <div style={{ fontSize: 11.5, color: 'var(--text-muted)', lineHeight: 1.4 }}>
                  <strong>Giá trị:</strong> {cap.benefit}
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* AI Usage Logs */}
      <div
        style={{
          padding: 18,
          borderRadius: 14,
          backgroundColor: 'var(--bg-primary)',
          border: '1px solid var(--border-light)',
          display: 'flex',
          flexDirection: 'column',
          gap: 12,
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
          <strong style={{ fontSize: 14.5 }}>4. Nhật ký Gọi API AI Gần đây</strong>
          <button type="button" className="btn btn-outline btn-xs" onClick={refreshLogs}>
            <RefreshRoundedIcon sx={{ fontSize: 14 }} />
            Làm mới
          </button>
        </div>

        {logs.length === 0 ? (
          <div style={{ fontSize: 12.5, color: 'var(--text-muted)', padding: '12px 0' }}>
            Chưa có lượt gọi API AI nào được ghi nhận. Nhấn &ldquo;Kiểm tra API&rdquo; ở một nhà cung cấp để thử nghiệm.
          </div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table className="data-table" style={{ width: '100%', fontSize: 12 }}>
              <thead>
                <tr>
                  <th>Thời gian</th>
                  <th>Chức năng</th>
                  <th>Provider / Model</th>
                  <th>Người dùng</th>
                  <th>Trạng thái</th>
                  <th>Độ trễ</th>
                  <th>Tokens</th>
                  <th>Chi tiết</th>
                </tr>
              </thead>
              <tbody>
                {logs.map((log) => (
                  <tr key={log.id}>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      {new Date(log.createdAt).toLocaleString('vi-VN')}
                    </td>
                    <td><code>{log.featureCode}</code></td>
                    <td>
                      <div><strong>{log.providerName}</strong></div>
                      <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{log.model}</div>
                    </td>
                    <td>@{log.username}</td>
                    <td>
                      <StatusBadge
                        status={log.status === 'SUCCESS' ? 'APPROVED' : 'REJECTED'}
                        label={log.status}
                      />
                    </td>
                    <td>{log.latencyMs} ms</td>
                    <td>{log.promptTokens + log.completionTokens}</td>
                    <td style={{ maxWidth: 260, wordBreak: 'break-word' }}>{log.message || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
