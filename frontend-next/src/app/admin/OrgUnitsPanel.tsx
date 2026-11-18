'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import PeopleRoundedIcon from '@mui/icons-material/PeopleRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import { ConfirmDialog, EmptyState } from '@/app/components/ui';
import { SysOrgUnit, SysUser } from '@/lib/types';
import { useEscapeKey } from '@/lib/ui-hooks';
import { flattenOrgUnits } from '@/lib/org-units';

export default function OrgUnitsPanel({
  users,
  toast,
  onOrgUnitsChanged,
}: {
  users: SysUser[];
  toast: (message: string, severity?: 'success' | 'error' | 'info') => void;
  onOrgUnitsChanged?: () => Promise<void> | void;
}) {
  const [units, setUnits] = useState<SysOrgUnit[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedUnitId, setSelectedUnitId] = useState<string | null>(null);
  const [subtreeUsers, setSubtreeUsers] = useState<string[]>([]);
  const [loadingSubtree, setLoadingSubtree] = useState(false);

  const [showModal, setShowModal] = useState(false);
  const [editing, setEditing] = useState<SysOrgUnit | null>(null);
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [parentId, setParentId] = useState('');
  const [managerUsername, setManagerUsername] = useState('');
  const [orderIndex, setOrderIndex] = useState(1);
  const [saving, setSaving] = useState(false);
  const [formErr, setFormErr] = useState('');

  const [confirmDelete, setConfirmDelete] = useState<SysOrgUnit | null>(null);
  useEscapeKey(showModal, () => setShowModal(false));

  const loadUnits = useCallback(async () => {
    try {
      const res = await fetch('/api/org-units', { credentials: 'include' }).then((r) => r.json());
      if (res.success && Array.isArray(res.data)) {
        setUnits(res.data);
        setSelectedUnitId((prev) =>
          prev && res.data.some((u: SysOrgUnit) => u.id === prev) ? prev : res.data[0]?.id || null
        );
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let active = true;
    fetch('/api/org-units', { credentials: 'include' })
      .then((r) => r.json())
      .then((res) => {
        if (!active) return;
        if (res.success && Array.isArray(res.data)) {
          setUnits(res.data);
          setSelectedUnitId((prev) =>
            prev && res.data.some((u: SysOrgUnit) => u.id === prev) ? prev : res.data[0]?.id || null
          );
        }
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, []);

  const loadSubtree = useCallback(async (unitId: string | null) => {
    if (!unitId) {
      setSubtreeUsers([]);
      return;
    }
    setLoadingSubtree(true);
    try {
      const res = await fetch(`/api/org-units/${encodeURIComponent(unitId)}/subtree-users`, {
        credentials: 'include',
      }).then((r) => r.json());
      if (res.success && Array.isArray(res.data)) {
        setSubtreeUsers(res.data);
      } else {
        setSubtreeUsers([]);
      }
    } catch {
      setSubtreeUsers([]);
    } finally {
      setLoadingSubtree(false);
    }
  }, []);

  useEffect(() => {
    if (!selectedUnitId) return;
    let active = true;
    fetch(`/api/org-units/${encodeURIComponent(selectedUnitId)}/subtree-users`, {
      credentials: 'include',
    })
      .then((r) => r.json())
      .then((res) => {
        if (!active) return;
        setSubtreeUsers(res.success && Array.isArray(res.data) ? res.data : []);
      })
      .catch(() => {
        if (active) setSubtreeUsers([]);
      })
      .finally(() => {
        if (active) setLoadingSubtree(false);
      });
    return () => { active = false; };
  }, [selectedUnitId]);

  const orderedNodes = useMemo(() => flattenOrgUnits(units), [units]);
  const selectedUnit = useMemo(
    () => units.find((u) => u.id === selectedUnitId) || null,
    [units, selectedUnitId]
  );

  const directMemberCountByUnit = useMemo(() => {
    const map: Record<string, number> = {};
    for (const u of users) {
      if (u.orgUnitId) {
        map[u.orgUnitId] = (map[u.orgUnitId] || 0) + 1;
      } else {
        const matched = units.find(
          (ou) => ou.name.toLowerCase() === (u.department || '').trim().toLowerCase()
        );
        if (matched) map[matched.id] = (map[matched.id] || 0) + 1;
      }
    }
    return map;
  }, [users, units]);

  const openCreateModal = (defaultParentId = '') => {
    setEditing(null);
    setCode('');
    setName('');
    setParentId(defaultParentId);
    setManagerUsername('');
    setOrderIndex(units.length + 1);
    setFormErr('');
    setShowModal(true);
  };

  const openEditModal = (unit: SysOrgUnit) => {
    setEditing(unit);
    setCode(unit.code);
    setName(unit.name);
    setParentId(unit.parentId || '');
    setManagerUsername(unit.managerUsername || '');
    setOrderIndex(unit.orderIndex || 1);
    setFormErr('');
    setShowModal(true);
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!code.trim() || !name.trim()) {
      setFormErr('Mã đơn vị và Tên đơn vị là bắt buộc.');
      return;
    }
    setSaving(true);
    setFormErr('');
    try {
      const res = await fetch('/api/org-units', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          id: editing?.id || null,
          code: code.trim().toUpperCase(),
          name: name.trim(),
          parentId: parentId || null,
          managerUsername: managerUsername.trim() || null,
          orderIndex: Number(orderIndex) || 1,
          isActive: true,
        }),
      }).then((r) => r.json());
      if (res.success && res.data) {
        setShowModal(false);
        await loadUnits();
        setSelectedUnitId(res.data.id);
        await loadSubtree(res.data.id);
        await onOrgUnitsChanged?.();
        toast(editing ? 'Đã cập nhật đơn vị tổ chức!' : 'Đã tạo đơn vị tổ chức mới!');
      } else {
        setFormErr(res.message || 'Không thể lưu đơn vị.');
      }
    } catch {
      setFormErr('Lỗi kết nối máy chủ.');
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (unit: SysOrgUnit) => {
    const res = await fetch(`/api/org-units/${encodeURIComponent(unit.id)}`, {
      method: 'DELETE',
      credentials: 'include',
    }).then((r) => r.json());
    setConfirmDelete(null);
    if (res.success) {
      await loadUnits();
      await onOrgUnitsChanged?.();
      toast(`Đã xóa đơn vị "${unit.name}".`);
    } else {
      toast(res.message || 'Không thể xóa đơn vị.', 'error');
    }
  };

  const matchedUsersInSubtree = useMemo(() => {
    const set = new Set(subtreeUsers.map((u) => u.toLowerCase()));
    return users.filter((u) => set.has(u.username.toLowerCase()));
  }, [subtreeUsers, users]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div className="card" style={{ padding: 18, display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <AccountTreeRoundedIcon sx={{ fontSize: 20, color: '#1a73e8' }} />
            <strong style={{ fontSize: 15 }}>Sơ đồ Cơ cấu Tổ chức (Org Units)</strong>
          </div>
          <div style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 2 }}>
            Xây dựng cây Khối / Phòng ban / Nhóm; tự động kế thừa phân quyền dữ liệu theo nhánh (`DEPARTMENT` row scope).
          </div>
        </div>
        <button type="button" className="btn btn-primary btn-sm" onClick={() => openCreateModal('')}>
          <AddRoundedIcon sx={{ fontSize: 16 }} /> Thêm Đơn vị Gốc
        </button>
      </div>

      {loading ? (
        <div className="card" style={{ padding: 32, textAlign: 'center', color: 'var(--text-muted)' }}>
          Đang tải cây cơ cấu tổ chức...
        </div>
      ) : units.length === 0 ? (
        <EmptyState
          icon={<AccountTreeRoundedIcon sx={{ fontSize: 32 }} />}
          title="Chưa có đơn vị tổ chức nào"
          description="Tạo Khối / Phòng ban đầu tiên để phân cấp nhân sự và bật phân quyền dữ liệu theo nhánh phòng ban."
          action={
            <button type="button" className="btn btn-primary btn-sm" onClick={() => openCreateModal('')}>
              <AddRoundedIcon sx={{ fontSize: 16 }} /> Tạo Đơn vị đầu tiên
            </button>
          }
        />
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(320px, 1.4fr) minmax(280px, 1fr)', gap: 16, alignItems: 'start' }}>
          {/* Tree list */}
          <div className="card" style={{ padding: 14, display: 'flex', flexDirection: 'column', gap: 6 }}>
            <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-muted)', padding: '2px 6px 6px', textTransform: 'uppercase' }}>
              Cây Đơn vị ({units.length})
            </div>
            {orderedNodes.map(({ unit, depth }) => {
              const isSelected = unit.id === selectedUnitId;
              const directCount = directMemberCountByUnit[unit.id] || 0;
              return (
                <div
                  key={unit.id}
                  onClick={() => setSelectedUnitId(unit.id)}
                  style={{
                    marginLeft: depth * 22,
                    padding: '10px 12px',
                    borderRadius: 10,
                    border: `1.5px solid ${isSelected ? '#1a73e8' : 'var(--border-subtle)'}`,
                    backgroundColor: isSelected ? 'rgba(26, 115, 232, 0.08)' : 'var(--bg-primary)',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    gap: 10,
                    cursor: 'pointer',
                  }}
                >
                  <div style={{ minWidth: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                      <code style={{ fontSize: 11 }}>{unit.code}</code>
                      <strong style={{ fontSize: 13.5, color: 'var(--text-primary)' }}>{unit.name}</strong>
                      <span className="badge badge-info" style={{ fontSize: 10.5 }}>
                        {directCount} trực tiếp
                      </span>
                    </div>
                    <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 3 }}>
                      Trưởng đơn vị: {unit.managerUsername ? <code>@{unit.managerUsername}</code> : 'Chưa gán'}
                    </div>
                  </div>
                  <div style={{ display: 'flex', gap: 4, flexShrink: 0 }} onClick={(e) => e.stopPropagation()}>
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      title="Thêm đơn vị trực thuộc"
                      onClick={() => openCreateModal(unit.id)}
                    >
                      + Con
                    </button>
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      title="Sửa đơn vị"
                      onClick={() => openEditModal(unit)}
                    >
                      <EditRoundedIcon sx={{ fontSize: 13 }} />
                    </button>
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      style={{ color: '#d93025' }}
                      title="Xóa đơn vị"
                      onClick={() => setConfirmDelete(unit)}
                    >
                      <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} />
                    </button>
                  </div>
                </div>
              );
            })}
          </div>

          {/* Subtree detail */}
          <div className="card" style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 12 }}>
            {selectedUnit ? (
              <>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 10 }}>
                  <div>
                    <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>{selectedUnit.code}</div>
                    <h4 style={{ margin: '2px 0 4px', fontSize: 16, fontWeight: 700 }}>{selectedUnit.name}</h4>
                    <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                      Quản lý: {selectedUnit.managerUsername ? <strong>@{selectedUnit.managerUsername}</strong> : 'Chưa chỉ định'}
                    </div>
                  </div>
                  <button type="button" className="btn btn-outline btn-xs" onClick={() => openCreateModal(selectedUnit.id)}>
                    <AddRoundedIcon sx={{ fontSize: 14 }} /> Đơn vị con
                  </button>
                </div>

                <div style={{ borderTop: '1px solid var(--border-light)', paddingTop: 12 }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, fontWeight: 700, marginBottom: 8 }}>
                    <PeopleRoundedIcon sx={{ fontSize: 16, color: '#1a73e8' }} />
                    Nhân sự trong nhánh ({loadingSubtree ? '...' : subtreeUsers.length})
                  </div>
                  {loadingSubtree ? (
                    <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Đang tính toán nhánh nhân sự...</div>
                  ) : subtreeUsers.length === 0 ? (
                    <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                      Chưa có nhân sự nào được gắn vào đơn vị này hoặc các đơn vị con. Hãy vào tab <strong>Người dùng</strong> để gán Đơn vị cho tài khoản.
                    </div>
                  ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                      {subtreeUsers.map((uname) => {
                        const info = matchedUsersInSubtree.find((u) => u.username.toLowerCase() === uname.toLowerCase());
                        return (
                          <div
                            key={uname}
                            style={{
                              padding: '8px 10px',
                              borderRadius: 8,
                              border: '1px solid var(--border-light)',
                              backgroundColor: 'var(--bg-secondary)',
                              display: 'flex',
                              justifyContent: 'space-between',
                              alignItems: 'center',
                              fontSize: 12.5,
                            }}
                          >
                            <div>
                              <strong>{info?.fullName || uname}</strong>{' '}
                              <code style={{ fontSize: 11 }}>@{uname}</code>
                              {info?.department && (
                                <span style={{ marginLeft: 8, fontSize: 11.5, color: 'var(--text-muted)' }}>
                                  ({info.department})
                                </span>
                              )}
                            </div>
                            {info?.roleCode && <span className="badge badge-info">{info.roleCode}</span>}
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              </>
            ) : (
              <div style={{ fontSize: 13, color: 'var(--text-muted)' }}>Chọn một đơn vị bên trái để xem chi tiết nhân sự nhánh.</div>
            )}
          </div>
        </div>
      )}

      {showModal && (
        <div className="modal-overlay" onClick={() => setShowModal(false)}>
          <div className="modal-dialog" style={{ maxWidth: 520 }} onClick={(e) => e.stopPropagation()}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
              <h3 style={{ fontSize: 16.5, fontWeight: 700, margin: 0 }}>
                {editing ? `Chỉnh sửa Đơn vị: ${editing.name}` : 'Thêm Đơn vị Tổ chức Mới'}
              </h3>
              <button type="button" className="btn btn-outline btn-xs" onClick={() => setShowModal(false)}>
                <CloseRoundedIcon sx={{ fontSize: 16 }} />
              </button>
            </div>
            {formErr && (
              <div style={{ padding: '8px 12px', borderRadius: 8, backgroundColor: '#fce8e6', color: '#c5221f', fontSize: 12.5, fontWeight: 600, marginBottom: 12 }}>
                {formErr}
              </div>
            )}
            <form onSubmit={handleSave} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1.4fr', gap: 10 }}>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Mã đơn vị *</label>
                  <input
                    className="form-input"
                    value={code}
                    onChange={(e) => setCode(e.target.value.toUpperCase())}
                    placeholder="VD: KHOI_KD"
                    required
                  />
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Tên đơn vị *</label>
                  <input
                    className="form-input"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="VD: Khối Kinh doanh"
                    required
                  />
                </div>
              </div>
              <div className="form-group" style={{ marginBottom: 0 }}>
                <label className="form-label">Đơn vị cấp trên (Parent Unit)</label>
                <select className="form-select" value={parentId} onChange={(e) => setParentId(e.target.value)}>
                  <option value="">-- Không có (Đơn vị gốc) --</option>
                  {orderedNodes
                    .filter(({ unit }) => !editing || unit.id !== editing.id)
                    .map(({ unit, depth }) => (
                      <option key={unit.id} value={unit.id}>
                        {`${'— '.repeat(depth)}${unit.name} (${unit.code})`}
                      </option>
                    ))}
                </select>
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1.5fr 0.7fr', gap: 10 }}>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Trưởng đơn vị (Username)</label>
                  <select className="form-select" value={managerUsername} onChange={(e) => setManagerUsername(e.target.value)}>
                    <option value="">-- Chưa chỉ định --</option>
                    {users.filter((u) => u.isActive).map((u) => (
                      <option key={u.id} value={u.username}>
                        {u.fullName} (@{u.username})
                      </option>
                    ))}
                  </select>
                </div>
                <div className="form-group" style={{ marginBottom: 0 }}>
                  <label className="form-label">Thứ tự</label>
                  <input
                    type="number"
                    className="form-input"
                    min={1}
                    value={orderIndex}
                    onChange={(e) => setOrderIndex(Number(e.target.value) || 1)}
                  />
                </div>
              </div>
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 4 }}>
                <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowModal(false)}>
                  Hủy
                </button>
                <button type="submit" className="btn btn-primary btn-sm" disabled={saving}>
                  <SaveRoundedIcon sx={{ fontSize: 15 }} /> {saving ? 'Đang lưu...' : 'Lưu Đơn vị'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <ConfirmDialog
        open={Boolean(confirmDelete)}
        title={`Xóa đơn vị ${confirmDelete?.name || ''}`}
        message="Các đơn vị con (nếu có) sẽ được tách thành đơn vị gốc. Xác nhận xóa?"
        onConfirm={() => { if (confirmDelete) void handleDelete(confirmDelete); }}
        onCancel={() => setConfirmDelete(null)}
      />
    </div>
  );
}
