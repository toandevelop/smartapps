'use client';

import React, { useState } from 'react';
import Link from 'next/link';
import AssignmentTurnedInRoundedIcon from '@mui/icons-material/AssignmentTurnedInRounded';
import CalendarTodayRoundedIcon from '@mui/icons-material/CalendarTodayRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import RadioButtonUncheckedRoundedIcon from '@mui/icons-material/RadioButtonUncheckedRounded';
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded';
import RateReviewRoundedIcon from '@mui/icons-material/RateReviewRounded';
import ReplayRoundedIcon from '@mui/icons-material/ReplayRounded';
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded';
import { AppTask, ChecklistItem } from '@/lib/types';

export const TASK_STATUS_META: Record<string, { label: string; bg: string; color: string }> = {
  TODO: { label: 'Cần làm', bg: '#f1f3f4', color: '#3c4043' },
  IN_PROGRESS: { label: 'Đang thực hiện', bg: '#e8f0fe', color: '#1a73e8' },
  REVIEW: { label: 'Chờ duyệt', bg: '#fef7e0', color: '#b06000' },
  DONE: { label: 'Hoàn thành', bg: '#e6f4ea', color: '#137333' },
  CANCELLED: { label: 'Đã hủy', bg: '#fce8e6', color: '#c5221f' },
};

export const TASK_PRIORITY_META: Record<string, { label: string; bg: string; color: string }> = {
  URGENT: { label: 'Khẩn cấp', bg: '#fce8e6', color: '#c5221f' },
  HIGH: { label: 'Cao', bg: '#fef7e0', color: '#b06000' },
  MEDIUM: { label: 'Trung bình', bg: '#e8f0fe', color: '#1a73e8' },
  LOW: { label: 'Thấp', bg: '#e6f4ea', color: '#137333' },
};

interface TaskCardInChatProps {
  task: AppTask;
  compact?: boolean;
  onTaskUpdated?: (updated: AppTask) => void;
}

export default function TaskCardInChat({ task, compact = false, onTaskUpdated }: TaskCardInChatProps) {
  const [updating, setUpdating] = useState(false);

  const st = TASK_STATUS_META[task.status] || TASK_STATUS_META.TODO;
  const pri = TASK_PRIORITY_META[task.priority] || TASK_PRIORITY_META.MEDIUM;
  const isOverdue =
    task.dueDate &&
    task.status !== 'DONE' &&
    task.status !== 'CANCELLED' &&
    new Date(task.dueDate + 'T23:59:59') < new Date();

  const patchTask = async (payload: Record<string, unknown>) => {
    setUpdating(true);
    try {
      const res = await fetch(`/api/tasks/${encodeURIComponent(task.id)}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(payload),
      }).then((r) => r.json());
      if (res.success && res.data && onTaskUpdated) {
        onTaskUpdated(res.data);
      }
    } finally {
      setUpdating(false);
    }
  };

  const toggleChecklistItem = async (item: ChecklistItem) => {
    const nextChecklist = task.checklist.map((c) => (c.id === item.id ? { ...c, done: !c.done } : c));
    await patchTask({ checklist: nextChecklist });
  };

  return (
    <div
      style={{
        marginTop: 6,
        borderRadius: 14,
        border: '1px solid var(--border-subtle)',
        borderLeft: `4px solid ${pri.color}`,
        backgroundColor: 'var(--bg-surface)',
        padding: compact ? '10px 12px' : '12px 14px',
        maxWidth: 480,
        boxShadow: 'var(--shadow-sm)',
      }}
    >
      {/* Header row */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, flexWrap: 'wrap' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
          <AssignmentTurnedInRoundedIcon sx={{ fontSize: 16, color: 'var(--accent-primary)' }} />
          <code style={{ fontSize: 11, fontWeight: 700, color: 'var(--accent-primary)' }}>{task.taskCode}</code>
          <span className="badge" style={{ backgroundColor: pri.bg, color: pri.color, fontSize: 10.5, padding: '2px 8px' }}>
            {pri.label}
          </span>
          {task.linkedAppCode && (
            <Link
              href={`/apps/${task.linkedAppCode}`}
              className="badge badge-info"
              style={{ fontSize: 10, padding: '2px 7px', textDecoration: 'none' }}
            >
              {task.linkedAppCode}
            </Link>
          )}
        </div>
        <span className="badge" style={{ backgroundColor: st.bg, color: st.color, fontSize: 11, padding: '2px 9px' }}>
          {st.label}
        </span>
      </div>

      {/* Title & description */}
      <div
        style={{
          fontSize: compact ? 13 : 14,
          fontWeight: 700,
          color: 'var(--text-primary)',
          marginTop: 6,
          textDecoration: task.status === 'DONE' ? 'line-through' : 'none',
          opacity: task.status === 'DONE' ? 0.78 : 1,
        }}
      >
        {task.title}
      </div>
      {!compact && task.description && (
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4, lineHeight: 1.45 }}>
          {task.description}
        </div>
      )}

      {/* Assignee & Due Date */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 10,
          marginTop: 8,
          fontSize: 11.5,
          color: 'var(--text-muted)',
          flexWrap: 'wrap',
        }}
      >
        <span>
          Người nhận: <strong style={{ color: 'var(--text-primary)' }}>{task.assigneeFullName || `@${task.assigneeUsername}`}</strong>
        </span>
        {task.dueDate && (
          <span
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 4,
              color: isOverdue ? '#d93025' : 'var(--text-muted)',
              fontWeight: isOverdue ? 700 : 500,
            }}
          >
            <CalendarTodayRoundedIcon sx={{ fontSize: 12 }} />
            Hạn: {new Date(task.dueDate).toLocaleDateString('vi-VN')}
            {isOverdue && ' (Quá hạn)'}
          </span>
        )}
      </div>

      {/* Progress bar */}
      <div style={{ marginTop: 8, display: 'flex', alignItems: 'center', gap: 8 }}>
        <div
          style={{
            flex: 1,
            height: 6,
            borderRadius: 999,
            backgroundColor: 'var(--bg-secondary)',
            overflow: 'hidden',
          }}
        >
          <div
            style={{
              width: `${task.progress}%`,
              height: '100%',
              backgroundColor: task.status === 'DONE' ? '#1e8e3e' : 'var(--accent-primary)',
              transition: 'width 0.2s ease',
            }}
          />
        </div>
        <span style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-secondary)' }}>{task.progress}%</span>
      </div>

      {/* Checklist items */}
      {!compact && task.checklist && task.checklist.length > 0 && (
        <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 4 }}>
          {task.checklist.map((item) => (
            <button
              key={item.id}
              type="button"
              disabled={updating}
              onClick={() => toggleChecklistItem(item)}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 6,
                border: 'none',
                background: 'transparent',
                padding: '2px 0',
                cursor: 'pointer',
                textAlign: 'left',
                fontSize: 12,
                color: item.done ? 'var(--text-muted)' : 'var(--text-primary)',
                textDecoration: item.done ? 'line-through' : 'none',
              }}
            >
              {item.done ? (
                <CheckCircleRoundedIcon sx={{ fontSize: 15, color: '#1e8e3e', flexShrink: 0 }} />
              ) : (
                <RadioButtonUncheckedRoundedIcon sx={{ fontSize: 15, color: 'var(--text-muted)', flexShrink: 0 }} />
              )}
              <span>{item.text}</span>
            </button>
          ))}
        </div>
      )}

      {/* Quick Inline Actions */}
      <div
        style={{
          marginTop: 10,
          paddingTop: 8,
          borderTop: '1px solid var(--border-light)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 6,
          flexWrap: 'wrap',
        }}
      >
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
          {task.status === 'TODO' && (
            <button
              type="button"
              disabled={updating}
              className="btn btn-outline btn-xs"
              style={{ color: '#1a73e8' }}
              onClick={() => patchTask({ status: 'IN_PROGRESS', progress: Math.max(25, task.progress) })}
            >
              <PlayArrowRoundedIcon sx={{ fontSize: 14 }} />
              Bắt đầu làm
            </button>
          )}
          {(task.status === 'TODO' || task.status === 'IN_PROGRESS') && (
            <button
              type="button"
              disabled={updating}
              className="btn btn-outline btn-xs"
              style={{ color: '#b06000' }}
              onClick={() => patchTask({ status: 'REVIEW', progress: Math.max(80, task.progress) })}
            >
              <RateReviewRoundedIcon sx={{ fontSize: 14 }} />
              Gửi duyệt
            </button>
          )}
          {task.status !== 'DONE' ? (
            <button
              type="button"
              disabled={updating}
              className="btn btn-xs"
              style={{ backgroundColor: '#1e8e3e', color: '#ffffff', border: 'none' }}
              onClick={() => patchTask({ status: 'DONE', progress: 100 })}
            >
              <CheckCircleRoundedIcon sx={{ fontSize: 14 }} />
              Hoàn thành
            </button>
          ) : (
            <button
              type="button"
              disabled={updating}
              className="btn btn-outline btn-xs"
              onClick={() => patchTask({ status: 'IN_PROGRESS', progress: 50 })}
            >
              <ReplayRoundedIcon sx={{ fontSize: 14 }} />
              Mở lại
            </button>
          )}
        </div>

        <Link
          href="/apps/TASKS"
          style={{
            fontSize: 11,
            fontWeight: 600,
            color: 'var(--text-muted)',
            textDecoration: 'none',
            display: 'inline-flex',
            alignItems: 'center',
            gap: 3,
          }}
        >
          Bảng việc <OpenInNewRoundedIcon sx={{ fontSize: 12 }} />
        </Link>
      </div>
    </div>
  );
}
