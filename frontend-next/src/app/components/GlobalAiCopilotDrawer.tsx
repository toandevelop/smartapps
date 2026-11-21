'use client';

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import Drawer from '@mui/material/Drawer';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import SendRoundedIcon from '@mui/icons-material/SendRounded';
import BuildCircleRoundedIcon from '@mui/icons-material/BuildCircleRounded';
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import AddTaskRoundedIcon from '@mui/icons-material/AddTaskRounded';
import DeleteSweepRoundedIcon from '@mui/icons-material/DeleteSweepRounded';
import type {
  AiCopilotAskResponse,
  AiCopilotCitation,
  AiCopilotProposedAction,
  AiCopilotToolTrace,
} from '@/lib/types';

interface CopilotMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  engine?: string;
  citations?: AiCopilotCitation[];
  toolTraces?: AiCopilotToolTrace[];
  proposedAction?: AiCopilotProposedAction | null;
  committedRecordUrl?: string | null;
}

const QUICK_PROMPTS = [
  'Hồ sơ nào đang chờ tôi duyệt?',
  'Công việc của tôi cần xử lý?',
  'Thống kê tổng quan các ứng dụng',
  'Đề xuất tạo hồ sơ mới',
];

export default function GlobalAiCopilotDrawer({
  open,
  onClose,
}: {
  open: boolean;
  onClose: () => void;
}) {
  const pathname = usePathname() || '/';
  const router = useRouter();
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [committingId, setCommittingId] = useState<string | null>(null);
  const [messages, setMessages] = useState<CopilotMessage[]>([
    {
      id: 'welcome',
      role: 'assistant',
      content:
        'Xin chào! Mình là **Trợ lý AI Doanh nghiệp SmartApps Copilot**. Mình có thể tra cứu hồ sơ tiếng Việt không dấu liên ứng dụng, kiểm tra hồ sơ chờ duyệt, công việc cá nhân hoặc soạn bản nháp tạo hồ sơ mới theo đúng phân quyền RBAC/FLS của bạn.',
      engine: 'SmartApps Enterprise Agent 3.0',
    },
  ]);

  const scrollRef = useRef<HTMLDivElement | null>(null);

  const currentAppCode = React.useMemo(() => {
    const m = pathname.match(/^\/apps\/([^/]+)/);
    return m ? decodeURIComponent(m[1]) : undefined;
  }, [pathname]);

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages, busy]);

  const askCopilot = useCallback(
    async (textRaw: string) => {
      const text = textRaw.trim();
      if (!text || busy) return;
      const userMsg: CopilotMessage = {
        id: `u_${Date.now()}`,
        role: 'user',
        content: text,
      };
      setMessages((prev) => [...prev, userMsg]);
      setInput('');
      setBusy(true);

      try {
        const res = await fetch('/api/ai/copilot/ask', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({
            message: text,
            appCode: currentAppCode,
          }),
        }).then((r) => r.json());

        if (res.success && res.data) {
          const data = res.data as AiCopilotAskResponse;
          setMessages((prev) => [
            ...prev,
            {
              id: `a_${Date.now()}`,
              role: 'assistant',
              content: data.answer,
              engine: data.engine,
              citations: data.citations || [],
              toolTraces: data.toolTraces || [],
              proposedAction: data.proposedAction || null,
            },
          ]);
        } else {
          setMessages((prev) => [
            ...prev,
            {
              id: `err_${Date.now()}`,
              role: 'assistant',
              content: res.message || 'Không thể kết nối Trợ lý AI Copilot lúc này.',
            },
          ]);
        }
      } catch {
        setMessages((prev) => [
          ...prev,
          {
            id: `err_${Date.now()}`,
            role: 'assistant',
            content: 'Lỗi kết nối mạng khi gọi Trợ lý AI Copilot.',
          },
        ]);
      } finally {
        setBusy(false);
      }
    },
    [busy, currentAppCode]
  );

  const handleConfirmProposal = async (msgId: string, proposal: AiCopilotProposedAction) => {
    setCommittingId(msgId);
    try {
      const res = await fetch('/api/records', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          appId: proposal.appId,
          moduleId: proposal.moduleId,
          tableId: proposal.tableId,
          data: proposal.draftData,
        }),
      }).then((r) => r.json());

      if (res.success && res.data) {
        const targetUrl = `/apps/${proposal.appCode}?tableId=${proposal.tableId}&recordId=${res.data.id}`;
        setMessages((prev) =>
          prev.map((m) => (m.id === msgId ? { ...m, committedRecordUrl: targetUrl } : m))
        );
      } else {
        alert(res.message || 'Không thể tạo bản ghi từ đề xuất (kiểm tra quy tắc biểu mẫu).');
      }
    } finally {
      setCommittingId(null);
    }
  };

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      PaperProps={{
        role: 'dialog',
        'aria-label': 'Trợ lý AI Doanh nghiệp SmartApps Copilot',
        sx: {
          width: { xs: '100vw', sm: 460 },
          backgroundColor: 'var(--bg-surface)',
          color: 'var(--text-primary)',
          display: 'flex',
          flexDirection: 'column',
        },
      }}
    >
      {/* Header */}
      <div
        style={{
          padding: '14px 18px',
          borderBottom: '1px solid var(--border-subtle)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 10,
          background: 'linear-gradient(135deg, var(--accent-bg), var(--bg-surface))',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
          <span
            style={{
              width: 34,
              height: 34,
              borderRadius: 10,
              backgroundColor: 'var(--accent-primary)',
              color: 'var(--accent-on-primary, #fff)',
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0,
            }}
          >
            <AutoAwesomeRoundedIcon sx={{ fontSize: 19 }} />
          </span>
          <div style={{ minWidth: 0 }}>
            <div style={{ fontWeight: 700, fontSize: 14.5, display: 'flex', alignItems: 'center', gap: 6 }}>
              SmartApps AI Copilot
              {currentAppCode && (
                <span className="badge badge-info" style={{ fontSize: 10.5 }}>
                  {currentAppCode}
                </span>
              )}
            </div>
            <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
              Multi-App MCP Agent • Phân quyền RBAC &amp; FLS
            </div>
          </div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
          <button
            type="button"
            className="btn btn-outline btn-xs"
            title="Xóa lịch sử hội thoại"
            onClick={() =>
              setMessages((prev) => prev.slice(0, 1))
            }
          >
            <DeleteSweepRoundedIcon sx={{ fontSize: 15 }} />
          </button>
          <button
            type="button"
            className="btn btn-outline btn-xs"
            aria-label="Đóng Trợ lý AI"
            onClick={onClose}
          >
            <CloseRoundedIcon sx={{ fontSize: 16 }} />
          </button>
        </div>
      </div>

      {/* Quick Prompts */}
      <div
        style={{
          padding: '10px 14px',
          borderBottom: '1px solid var(--border-subtle)',
          display: 'flex',
          flexWrap: 'wrap',
          gap: 6,
          backgroundColor: 'var(--bg-primary)',
        }}
      >
        {QUICK_PROMPTS.map((p) => (
          <button
            key={p}
            type="button"
            className="btn btn-outline btn-xs"
            disabled={busy}
            onClick={() => void askCopilot(p)}
            style={{ fontSize: 11.5 }}
          >
            {p}
          </button>
        ))}
      </div>

      {/* Message Stream */}
      <div
        ref={scrollRef}
        style={{
          flex: 1,
          overflowY: 'auto',
          padding: 16,
          display: 'flex',
          flexDirection: 'column',
          gap: 14,
        }}
      >
        {messages.map((m) => (
          <div
            key={m.id}
            style={{
              alignSelf: m.role === 'user' ? 'flex-end' : 'stretch',
              maxWidth: m.role === 'user' ? '85%' : '100%',
              display: 'flex',
              flexDirection: 'column',
              gap: 8,
            }}
          >
            <div
              style={{
                padding: '11px 14px',
                borderRadius: 12,
                fontSize: 13,
                lineHeight: 1.55,
                whiteSpace: 'pre-wrap',
                backgroundColor:
                  m.role === 'user' ? 'var(--accent-primary)' : 'var(--bg-primary)',
                color:
                  m.role === 'user' ? 'var(--accent-on-primary, #fff)' : 'var(--text-primary)',
                border:
                  m.role === 'user' ? 'none' : '1px solid var(--border-subtle)',
              }}
            >
              {m.content}
            </div>

            {/* Tool traces */}
            {m.toolTraces && m.toolTraces.length > 0 && (
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                {m.toolTraces.map((tr, idx) => (
                  <span
                    key={`${tr.tool}_${idx}`}
                    title={tr.summary}
                    style={{
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: 4,
                      fontSize: 11,
                      padding: '2px 8px',
                      borderRadius: 999,
                      backgroundColor: 'var(--accent-bg)',
                      color: 'var(--accent-primary)',
                      border: '1px solid var(--border-subtle)',
                    }}
                  >
                    <BuildCircleRoundedIcon sx={{ fontSize: 13 }} />
                    <strong>{tr.tool}</strong> ({tr.resultCount})
                  </span>
                ))}
              </div>
            )}

            {/* Citations */}
            {m.citations && m.citations.length > 0 && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                {m.citations.map((c) => (
                  <button
                    key={`${c.kind}_${c.id}`}
                    type="button"
                    onClick={() => {
                      router.push(c.url);
                      onClose();
                    }}
                    style={{
                      textAlign: 'left',
                      padding: '8px 12px',
                      borderRadius: 10,
                      border: '1px solid var(--border-subtle)',
                      backgroundColor: 'var(--bg-surface)',
                      color: 'var(--text-primary)',
                      cursor: 'pointer',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      gap: 8,
                    }}
                  >
                    <div style={{ minWidth: 0 }}>
                      <div style={{ fontWeight: 600, fontSize: 12.5, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {c.title}
                      </div>
                      <div style={{ fontSize: 11, color: 'var(--text-muted)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {c.subtitle}
                      </div>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexShrink: 0 }}>
                      {c.badge && (
                        <span className="badge badge-info" style={{ fontSize: 10 }}>
                          {c.badge}
                        </span>
                      )}
                      <OpenInNewRoundedIcon sx={{ fontSize: 14, color: 'var(--accent-primary)' }} />
                    </div>
                  </button>
                ))}
              </div>
            )}

            {/* Human-in-the-Loop Proposal Card */}
            {m.proposedAction && (
              <div
                style={{
                  padding: 12,
                  borderRadius: 10,
                  border: '1px solid var(--accent-primary)',
                  backgroundColor: 'var(--accent-bg)',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 8,
                }}
              >
                <div style={{ fontWeight: 700, fontSize: 12.5, display: 'flex', alignItems: 'center', gap: 6 }}>
                  <AddTaskRoundedIcon sx={{ fontSize: 16, color: 'var(--accent-primary)' }} />
                  {m.proposedAction.label} ({m.proposedAction.appCode})
                </div>
                <div
                  style={{
                    fontSize: 11.5,
                    fontFamily: 'var(--font-jetbrains), monospace',
                    backgroundColor: 'var(--bg-surface)',
                    padding: '6px 8px',
                    borderRadius: 6,
                    border: '1px solid var(--border-subtle)',
                    maxHeight: 120,
                    overflowY: 'auto',
                  }}
                >
                  {Object.entries(m.proposedAction.draftData).map(([k, v]) => (
                    <div key={k}>
                      <strong>{k}:</strong> {String(v ?? '')}
                    </div>
                  ))}
                </div>
                <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{m.proposedAction.note}</div>
                {m.committedRecordUrl ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-xs"
                    onClick={() => {
                      router.push(m.committedRecordUrl!);
                      onClose();
                    }}
                  >
                    <CheckCircleRoundedIcon sx={{ fontSize: 14 }} /> Đã tạo bản ghi • Mở hồ sơ ngay
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary btn-xs"
                    disabled={committingId === m.id}
                    onClick={() => void handleConfirmProposal(m.id, m.proposedAction!)}
                  >
                    <CheckCircleRoundedIcon sx={{ fontSize: 14 }} />
                    {committingId === m.id ? 'Đang lưu bản ghi...' : 'Xác nhận tạo bản ghi'}
                  </button>
                )}
              </div>
            )}
          </div>
        ))}

        {busy && (
          <div style={{ fontSize: 12, color: 'var(--text-muted)', fontStyle: 'italic' }}>
            SmartApps AI Copilot đang truy xuất công cụ &amp; phân tích...
          </div>
        )}
      </div>

      {/* Input form */}
      <form
        onSubmit={(e) => {
          e.preventDefault();
          void askCopilot(input);
        }}
        style={{
          padding: '12px 14px',
          borderTop: '1px solid var(--border-subtle)',
          display: 'flex',
          gap: 8,
          backgroundColor: 'var(--bg-surface)',
        }}
      >
        <input
          className="form-input"
          placeholder="Hỏi AI Copilot (vd: Tìm hồ sơ..., Hồ sơ chờ duyệt...)"
          aria-label="Nhập câu hỏi cho Trợ lý AI Copilot"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          disabled={busy}
          style={{ flex: 1, height: 38 }}
        />
        <button
          type="submit"
          className="btn btn-primary btn-sm"
          disabled={busy || !input.trim()}
          aria-label="Gửi câu hỏi tới AI Copilot"
        >
          <SendRoundedIcon sx={{ fontSize: 16 }} />
          Gửi
        </button>
      </form>
    </Drawer>
  );
}
