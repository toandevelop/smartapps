'use client';

import React, { useState, useEffect } from 'react';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import AssignmentTurnedInRoundedIcon from '@mui/icons-material/AssignmentTurnedInRounded';
import { ChatChannel, ChatUserSummary, ChecklistItem } from '@/lib/types';
import { usePlatform } from '@/lib/app-context';
import { useEscapeKey } from '@/lib/ui-hooks';

export interface AssignTaskPayload {
  title: string;
  description?: string;
  priority: string;
  assigneeUsername: string;
  dueDate?: string;
  checklist: ChecklistItem[];
  channelId?: string;
  linkedAppCode?: string;
  postToChat: boolean;
}

interface AssignTaskModalProps {
  open: boolean;
  users: ChatUserSummary[];
  channels?: ChatChannel[];
  defaultChannelId?: string | null;
  defaultAssignee?: string;
  initialTitle?: string;
  initialDescription?: string;
  submitting?: boolean;
  onClose: () => void;
  onSubmit: (payload: AssignTaskPayload) => Promise<void>;
}

export default function AssignTaskModal({
  open,
  users,
  channels = [],
  defaultChannelId = null,
  defaultAssignee = '',
  initialTitle = '',
  initialDescription = '',
  submitting = false,
  onClose,
  onSubmit,
}: AssignTaskModalProps) {
  const { apps } = usePlatform();
  const [title, setTitle] = useState(initialTitle);
  const [description, setDescription] = useState(initialDescription);
  const [assignee, setAssignee] = useState(defaultAssignee);
  const [priority, setPriority] = useState('MEDIUM');
  const [dueDate, setDueDate] = useState('');
  const [channelId, setChannelId] = useState(defaultChannelId || '');
  const [linkedAppCode, setLinkedAppCode] = useState('');
  const [checklist, setChecklist] = useState<ChecklistItem[]>([]);
  const [newStep, setNewStep] = useState('');
  useEscapeKey(open, onClose);

  useEffect(() => {
    if (!open) return;
    setTitle(initialTitle);
    setDescription(initialDescription);
    setAssignee(defaultAssignee || users[0]?.username || '');
    setPriority('MEDIUM');
    const d = new Date();
    d.setDate(d.getDate() + 3);
    setDueDate(d.toISOString().slice(0, 10));
    setChannelId(defaultChannelId || '');
    setLinkedAppCode('');
    setChecklist([]);
    setNewStep('');
  }, [open, initialTitle, initialDescription, defaultAssignee, defaultChannelId, users]);

  if (!open) return null;

  const addChecklistStep = () => {
    if (!newStep.trim()) return;
    setChecklist((prev) => [
      ...prev,
      { id: `chk_${Date.now()}_${prev.length}`, text: newStep.trim(), done: false },
    ]);
    setNewStep('');
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!title.trim() || !assignee) return;
    await onSubmit({
      title: title.trim(),
      description: description.trim() || undefined,
      priority,
      assigneeUsername: assignee,
      dueDate: dueDate || undefined,
      checklist,
      channelId: channelId || undefined,
      linkedAppCode: linkedAppCode || undefined,
      postToChat: true,
    });
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 560 }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 14 }}>
          <div
            style={{
              width: 36,
              height: 36,
              borderRadius: 10,
              backgroundColor: 'var(--accent-primary-container)',
              color: 'var(--accent-primary)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <AssignmentTurnedInRoundedIcon sx={{ fontSize: 20 }} />
          </div>
          <div>
            <h3 style={{ fontSize: 16.5, fontWeight: 700, margin: 0 }}>Giao việc trực tiếp trong Chat</h3>
            <p style={{ fontSize: 12, color: 'var(--text-muted)', margin: 0 }}>
              Tạo công việc mới, gửi thông báo tức thì và hiển thị thẻ tương tác trong cuộc trò chuyện
            </p>
          </div>
        </div>

        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div>
            <label className="form-label">Tiêu đề công việc *</label>
            <input
              className="form-input"
              placeholder="Ví dụ: Hoàn thiện báo cáo kiểm kê thiết bị Q4"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              required
              autoFocus
            />
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
            <div>
              <label className="form-label">Giao cho nhân sự *</label>
              <select
                className="form-select"
                value={assignee}
                onChange={(e) => setAssignee(e.target.value)}
                required
              >
                {users.map((u) => (
                  <option key={u.username} value={u.username}>
                    {u.fullName} (@{u.username})
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="form-label">Mức độ ưu tiên</label>
              <select className="form-select" value={priority} onChange={(e) => setPriority(e.target.value)}>
                <option value="LOW">Thấp</option>
                <option value="MEDIUM">Trung bình</option>
                <option value="HIGH">Cao</option>
                <option value="URGENT">Khẩn cấp</option>
              </select>
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
            <div>
              <label className="form-label">Hạn hoàn thành</label>
              <input
                type="date"
                className="form-input"
                value={dueDate}
                onChange={(e) => setDueDate(e.target.value)}
              />
            </div>
            <div>
              <label className="form-label">Liên kết Ứng dụng (tùy chọn)</label>
              <select
                className="form-select"
                value={linkedAppCode}
                onChange={(e) => setLinkedAppCode(e.target.value)}
              >
                <option value="">-- Không liên kết --</option>
                {apps.map((a) => (
                  <option key={a.id} value={a.code}>
                    {a.name} ({a.code})
                  </option>
                ))}
              </select>
            </div>
          </div>

          {channels.length > 0 && !defaultChannelId && (
            <div>
              <label className="form-label">Đăng thẻ công việc vào kênh Chat</label>
              <select className="form-select" value={channelId} onChange={(e) => setChannelId(e.target.value)}>
                <option value="">Tự động gửi vào Chat riêng (DM) với người nhận</option>
                {channels.map((ch) => (
                  <option key={ch.id} value={ch.id}>
                    {ch.channelType === 'DIRECT' ? `@ ${ch.name}` : `# ${ch.name}`}
                  </option>
                ))}
              </select>
            </div>
          )}

          <div>
            <label className="form-label">Mô tả / Yêu cầu chi tiết</label>
            <textarea
              className="form-textarea"
              rows={2}
              placeholder="Ghi chú thêm đầu việc, tài liệu liên quan..."
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </div>

          <div>
            <label className="form-label">Checklist các bước nhỏ ({checklist.length})</label>
            <div style={{ display: 'flex', gap: 6 }}>
              <input
                className="form-input"
                placeholder="Nhập bước thực hiện rồi nhấn +"
                value={newStep}
                onChange={(e) => setNewStep(e.target.value)}
                onKeyDown={(e) => {
                  if (e.nativeEvent.isComposing || e.keyCode === 229) return;
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    addChecklistStep();
                  }
                }}
              />
              <button type="button" className="btn btn-outline btn-sm" onClick={addChecklistStep}>
                <AddRoundedIcon sx={{ fontSize: 16 }} />
              </button>
            </div>
            {checklist.length > 0 && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 4, marginTop: 8 }}>
                {checklist.map((item) => (
                  <div
                    key={item.id}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      padding: '4px 10px',
                      borderRadius: 8,
                      backgroundColor: 'var(--bg-secondary)',
                      fontSize: 12.5,
                    }}
                  >
                    <span>• {item.text}</span>
                    <button
                      type="button"
                      onClick={() => setChecklist((prev) => prev.filter((x) => x.id !== item.id))}
                      style={{ border: 'none', background: 'transparent', color: '#d93025', cursor: 'pointer' }}
                    >
                      <DeleteOutlineRoundedIcon sx={{ fontSize: 15 }} />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 6 }}>
            <button type="button" className="btn btn-outline btn-sm" onClick={onClose}>
              Hủy
            </button>
            <button type="submit" disabled={submitting} className="btn btn-primary btn-sm">
              {submitting ? 'Đang giao việc...' : 'Giao việc & Gửi vào Chat'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
