'use client';

import React, { useCallback, useEffect, useState } from 'react';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import CalendarMonthRoundedIcon from '@mui/icons-material/CalendarMonthRounded';
import SwapHorizRoundedIcon from '@mui/icons-material/SwapHorizRounded';
import type { BusinessHoliday, SysApp, SysUser, SysWorkflowDelegation } from '@/lib/types';

export default function WorkflowGovernancePanels({
  apps,
  users,
  toast,
}: {
  apps: SysApp[];
  users: SysUser[];
  toast: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [delegations, setDelegations] = useState<SysWorkflowDelegation[]>([]);
  const [holidays, setHolidays] = useState<BusinessHoliday[]>([]);

  const [delegatorUsername, setDelegatorUsername] = useState('');
  const [delegateUsername, setDelegateUsername] = useState('');
  const [delAppId, setDelAppId] = useState('');
  const [startsAt, setStartsAt] = useState(() => new Date().toISOString().slice(0, 16));
  const [expiresAt, setExpiresAt] = useState(() => {
    const d = new Date();
    d.setDate(d.getDate() + 7);
    return d.toISOString().slice(0, 16);
  });
  const [reason, setReason] = useState('');
  const [savingDel, setSavingDel] = useState(false);

  const [holDate, setHolDate] = useState('');
  const [holName, setHolName] = useState('');
  const [holRecurring, setHolRecurring] = useState(false);
  const [savingHol, setSavingHol] = useState(false);

  const loadData = useCallback(async () => {
    const [dRes, hRes] = await Promise.all([
      fetch('/api/workflows/delegations', { credentials: 'include' }).then((r) => r.json()).catch(() => null),
      fetch('/api/workflows/holidays', { credentials: 'include' }).then((r) => r.json()).catch(() => null),
    ]);
    if (dRes?.success) setDelegations(dRes.data || []);
    if (hRes?.success) setHolidays(hRes.data || []);
  }, []);

  useEffect(() => {
    let cancelled = false;
    Promise.resolve().then(() => {
      if (!cancelled) void loadData();
    });
    return () => {
      cancelled = true;
    };
  }, [loadData]);

  const activeUsers = users.filter((u) => u.isActive);

  const submitDelegation = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!delegateUsername) return;
    setSavingDel(true);
    try {
      const res = await fetch('/api/workflows/delegations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          delegatorUsername: delegatorUsername || null,
          delegateUsername,
          appId: delAppId || null,
          startsAt: startsAt ? new Date(startsAt).toISOString() : undefined,
          expiresAt: new Date(expiresAt).toISOString(),
          reason: reason.trim() || null,
          isActive: true,
        }),
      }).then((r) => r.json());
      if (res.success) {
        toast('Đã lưu ủy quyền phê duyệt!');
        setReason('');
        await loadData();
      } else {
        toast(res.message || 'Không thể lưu ủy quyền.', 'error');
      }
    } finally {
      setSavingDel(false);
    }
  };

  const submitHoliday = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!holDate || !holName.trim()) return;
    setSavingHol(true);
    try {
      const res = await fetch('/api/workflows/holidays', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          holidayDate: holDate,
          name: holName.trim(),
          isRecurring: holRecurring,
        }),
      }).then((r) => r.json());
      if (res.success) {
        toast('Đã cập nhật ngày nghỉ lễ SLA!');
        setHolDate('');
        setHolName('');
        setHolRecurring(false);
        await loadData();
      } else {
        toast(res.message || 'Không thể lưu ngày nghỉ lễ.', 'error');
      }
    } finally {
      setSavingHol(false);
    }
  };

  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(380px, 1fr))', gap: 16 }}>
      {/* Delegation Management */}
      <div className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 12 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <SwapHorizRoundedIcon sx={{ color: '#1a73e8' }} />
          <div>
            <strong style={{ fontSize: 14.5 }}>Ủy quyền Phê duyệt có Thời hạn</strong>
            <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              Cho phép người khác phê duyệt thay khi trưởng bộ phận đi công tác / nghỉ phép
            </div>
          </div>
        </div>

        <form onSubmit={submitDelegation} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
          <select className="form-select" value={delegatorUsername} onChange={(e) => setDelegatorUsername(e.target.value)}>
            <option value="">Người ủy quyền (mặc định: tôi)</option>
            {activeUsers.map((u) => (
              <option key={u.id} value={u.username}>{u.fullName} (@{u.username})</option>
            ))}
          </select>
          <select className="form-select" required value={delegateUsername} onChange={(e) => setDelegateUsername(e.target.value)}>
            <option value="">Người nhận ủy quyền *</option>
            {activeUsers.map((u) => (
              <option key={u.id} value={u.username}>{u.fullName} (@{u.username})</option>
            ))}
          </select>
          <select className="form-select" value={delAppId} onChange={(e) => setDelAppId(e.target.value)}>
            <option value="">Tất cả ứng dụng</option>
            {apps.map((a) => (
              <option key={a.id} value={a.id}>{a.name} ({a.code})</option>
            ))}
          </select>
          <input className="form-input" placeholder="Lý do (VD: Nghỉ phép năm)" value={reason} onChange={(e) => setReason(e.target.value)} />
          <input type="datetime-local" className="form-input" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} required />
          <input type="datetime-local" className="form-input" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} required />
          <div style={{ gridColumn: '1 / -1', display: 'flex', justifyContent: 'flex-end' }}>
            <button type="submit" className="btn btn-primary btn-xs" disabled={savingDel}>
              <AddRoundedIcon sx={{ fontSize: 14 }} /> {savingDel ? 'Đang lưu...' : 'Thêm ủy quyền'}
            </button>
          </div>
        </form>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 6, maxHeight: 240, overflowY: 'auto' }}>
          {delegations.length === 0 ? (
            <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>Chưa có cấu hình ủy quyền nào.</div>
          ) : (
            delegations.map((d) => (
              <div
                key={d.id}
                style={{
                  padding: '8px 10px',
                  borderRadius: 8,
                  backgroundColor: 'var(--bg-secondary)',
                  border: '1px solid var(--border-subtle)',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  gap: 8,
                  fontSize: 12,
                }}
              >
                <div>
                  <strong>@{d.delegatorUsername}</strong> → <strong>@{d.delegateUsername}</strong>
                  {d.appId && <span className="badge badge-info" style={{ marginLeft: 6 }}>{apps.find((a) => a.id === d.appId)?.code || d.appId}</span>}
                  <div style={{ color: 'var(--text-muted)', fontSize: 11, marginTop: 2 }}>
                    {new Date(d.startsAt).toLocaleDateString('vi-VN')} – {new Date(d.expiresAt).toLocaleDateString('vi-VN')}
                    {d.reason ? ` • ${d.reason}` : ''}
                  </div>
                </div>
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  style={{ color: '#d93025' }}
                  onClick={async () => {
                    await fetch(`/api/workflows/delegations/${d.id}`, { method: 'DELETE', credentials: 'include' });
                    toast('Đã xóa ủy quyền');
                    await loadData();
                  }}
                >
                  <DeleteOutlineRoundedIcon sx={{ fontSize: 14 }} />
                </button>
              </div>
            ))
          )}
        </div>
      </div>

      {/* Vietnam Business Holidays for SLA */}
      <div className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 12 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <CalendarMonthRoundedIcon sx={{ color: '#00897b' }} />
          <div>
            <strong style={{ fontSize: 14.5 }}>Lịch Làm việc &amp; Ngày Nghỉ lễ VN (Tính SLA)</strong>
            <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              Bộ đếm SLA tự động loại trừ Thứ 7, Chủ Nhật và các ngày nghỉ lễ bên dưới (múi giờ UTC+7)
            </div>
          </div>
        </div>

        <form onSubmit={submitHoliday} style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
          <input type="date" className="form-input" style={{ width: 145 }} value={holDate} onChange={(e) => setHolDate(e.target.value)} required />
          <input className="form-input" style={{ flex: 1, minWidth: 140 }} placeholder="Tên ngày lễ (VD: Giỗ Tổ Hùng Vương)" value={holName} onChange={(e) => setHolName(e.target.value)} required />
          <label style={{ display: 'inline-flex', alignItems: 'center', gap: 4, fontSize: 12 }}>
            <input type="checkbox" checked={holRecurring} onChange={(e) => setHolRecurring(e.target.checked)} />
            Hằng năm
          </label>
          <button type="submit" className="btn btn-primary btn-xs" disabled={savingHol}>
            <AddRoundedIcon sx={{ fontSize: 14 }} /> Thêm ngày lễ
          </button>
        </form>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 6, maxHeight: 240, overflowY: 'auto' }}>
          {holidays.map((h) => (
            <div
              key={h.id}
              style={{
                padding: '6px 10px',
                borderRadius: 8,
                backgroundColor: 'var(--bg-secondary)',
                border: '1px solid var(--border-subtle)',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                fontSize: 12.5,
              }}
            >
              <div>
                <strong>{h.holidayDate}</strong> — {h.name}
                {h.isRecurring && <span className="badge badge-info" style={{ marginLeft: 6, fontSize: 10 }}>Hằng năm</span>}
              </div>
              <button
                type="button"
                className="btn btn-outline btn-xs"
                style={{ color: '#d93025' }}
                onClick={async () => {
                  await fetch(`/api/workflows/holidays/${h.id}`, { method: 'DELETE', credentials: 'include' });
                  toast('Đã xóa ngày lễ');
                  await loadData();
                }}
              >
                <DeleteOutlineRoundedIcon sx={{ fontSize: 14 }} />
              </button>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
