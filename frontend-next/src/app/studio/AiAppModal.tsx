'use client';

import React, { useState, useEffect } from 'react';
import Link from 'next/link';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded';
import { AiStatus, SysApp } from '@/lib/types';
import { useEscapeKey } from '@/lib/ui-hooks';

const EXAMPLES = [
  'Quản lý đội xe vận tải, lịch bảo dưỡng và chi phí phụ tùng',
  'Quản lý hợp đồng kinh tế, thời hạn hiệu lực và giá trị hợp đồng',
  'Quản lý kho hàng hóa, tồn kho và phiếu nhập vật tư',
];

export default function AiAppModal({
  open,
  onClose,
  onGenerated,
}: {
  open: boolean;
  onClose: () => void;
  onGenerated: (app: SysApp) => void;
}) {
  const [prompt, setPrompt] = useState('');
  const [appCode, setAppCode] = useState('');
  const [department, setDepartment] = useState('');
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState('');
  const [aiStatus, setAiStatus] = useState<AiStatus | null>(null);
  useEscapeKey(open, onClose);

  useEffect(() => {
    if (!open) return;
    fetch('/api/settings/ai/status', { credentials: 'include' })
      .then((r) => r.json())
      .then((res) => {
        if (res.success && res.data?.status) setAiStatus(res.data.status);
      })
      .catch(() => {});
  }, [open]);

  if (!open) return null;

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!prompt.trim()) return;
    setLoading(true);
    setErr('');
    try {
      const res = await fetch('/api/apps/ai-generate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          prompt: prompt.trim(),
          appCode: appCode.trim() || null,
          department: department.trim() || null,
        }),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setPrompt('');
        setAppCode('');
        onGenerated(res.data);
      } else {
        setErr(res.message || 'Không thể sinh App từ mô tả.');
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 620 }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <AutoAwesomeRoundedIcon sx={{ fontSize: 22, color: '#1a73e8' }} />
            <h3 style={{ fontSize: 17, fontWeight: 700, margin: 0 }}>AI App Copilot — Sinh Ứng dụng Tự động</h3>
          </div>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', marginBottom: 10 }}>
          Mô tả quy trình công ty cần quản lý bằng tiếng Việt. Hệ thống sẽ tự động thiết kế Module, Bảng động,
          Trường dữ liệu (gồm <code>AUTO_NUMBER</code>, <code>SUB_TABLE</code>, Công thức) và Dashboard Widget.
        </p>

        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            flexWrap: 'wrap',
            gap: 8,
            padding: '8px 12px',
            borderRadius: 10,
            marginBottom: 12,
            fontSize: 12,
            backgroundColor: aiStatus?.isConfigured ? '#e6f4ea' : 'var(--bg-primary)',
            color: aiStatus?.isConfigured ? '#137333' : 'var(--text-secondary)',
            border: '1px solid var(--border-light)',
          }}
        >
          <span>
            {aiStatus?.isConfigured ? (
              <>
                <strong>Đang dùng API AI:</strong> {aiStatus.activeProviderName} (<code>{aiStatus.activeModel}</code>)
              </>
            ) : (
              <>Chưa cấu hình API AI riêng — đang dùng bộ mẫu thông minh (Rule Engine).</>
            )}
          </span>
          <Link
            href="/settings?tab=ai"
            onClick={onClose}
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 4,
              color: 'var(--accent-primary)',
              fontWeight: 600,
              textDecoration: 'none',
            }}
          >
            <SettingsRoundedIcon sx={{ fontSize: 14 }} />
            Cài đặt API AI
          </Link>
        </div>

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginBottom: 12 }}>
          {EXAMPLES.map((ex) => (
            <button
              key={ex}
              type="button"
              className="btn btn-outline btn-xs"
              onClick={() => setPrompt(ex)}
            >
              {ex}
            </button>
          ))}
        </div>

        {err && <div className="badge badge-danger" style={{ padding: '8px 12px', marginBottom: 10 }}>{err}</div>}

        <form onSubmit={submit}>
          <div className="form-group">
            <label className="form-label">Mô tả nghiệp vụ *</label>
            <textarea
              className="form-textarea"
              rows={3}
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              placeholder="VD: Tạo ứng dụng quản lý bảo trì xe công ty gồm danh sách xe, lịch bảo dưỡng và tổng hợp chi phí..."
              required
            />
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Mã App mong muốn (tùy chọn)</label>
              <input
                className="form-input"
                value={appCode}
                onChange={(e) => setAppCode(e.target.value.toUpperCase().replace(/[^A-Z0-9_]/g, ''))}
                placeholder="VD: FLEET, WMS, CONTRACT"
              />
            </div>
            <div className="form-group">
              <label className="form-label">Phòng ban phụ trách</label>
              <input
                className="form-input"
                value={department}
                onChange={(e) => setDepartment(e.target.value)}
                placeholder="VD: Khối Vận hành"
              />
            </div>
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 10 }}>
            <button type="button" className="btn btn-outline" onClick={onClose}>Hủy</button>
            <button type="submit" className="btn btn-primary" disabled={loading}>
              <AutoAwesomeRoundedIcon sx={{ fontSize: 16 }} />
              {loading ? 'AI đang thiết kế App...' : 'Tạo App Ngay'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
