'use client';

import React, { useState, useEffect, useLayoutEffect, useCallback, useRef, Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import ForumRoundedIcon from '@mui/icons-material/ForumRounded';
import TagRoundedIcon from '@mui/icons-material/TagRounded';
import PersonRoundedIcon from '@mui/icons-material/PersonRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import SendRoundedIcon from '@mui/icons-material/SendRounded';
import AttachFileRoundedIcon from '@mui/icons-material/AttachFileRounded';
import AssignmentTurnedInRoundedIcon from '@mui/icons-material/AssignmentTurnedInRounded';
import PushPinRoundedIcon from '@mui/icons-material/PushPinRounded';
import ReplyRoundedIcon from '@mui/icons-material/ReplyRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import AddReactionRoundedIcon from '@mui/icons-material/AddReactionRounded';
import GroupAddRoundedIcon from '@mui/icons-material/GroupAddRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import InsertDriveFileRoundedIcon from '@mui/icons-material/InsertDriveFileRounded';
import ViewSidebarRoundedIcon from '@mui/icons-material/ViewSidebarRounded';
import { NotificationSnackbar } from '@/app/components/ui';
import TaskCardInChat from '@/app/components/chat/TaskCardInChat';
import AssignTaskModal, { AssignTaskPayload } from '@/app/components/chat/AssignTaskModal';
import { useAuth } from '@/lib/auth-context';
import { usePlatform } from '@/lib/app-context';
import { useSettings } from '@/lib/settings-context';
import { mergeChatHistory } from '@/lib/chat-history';
import {
  AppTask,
  ChatAttachment,
  ChatChannel,
  ChatMessage,
  ChatUserSummary,
} from '@/lib/types';

const QUICK_EMOJIS = ['👍', '❤️', '✅', '🔥', '👏', '👀'];

function renderContentWithMentions(text: string) {
  const parts = text.split(/(@[a-zA-Z0-9_\.]+)/g);
  return parts.map((part, idx) =>
    part.startsWith('@') && part.length > 1 ? (
      <span
        key={idx}
        style={{
          color: 'var(--accent-primary)',
          fontWeight: 700,
          backgroundColor: 'var(--accent-primary-container)',
          padding: '0 4px',
          borderRadius: 4,
        }}
      >
        {part}
      </span>
    ) : (
      <React.Fragment key={idx}>{part}</React.Fragment>
    )
  );
}

function ChatWorkspaceInner() {
  const searchParams = useSearchParams();
  const requestedChannelId = searchParams.get('channel');
  const { user } = useAuth();
  const { apps } = usePlatform();
  const { userSettings, systemSettings, refreshUnreadSummary } = useSettings();

  const [channels, setChannels] = useState<ChatChannel[]>([]);
  const [users, setUsers] = useState<ChatUserSummary[]>([]);
  const [activeChannelId, setActiveChannelId] = useState<string>('');
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [channelTasks, setChannelTasks] = useState<AppTask[]>([]);
  const [loadingChannels, setLoadingChannels] = useState(true);
  const [loadingMsgs, setLoadingMsgs] = useState(false);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [nextCursor, setNextCursor] = useState<string | null>(null);

  const [sidebarQuery, setSidebarQuery] = useState('');
  const [showPinnedOnly, setShowPinnedOnly] = useState(false);
  const [showRightPanel, setShowRightPanel] = useState(true);
  const [rightTab, setRightTab] = useState<'tasks' | 'members'>('tasks');
  const [taskFilter, setTaskFilter] = useState<'ALL' | 'OPEN' | 'DONE'>('ALL');

  // Composer state
  const [draft, setDraft] = useState('');
  const [sending, setSending] = useState(false);
  const [replyTo, setReplyTo] = useState<ChatMessage | null>(null);
  const [pendingAttachments, setPendingAttachments] = useState<ChatAttachment[]>([]);
  const [uploadingFile, setUploadingFile] = useState(false);
  const [emojiPickerForMsg, setEmojiPickerForMsg] = useState<string | null>(null);
  const [mentionQuery, setMentionQuery] = useState<string | null>(null);
  const [addMemberUser, setAddMemberUser] = useState('');

  // Modals
  const [showCreateChannel, setShowCreateChannel] = useState(false);
  const [newChName, setNewChName] = useState('');
  const [newChDesc, setNewChDesc] = useState('');
  const [newChType, setNewChType] = useState<'GROUP' | 'PUBLIC'>('GROUP');
  const [newChApp, setNewChApp] = useState('');
  const [newChMembers, setNewChMembers] = useState<string[]>([]);

  const [taskModalOpen, setTaskModalOpen] = useState(false);
  const [taskInitialTitle, setTaskInitialTitle] = useState('');
  const [taskInitialDesc, setTaskInitialDesc] = useState('');
  const [taskDefaultAssignee, setTaskDefaultAssignee] = useState('');
  const [convertingMsgId, setConvertingMsgId] = useState<string | null>(null);
  const [submittingTask, setSubmittingTask] = useState(false);

  const [snack, setSnack] = useState<{ open: boolean; message: string; severity: 'success' | 'error' | 'info' }>({
    open: false,
    message: '',
    severity: 'info',
  });

  const messagesEndRef = useRef<HTMLDivElement>(null);
  const messagesScrollRef = useRef<HTMLDivElement>(null);
  const activeConversationRef = useRef('');
  activeConversationRef.current = `${user?.id || ''}:${activeChannelId}`;
  const latestRequestRef = useRef(0);
  const olderRequestRef = useRef<object | null>(null);
  const loadedOlderRef = useRef(false);
  const scrollUpdateRef = useRef<{ mode: 'bottom' | 'preserve'; height: number; top: number } | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const loadChannelsAndUsers = useCallback(async () => {
    try {
      const [chRes, uRes] = await Promise.all([
        fetch('/api/chat/channels', { credentials: 'include' }),
        fetch('/api/chat/users', { credentials: 'include' }),
      ]);
      if (chRes.ok) {
        const j = await chRes.json();
        if (j.success && Array.isArray(j.data)) {
          setChannels(j.data);
          setActiveChannelId((prev) => {
            if (requestedChannelId && j.data.some((c: ChatChannel) => c.id === requestedChannelId)) {
              return requestedChannelId;
            }
            if (prev && j.data.some((c: ChatChannel) => c.id === prev)) return prev;
            return j.data[0]?.id || '';
          });
        }
      }
      if (uRes.ok) {
        const j = await uRes.json();
        if (j.success && Array.isArray(j.data)) setUsers(j.data);
      }
    } finally {
      setLoadingChannels(false);
    }
  }, [requestedChannelId]);

  const loadChannelContent = useCallback(
    async (chId: string, silent = false) => {
      if (!chId) return;
      const conversation = activeConversationRef.current;
      const requestId = ++latestRequestRef.current;
      if (!silent) setLoadingMsgs(true);
      try {
        const [msgRes, tskRes] = await Promise.all([
          fetch(`/api/chat/channels/${encodeURIComponent(chId)}/messages?limit=100`, { credentials: 'include' }),
          fetch(`/api/tasks?channelId=${encodeURIComponent(chId)}`, { credentials: 'include' }),
        ]);
        const j = await msgRes.json();
        if (activeConversationRef.current !== conversation || latestRequestRef.current !== requestId) return;
        if (!msgRes.ok || !j.success || !Array.isArray(j.data))
          throw new Error(j.message || 'Không tải được hội thoại.');
        setLoadingMsgs(false);
        const scroller = messagesScrollRef.current;
        if (!silent || (scroller && scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight < 100))
          scrollUpdateRef.current = { mode: 'bottom', height: 0, top: 0 };
        setMessages((previous) => silent ? mergeChatHistory(previous, j.data, true) : j.data);
        if (!silent || !loadedOlderRef.current) setNextCursor(j.pagination?.nextCursor || null);
        if (tskRes.ok) {
          const tasks = await tskRes.json();
          if (activeConversationRef.current === conversation && tasks.success && Array.isArray(tasks.data)) setChannelTasks(tasks.data);
        }
        if (activeConversationRef.current !== conversation || latestRequestRef.current !== requestId) return;
        setChannels((prev) => prev.map((c) => (c.id === chId ? { ...c, unreadCount: 0 } : c)));
        refreshUnreadSummary();
      } catch (error) {
        if (activeConversationRef.current === conversation && latestRequestRef.current === requestId)
          setSnack({ open: true, message: error instanceof Error ? error.message : 'Lỗi kết nối hội thoại.', severity: 'error' });
      } finally {
        if (activeConversationRef.current === conversation && latestRequestRef.current === requestId) setLoadingMsgs(false);
      }
    },
    [refreshUnreadSummary]
  );

  const loadOlderMessages = async () => {
    if (!activeChannelId || !nextCursor || olderRequestRef.current) return;
    const conversation = activeConversationRef.current;
    const request = {};
    olderRequestRef.current = request;
    setLoadingOlder(true);
    try {
      const qs = new URLSearchParams({ limit: '100', before: nextCursor });
      const response = await fetch(`/api/chat/channels/${encodeURIComponent(activeChannelId)}/messages?${qs}`, { credentials: 'include' });
      const result = await response.json();
      if (activeConversationRef.current !== conversation || olderRequestRef.current !== request) return;
      if (!response.ok || !result.success || !Array.isArray(result.data))
        throw new Error(result.message || 'Không tải được tin nhắn cũ.');
      const scroller = messagesScrollRef.current;
      if (scroller) scrollUpdateRef.current = { mode: 'preserve', height: scroller.scrollHeight, top: scroller.scrollTop };
      loadedOlderRef.current = true;
      setMessages((previous) => mergeChatHistory(previous, result.data));
      setNextCursor(result.pagination?.nextCursor || null);
    } catch (error) {
      if (activeConversationRef.current === conversation && olderRequestRef.current === request)
        setSnack({ open: true, message: error instanceof Error ? error.message : 'Lỗi kết nối khi tải lịch sử.', severity: 'error' });
    } finally {
      if (olderRequestRef.current === request) {
        olderRequestRef.current = null;
        setLoadingOlder(false);
      }
    }
  };

  useEffect(() => {
    if (user) loadChannelsAndUsers();
  }, [user, loadChannelsAndUsers]);

  useEffect(() => {
    if (requestedChannelId && channels.some((c) => c.id === requestedChannelId)) {
      setActiveChannelId(requestedChannelId);
    }
  }, [requestedChannelId, channels]);

  useEffect(() => {
    setMessages([]);
    setChannelTasks([]);
    setNextCursor(null);
    loadedOlderRef.current = false;
    olderRequestRef.current = null;
    setLoadingOlder(false);
    scrollUpdateRef.current = null;
    if (activeChannelId) {
      setShowPinnedOnly(false);
      setReplyTo(null);
      loadChannelContent(activeChannelId);
    }
  }, [activeChannelId, user?.id, loadChannelContent]);

  useLayoutEffect(() => {
    const scroller = messagesScrollRef.current;
    const update = scrollUpdateRef.current;
    if (!scroller || !update) return;
    scroller.scrollTop = update.mode === 'preserve'
      ? update.top + scroller.scrollHeight - update.height : scroller.scrollHeight;
    scrollUpdateRef.current = null;
  }, [messages]);

  // Real-time SSE listener
  useEffect(() => {
    const onRealtime = (e: Event) => {
      const detail = (e as CustomEvent<any>).detail;
      if (!detail) return;
      if (detail.type === 'chat_message' || detail.type === 'chat_reaction' || detail.type === 'chat_channel_created') {
        if (detail.data?.deleted && detail.data?.messageId)
          setMessages((previous) => previous.filter((message) => message.id !== detail.data.messageId));
        loadChannelsAndUsers();
        if (activeChannelId && (!detail.data?.channelId || detail.data.channelId === activeChannelId)) {
          loadChannelContent(activeChannelId, true);
        }
      } else if (detail.type === 'task_updated') {
        if (activeChannelId) loadChannelContent(activeChannelId, true);
      }
    };
    window.addEventListener('smartapps_realtime', onRealtime);
    return () => window.removeEventListener('smartapps_realtime', onRealtime);
  }, [activeChannelId, loadChannelsAndUsers, loadChannelContent]);

  const activeChannel = channels.find((c) => c.id === activeChannelId) || null;

  const handleDraftChange = (val: string) => {
    setDraft(val);
    const match = val.match(/@([a-zA-Z0-9_\.]*)$/);
    setMentionQuery(match ? match[1] : null);
  };

  const insertMention = (username: string) => {
    const next = draft.replace(/@([a-zA-Z0-9_\.]*)$/, `@${username} `);
    setDraft(next);
    setMentionQuery(null);
  };

  const handleUploadFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploadingFile(true);
    try {
      const fd = new FormData();
      fd.append('file', file);
      const res = await fetch('/api/attachments', {
        method: 'POST',
        credentials: 'include',
        body: fd,
      }).then((r) => r.json());
      if (res.success && res.data) {
        setPendingAttachments((prev) => [...prev, res.data]);
      } else {
        setSnack({ open: true, message: res.message || 'Lỗi tải tệp lên', severity: 'error' });
      }
    } finally {
      setUploadingFile(false);
      e.target.value = '';
    }
  };

  const handleSendMessage = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!activeChannelId || sending) return;
    if (!draft.trim() && pendingAttachments.length === 0) return;

    setSending(true);
    try {
      const res = await fetch(`/api/chat/channels/${encodeURIComponent(activeChannelId)}/messages`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          content: draft.trim(),
          messageType: pendingAttachments.length > 0 && !draft.trim() ? 'FILE' : 'TEXT',
          replyToId: replyTo?.id || null,
          attachments: pendingAttachments,
        }),
      }).then((r) => r.json());

      if (res.success) {
        setDraft('');
        setReplyTo(null);
        setPendingAttachments([]);
        setMentionQuery(null);
        if (res.createdTask) {
          setSnack({
            open: true,
            message: `Đã giao việc ${res.createdTask.taskCode} cho @${res.createdTask.assigneeUsername} ngay trong chat!`,
            severity: 'success',
          });
        }
        await loadChannelContent(activeChannelId, true);
        await loadChannelsAndUsers();
      } else {
        setSnack({ open: true, message: res.message || 'Không gửi được tin nhắn', severity: 'error' });
      }
    } finally {
      setSending(false);
    }
  };

  const handleStartDirectChat = async (targetUsername: string) => {
    const res = await fetch('/api/chat/channels/direct', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ targetUsername }),
    }).then((r) => r.json());
    if (res.success && res.data) {
      await loadChannelsAndUsers();
      setActiveChannelId(res.data.id);
    }
  };

  const handleCreateChannel = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newChName.trim()) return;
    const res = await fetch('/api/chat/channels', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({
        name: newChName.trim(),
        description: newChDesc.trim() || null,
        channelType: newChType,
        linkedAppCode: newChApp || null,
        memberUsernames: newChMembers,
      }),
    }).then((r) => r.json());
    if (res.success && res.data) {
      setShowCreateChannel(false);
      setNewChName('');
      setNewChDesc('');
      setNewChApp('');
      setNewChMembers([]);
      await loadChannelsAndUsers();
      setActiveChannelId(res.data.id);
      setSnack({ open: true, message: `Đã tạo nhóm chat "${res.data.name}"`, severity: 'success' });
    }
  };

  const openAssignTaskInChannel = () => {
    setConvertingMsgId(null);
    setTaskInitialTitle('');
    setTaskInitialDesc('');
    const peer = activeChannel?.members.find((m) => m.username !== user?.username);
    setTaskDefaultAssignee(peer?.username || users[0]?.username || '');
    setTaskModalOpen(true);
  };

  const openConvertMessageToTask = (msg: ChatMessage) => {
    setConvertingMsgId(msg.id);
    setTaskInitialTitle(msg.content.length > 100 ? msg.content.slice(0, 100) : msg.content);
    setTaskInitialDesc(`Chuyển từ tin nhắn của @${msg.senderUsername}: "${msg.content}"`);
    setTaskDefaultAssignee(msg.mentions?.[0] || msg.senderUsername);
    setTaskModalOpen(true);
  };

  const handleSubmitTask = async (payload: AssignTaskPayload) => {
    if (!activeChannelId) return;
    setSubmittingTask(true);
    try {
      const url = convertingMsgId
        ? `/api/chat/messages/${encodeURIComponent(convertingMsgId)}/convert-to-task`
        : `/api/chat/channels/${encodeURIComponent(activeChannelId)}/tasks`;
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(payload),
      }).then((r) => r.json());

      if (res.success) {
        setTaskModalOpen(false);
        setConvertingMsgId(null);
        await loadChannelContent(activeChannelId, true);
        await loadChannelsAndUsers();
        setSnack({
          open: true,
          message: `Đã giao việc ${res.data?.task?.taskCode || ''} và đăng thẻ công việc vào cuộc trò chuyện!`,
          severity: 'success',
        });
      } else {
        setSnack({ open: true, message: res.message || 'Lỗi tạo công việc', severity: 'error' });
      }
    } finally {
      setSubmittingTask(false);
    }
  };

  const handleToggleReaction = async (msgId: string, emoji: string) => {
    setEmojiPickerForMsg(null);
    const res = await fetch(`/api/chat/messages/${encodeURIComponent(msgId)}/reactions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ emoji }),
    }).then((r) => r.json());
    if (res.success && res.data) {
      setMessages((prev) => prev.map((m) => (m.id === msgId ? { ...m, reactions: res.data } : m)));
    }
  };

  const handleTogglePin = async (msgId: string) => {
    const res = await fetch(`/api/chat/messages/${encodeURIComponent(msgId)}/pin`, {
      method: 'PATCH',
      credentials: 'include',
    }).then((r) => r.json());
    if (res.success && res.data) {
      setMessages((prev) => prev.map((m) => (m.id === msgId ? { ...m, isPinned: res.data.isPinned } : m)));
    }
  };

  const handleDeleteMessage = async (msgId: string) => {
    const res = await fetch(`/api/chat/messages/${encodeURIComponent(msgId)}`, {
      method: 'DELETE',
      credentials: 'include',
    }).then((r) => r.json());
    if (res.success) {
      setMessages((prev) => prev.filter((m) => m.id !== msgId));
    }
  };

  const handleTaskUpdated = (updated: AppTask) => {
    setChannelTasks((prev) => prev.map((t) => (t.id === updated.id ? updated : t)));
    setMessages((prev) => prev.map((m) => (m.taskId === updated.id ? { ...m, task: updated } : m)));
    refreshUnreadSummary();
  };

  const filteredChannels = channels.filter(
    (c) => !sidebarQuery.trim() || c.name.toLowerCase().includes(sidebarQuery.toLowerCase())
  );
  const groupChannels = filteredChannels.filter((c) => c.channelType !== 'DIRECT');
  const dmChannels = filteredChannels.filter((c) => c.channelType === 'DIRECT');
  const otherColleagues = users.filter(
    (u) =>
      u.username !== user?.username &&
      (!sidebarQuery.trim() ||
        u.fullName.toLowerCase().includes(sidebarQuery.toLowerCase()) ||
        u.username.toLowerCase().includes(sidebarQuery.toLowerCase()))
  );

  const displayedMessages = showPinnedOnly ? messages.filter((m) => m.isPinned) : messages;
  const displayedChannelTasks = channelTasks.filter((t) => {
    if (taskFilter === 'OPEN') return t.status !== 'DONE' && t.status !== 'CANCELLED';
    if (taskFilter === 'DONE') return t.status === 'DONE';
    return true;
  });

  const mentionCandidates =
    mentionQuery !== null
      ? users.filter(
          (u) =>
            u.username.toLowerCase().includes(mentionQuery.toLowerCase()) ||
            u.fullName.toLowerCase().includes(mentionQuery.toLowerCase())
        )
      : [];

  const nonMemberUsers = activeChannel
    ? users.filter((u) => !activeChannel.members.some((m) => m.username === u.username))
    : [];

  return (
    <div style={{ display: 'flex', height: 'calc(100vh - 64px)', overflow: 'hidden', backgroundColor: 'var(--bg-primary)' }}>
      {/* Left Pane: Channels & Direct Messages */}
      <div
        style={{
          width: 290,
          flexShrink: 0,
          backgroundColor: 'var(--bg-surface)',
          borderRight: '1px solid var(--border-subtle)',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
        }}
      >
        <div style={{ padding: '14px 14px 10px', borderBottom: '1px solid var(--border-light)' }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <ForumRoundedIcon sx={{ fontSize: 20, color: 'var(--accent-primary)' }} />
              <strong style={{ fontSize: 15 }}>Chat Nội bộ</strong>
            </div>
            <button
              type="button"
              className="btn btn-primary btn-xs"
              onClick={() => setShowCreateChannel(true)}
            >
              <AddRoundedIcon sx={{ fontSize: 15 }} />
              Tạo nhóm
            </button>
          </div>
          <div style={{ position: 'relative' }}>
            <SearchRoundedIcon
              sx={{ fontSize: 16, color: 'var(--text-muted)', position: 'absolute', left: 10, top: 10 }}
            />
            <input
              className="form-input"
              style={{ height: 34, paddingLeft: 32, fontSize: 12.5 }}
              placeholder="Tìm kênh hoặc đồng nghiệp..."
              value={sidebarQuery}
              onChange={(e) => setSidebarQuery(e.target.value)}
            />
          </div>
        </div>

        <div style={{ flex: 1, overflowY: 'auto', padding: '10px 8px' }}>
          <div style={{ fontSize: 10.5, fontWeight: 700, color: 'var(--text-muted)', padding: '4px 10px', textTransform: 'uppercase' }}>
            Kênh &amp; Nhóm làm việc ({groupChannels.length})
          </div>
          {loadingChannels ? (
            <div style={{ padding: 14, fontSize: 12, color: 'var(--text-muted)' }}>Đang tải kênh...</div>
          ) : (
            groupChannels.map((ch) => {
              const active = ch.id === activeChannelId;
              return (
                <button
                  key={ch.id}
                  type="button"
                  onClick={() => setActiveChannelId(ch.id)}
                  style={{
                    width: '100%',
                    textAlign: 'left',
                    padding: '9px 10px',
                    borderRadius: 12,
                    border: 'none',
                    backgroundColor: active ? 'var(--accent-primary-container)' : 'transparent',
                    cursor: 'pointer',
                    display: 'flex',
                    alignItems: 'center',
                    gap: 10,
                    marginBottom: 2,
                  }}
                >
                  <div
                    style={{
                      width: 32,
                      height: 32,
                      borderRadius: 10,
                      backgroundColor: ch.avatarColor || '#1a73e8',
                      color: '#ffffff',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      flexShrink: 0,
                    }}
                  >
                    <TagRoundedIcon sx={{ fontSize: 17 }} />
                  </div>
                  <div style={{ minWidth: 0, flex: 1 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 4 }}>
                      <span
                        style={{
                          fontSize: 13,
                          fontWeight: active || ch.unreadCount > 0 ? 700 : 600,
                          color: active ? 'var(--accent-primary)' : 'var(--text-primary)',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap',
                        }}
                      >
                        {ch.name}
                      </span>
                      {ch.unreadCount > 0 && (
                        <span
                          style={{
                            minWidth: 18,
                            height: 18,
                            padding: '0 5px',
                            borderRadius: 999,
                            backgroundColor: '#d93025',
                            color: '#fff',
                            fontSize: 10,
                            fontWeight: 700,
                            display: 'inline-flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                          }}
                        >
                          {ch.unreadCount}
                        </span>
                      )}
                    </div>
                    <div
                      style={{
                        fontSize: 11.5,
                        color: 'var(--text-muted)',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                      }}
                    >
                      {ch.lastMessagePreview || ch.description || `${ch.memberCount} thành viên`}
                    </div>
                  </div>
                </button>
              );
            })
          )}

          <div style={{ fontSize: 10.5, fontWeight: 700, color: 'var(--text-muted)', padding: '12px 10px 4px', textTransform: 'uppercase' }}>
            Tin nhắn trực tiếp ({dmChannels.length})
          </div>
          {dmChannels.map((ch) => {
            const active = ch.id === activeChannelId;
            return (
              <button
                key={ch.id}
                type="button"
                onClick={() => setActiveChannelId(ch.id)}
                style={{
                  width: '100%',
                  textAlign: 'left',
                  padding: '8px 10px',
                  borderRadius: 12,
                  border: 'none',
                  backgroundColor: active ? 'var(--accent-primary-container)' : 'transparent',
                  cursor: 'pointer',
                  display: 'flex',
                  alignItems: 'center',
                  gap: 10,
                  marginBottom: 2,
                }}
              >
                <div
                  style={{
                    width: 30,
                    height: 30,
                    borderRadius: '50%',
                    backgroundColor: '#0284c7',
                    color: '#fff',
                    fontSize: 12,
                    fontWeight: 700,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    flexShrink: 0,
                  }}
                >
                  {ch.name.charAt(0).toUpperCase()}
                </div>
                <div style={{ minWidth: 0, flex: 1 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <span style={{ fontSize: 13, fontWeight: active || ch.unreadCount > 0 ? 700 : 600, color: 'var(--text-primary)' }}>
                      {ch.name}
                    </span>
                    {ch.unreadCount > 0 && (
                      <span className="badge badge-danger" style={{ fontSize: 10, padding: '1px 6px' }}>
                        {ch.unreadCount}
                      </span>
                    )}
                  </div>
                  <div style={{ fontSize: 11, color: 'var(--text-muted)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {ch.lastMessagePreview || ch.description}
                  </div>
                </div>
              </button>
            );
          })}

          <div style={{ fontSize: 10.5, fontWeight: 700, color: 'var(--text-muted)', padding: '12px 10px 4px', textTransform: 'uppercase' }}>
            Đồng nghiệp ({otherColleagues.length})
          </div>
          {otherColleagues.map((col) => (
            <button
              key={col.username}
              type="button"
              onClick={() => handleStartDirectChat(col.username)}
              style={{
                width: '100%',
                textAlign: 'left',
                padding: '6px 10px',
                borderRadius: 10,
                border: 'none',
                backgroundColor: 'transparent',
                cursor: 'pointer',
                display: 'flex',
                alignItems: 'center',
                gap: 8,
              }}
              title={`Nhắn tin riêng với ${col.fullName}`}
            >
              <PersonRoundedIcon sx={{ fontSize: 16, color: 'var(--text-muted)' }} />
              <div style={{ minWidth: 0, flex: 1 }}>
                <div style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {col.fullName}
                </div>
                <div style={{ fontSize: 10.5, color: 'var(--text-muted)' }}>@{col.username} • {col.roleCode}</div>
              </div>
            </button>
          ))}
        </div>
      </div>

      {/* Center Pane: Conversation Stream */}
      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
        {activeChannel ? (
          <>
            {/* Channel Header */}
            <div
              style={{
                padding: '12px 20px',
                backgroundColor: 'var(--bg-surface)',
                borderBottom: '1px solid var(--border-subtle)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 12,
                flexWrap: 'wrap',
              }}
            >
              <div style={{ minWidth: 0 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <strong style={{ fontSize: 15.5, color: 'var(--text-primary)' }}>
                    {activeChannel.channelType === 'DIRECT' ? activeChannel.name : `# ${activeChannel.name}`}
                  </strong>
                  {activeChannel.linkedAppCode && (
                    <Link href={`/apps/${activeChannel.linkedAppCode}`} className="badge badge-info" style={{ textDecoration: 'none' }}>
                      App: {activeChannel.linkedAppCode}
                    </Link>
                  )}
                  <span className="badge" style={{ backgroundColor: 'var(--bg-secondary)', color: 'var(--text-secondary)' }}>
                    {activeChannel.memberCount} thành viên
                  </span>
                </div>
                {activeChannel.description && (
                  <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
                    {activeChannel.description}
                  </div>
                )}
              </div>

              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <button
                  type="button"
                  className={`btn btn-xs ${showPinnedOnly ? 'btn-primary' : 'btn-outline'}`}
                  onClick={() => setShowPinnedOnly((p) => !p)}
                  title="Lọc tin nhắn đã ghim trong lịch sử đã tải"
                >
                  <PushPinRoundedIcon sx={{ fontSize: 14 }} />
                  Đã ghim ({messages.filter((m) => m.isPinned).length})
                </button>
                {systemSettings.allowTaskFromChat && (
                  <button
                    type="button"
                    className="btn btn-primary btn-xs"
                    onClick={openAssignTaskInChannel}
                  >
                    <AssignmentTurnedInRoundedIcon sx={{ fontSize: 14 }} />
                    Giao việc trong nhóm
                  </button>
                )}
                <button
                  type="button"
                  className={`btn btn-xs ${showRightPanel ? 'btn-secondary' : 'btn-outline'}`}
                  onClick={() => setShowRightPanel((p) => !p)}
                  title="Hiện / Ẩn bảng công việc & thành viên"
                >
                  <ViewSidebarRoundedIcon sx={{ fontSize: 14 }} />
                  Công việc ({channelTasks.length})
                </button>
              </div>
            </div>

            {/* Messages Area */}
            <div ref={messagesScrollRef} aria-busy={loadingMsgs || loadingOlder} style={{ flex: 1, overflowY: 'auto', padding: '16px 22px', display: 'flex', flexDirection: 'column', gap: 12 }}>
              {!loadingMsgs && nextCursor && (
                <div style={{ textAlign: 'center', flexShrink: 0 }}>
                  <button type="button" className="btn btn-outline btn-sm" onClick={loadOlderMessages} disabled={loadingOlder}>
                    {loadingOlder ? 'Đang tải tin nhắn cũ...' : 'Tải tin nhắn cũ hơn'}
                  </button>
                </div>
              )}
              {loadingMsgs ? (
                <div style={{ textAlign: 'center', padding: 32, color: 'var(--text-muted)' }}>Đang tải hội thoại...</div>
              ) : displayedMessages.length === 0 ? (
                <div style={{ textAlign: 'center', padding: 40, color: 'var(--text-muted)', fontSize: 13 }}>
                  {showPinnedOnly ? 'Chưa có tin nhắn nào được ghim trong kênh này.' : 'Hãy gửi tin nhắn đầu tiên hoặc nhấn "Giao việc trong nhóm"!'}
                </div>
              ) : (
                displayedMessages.map((msg) => {
                  if (msg.messageType === 'SYSTEM') {
                    return (
                      <div key={msg.id} style={{ textAlign: 'center', fontSize: 11.5, color: 'var(--text-muted)', padding: '4px 12px' }}>
                        {msg.content} • {new Date(msg.createdAt).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}
                      </div>
                    );
                  }

                  const isMine = msg.senderUsername.toLowerCase() === user?.username?.toLowerCase();
                  const reactionEntries = Object.entries(msg.reactions || {}).filter(([, arr]) => arr.length > 0);

                  return (
                    <div
                      key={msg.id}
                      style={{
                        display: 'flex',
                        gap: 10,
                        alignItems: 'flex-start',
                        padding: '8px 12px',
                        borderRadius: 14,
                        backgroundColor: msg.isPinned
                          ? 'rgba(234, 134, 0, 0.08)'
                          : isMine
                            ? 'var(--bg-surface)'
                            : 'var(--bg-surface)',
                        border: msg.isPinned ? '1px solid #feefc3' : '1px solid var(--border-light)',
                      }}
                    >
                      <div
                        style={{
                          width: 34,
                          height: 34,
                          borderRadius: '50%',
                          backgroundColor: isMine ? 'var(--accent-primary)' : '#5f6368',
                          color: '#ffffff',
                          fontWeight: 700,
                          fontSize: 13,
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          flexShrink: 0,
                        }}
                      >
                        {(msg.senderFullName || msg.senderUsername).charAt(0).toUpperCase()}
                      </div>

                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, flexWrap: 'wrap' }}>
                          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                            <strong style={{ fontSize: 13, color: 'var(--text-primary)' }}>
                              {msg.senderFullName || msg.senderUsername}
                            </strong>
                            <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>@{msg.senderUsername}</span>
                            {msg.senderRoleCode && (
                              <span className="badge badge-info" style={{ fontSize: 10, padding: '1px 6px' }}>
                                {msg.senderRoleCode}
                              </span>
                            )}
                            <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>
                              {new Date(msg.createdAt).toLocaleString('vi-VN', { hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit' })}
                            </span>
                            {msg.isPinned && (
                              <span className="badge badge-warning" style={{ fontSize: 10, padding: '1px 6px' }}>
                                Đã ghim
                              </span>
                            )}
                          </div>

                          {/* Message Quick Actions */}
                          <div style={{ display: 'flex', alignItems: 'center', gap: 4, position: 'relative' }}>
                            <button
                              type="button"
                              onClick={() => setEmojiPickerForMsg(emojiPickerForMsg === msg.id ? null : msg.id)}
                              title="Thả cảm xúc"
                              className="btn btn-outline btn-xs"
                              style={{ padding: '0 6px !important' }}
                            >
                              <AddReactionRoundedIcon sx={{ fontSize: 14 }} />
                            </button>
                            <button
                              type="button"
                              onClick={() => setReplyTo(msg)}
                              title="Trả lời tin nhắn"
                              className="btn btn-outline btn-xs"
                              style={{ padding: '0 6px !important' }}
                            >
                              <ReplyRoundedIcon sx={{ fontSize: 14 }} />
                            </button>
                            {systemSettings.allowTaskFromChat && msg.messageType !== 'TASK_CARD' && (
                              <button
                                type="button"
                                onClick={() => openConvertMessageToTask(msg)}
                                title="Giao việc từ tin nhắn này"
                                className="btn btn-outline btn-xs"
                                style={{ color: 'var(--accent-primary)', fontSize: '11px !important' }}
                              >
                                <AssignmentTurnedInRoundedIcon sx={{ fontSize: 13 }} />
                                Giao việc
                              </button>
                            )}
                            <button
                              type="button"
                              onClick={() => handleTogglePin(msg.id)}
                              title={msg.isPinned ? 'Bỏ ghim' : 'Ghim tin nhắn'}
                              className="btn btn-outline btn-xs"
                              style={{ padding: '0 6px !important' }}
                            >
                              <PushPinRoundedIcon sx={{ fontSize: 13 }} />
                            </button>
                            {(isMine || user?.isAdmin || user?.roleCode === 'ADMIN' || user?.roleCodes?.includes('ADMIN')) && (
                              <button
                                type="button"
                                onClick={() => handleDeleteMessage(msg.id)}
                                title="Xóa tin nhắn"
                                className="btn btn-outline btn-xs"
                                style={{ padding: '0 6px !important', color: '#d93025' }}
                              >
                                <DeleteOutlineRoundedIcon sx={{ fontSize: 14 }} />
                              </button>
                            )}

                            {emojiPickerForMsg === msg.id && (
                              <div
                                style={{
                                  position: 'absolute',
                                  top: '100%',
                                  right: 0,
                                  marginTop: 4,
                                  padding: '4px 6px',
                                  borderRadius: 999,
                                  backgroundColor: 'var(--bg-surface)',
                                  border: '1px solid var(--border-subtle)',
                                  boxShadow: 'var(--shadow-md)',
                                  display: 'flex',
                                  gap: 4,
                                  zIndex: 40,
                                }}
                              >
                                {QUICK_EMOJIS.map((em) => (
                                  <button
                                    key={em}
                                    type="button"
                                    onClick={() => handleToggleReaction(msg.id, em)}
                                    style={{ border: 'none', background: 'transparent', cursor: 'pointer', fontSize: 16, padding: 2 }}
                                  >
                                    {em}
                                  </button>
                                ))}
                              </div>
                            )}
                          </div>
                        </div>

                        {/* Reply Preview */}
                        {msg.replyToId && msg.replyToPreview && (
                          <div
                            style={{
                              marginTop: 4,
                              padding: '4px 10px',
                              borderLeft: '3px solid var(--accent-primary)',
                              backgroundColor: 'var(--bg-secondary)',
                              borderRadius: 6,
                              fontSize: 11.5,
                              color: 'var(--text-secondary)',
                            }}
                          >
                            <strong>@{msg.replyToSender}:</strong> {msg.replyToPreview}
                          </div>
                        )}

                        {/* Message Body */}
                        <div style={{ fontSize: 13.5, color: 'var(--text-primary)', marginTop: 4, lineHeight: 1.5, whiteSpace: 'pre-wrap' }}>
                          {renderContentWithMentions(msg.content)}
                        </div>

                        {/* File Attachments */}
                        {msg.attachments && msg.attachments.length > 0 && (
                          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginTop: 8 }}>
                            {msg.attachments.map((att) => (
                              <a
                                key={att.id}
                                href={att.url}
                                target="_blank"
                                rel="noreferrer"
                                style={{
                                  display: 'inline-flex',
                                  alignItems: 'center',
                                  gap: 6,
                                  padding: '6px 10px',
                                  borderRadius: 10,
                                  border: '1px solid var(--border-subtle)',
                                  backgroundColor: 'var(--bg-secondary)',
                                  color: 'var(--text-primary)',
                                  textDecoration: 'none',
                                  fontSize: 12,
                                  fontWeight: 600,
                                }}
                              >
                                <InsertDriveFileRoundedIcon sx={{ fontSize: 16, color: 'var(--accent-primary)' }} />
                                <span>{att.fileName}</span>
                              </a>
                            ))}
                          </div>
                        )}

                        {/* Integrated Interactive Task Card */}
                        {msg.task && <TaskCardInChat task={msg.task} onTaskUpdated={handleTaskUpdated} />}

                        {/* Emoji Reactions */}
                        {reactionEntries.length > 0 && (
                          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginTop: 8 }}>
                            {reactionEntries.map(([emoji, reactors]) => {
                              const reacted = reactors.some((u) => u.toLowerCase() === user?.username?.toLowerCase());
                              return (
                                <button
                                  key={emoji}
                                  type="button"
                                  onClick={() => handleToggleReaction(msg.id, emoji)}
                                  style={{
                                    padding: '2px 8px',
                                    borderRadius: 999,
                                    border: `1px solid ${reacted ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
                                    backgroundColor: reacted ? 'var(--accent-primary-container)' : 'var(--bg-secondary)',
                                    color: 'var(--text-primary)',
                                    fontSize: 12,
                                    cursor: 'pointer',
                                    display: 'inline-flex',
                                    alignItems: 'center',
                                    gap: 4,
                                  }}
                                  title={reactors.join(', ')}
                                >
                                  <span>{emoji}</span>
                                  <span style={{ fontWeight: 700, fontSize: 11 }}>{reactors.length}</span>
                                </button>
                              );
                            })}
                          </div>
                        )}
                      </div>
                    </div>
                  );
                })
              )}
              <div ref={messagesEndRef} />
            </div>

            {/* Composer Box */}
            <div
              style={{
                padding: '10px 20px 14px',
                backgroundColor: 'var(--bg-surface)',
                borderTop: '1px solid var(--border-subtle)',
                position: 'relative',
              }}
            >
              {/* Mention Autocomplete Popover */}
              {mentionQuery !== null && mentionCandidates.length > 0 && (
                <div
                  style={{
                    position: 'absolute',
                    bottom: '100%',
                    left: 20,
                    marginBottom: 6,
                    width: 280,
                    maxHeight: 200,
                    overflowY: 'auto',
                    backgroundColor: 'var(--bg-surface)',
                    border: '1px solid var(--border-subtle)',
                    borderRadius: 12,
                    boxShadow: 'var(--shadow-lg)',
                    padding: 6,
                    zIndex: 50,
                  }}
                >
                  {mentionCandidates.map((u) => (
                    <button
                      key={u.username}
                      type="button"
                      onClick={() => insertMention(u.username)}
                      style={{
                        width: '100%',
                        textAlign: 'left',
                        padding: '6px 10px',
                        borderRadius: 8,
                        border: 'none',
                        backgroundColor: 'transparent',
                        cursor: 'pointer',
                        display: 'flex',
                        justifyContent: 'space-between',
                        fontSize: 12.5,
                        color: 'var(--text-primary)',
                      }}
                    >
                      <strong>{u.fullName}</strong>
                      <span style={{ color: 'var(--text-muted)' }}>@{u.username}</span>
                    </button>
                  ))}
                </div>
              )}

              {/* Slash Task Helper Hint */}
              {draft.startsWith('/') && systemSettings.allowTaskFromChat && (
                <div
                  style={{
                    marginBottom: 8,
                    padding: '6px 12px',
                    borderRadius: 10,
                    backgroundColor: 'var(--accent-primary-container)',
                    color: 'var(--accent-primary)',
                    fontSize: 12,
                    fontWeight: 600,
                  }}
                >
                  💡 Lệnh nhanh giao việc: gõ <code>/task @username Tiêu đề công việc</code> hoặc <code>/giaoviec @username Nội dung</code> rồi nhấn Gửi!
                </div>
              )}

              {replyTo && (
                <div
                  style={{
                    marginBottom: 8,
                    padding: '6px 12px',
                    borderRadius: 10,
                    backgroundColor: 'var(--bg-secondary)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    fontSize: 12,
                  }}
                >
                  <span>
                    Đang trả lời <strong>@{replyTo.senderUsername}</strong>: {replyTo.content.slice(0, 80)}
                  </span>
                  <button
                    type="button"
                    onClick={() => setReplyTo(null)}
                    style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: 'var(--text-muted)' }}
                  >
                    <CloseRoundedIcon sx={{ fontSize: 16 }} />
                  </button>
                </div>
              )}

              {pendingAttachments.length > 0 && (
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginBottom: 8 }}>
                  {pendingAttachments.map((att) => (
                    <span
                      key={att.id}
                      className="badge badge-info"
                      style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}
                    >
                      {att.fileName}
                      <button
                        type="button"
                        onClick={() => setPendingAttachments((prev) => prev.filter((x) => x.id !== att.id))}
                        style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: 'inherit', padding: 0 }}
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
              )}

              <form onSubmit={handleSendMessage} style={{ display: 'flex', alignItems: 'flex-end', gap: 8 }}>
                <input ref={fileInputRef} type="file" hidden onChange={handleUploadFile} />
                {systemSettings.allowChatFileUpload && (
                  <button
                    type="button"
                    className="btn btn-outline btn-sm"
                    disabled={uploadingFile}
                    onClick={() => fileInputRef.current?.click()}
                    title="Đính kèm tệp hoặc hình ảnh"
                  >
                    <AttachFileRoundedIcon sx={{ fontSize: 17 }} />
                  </button>
                )}

                {systemSettings.allowTaskFromChat && (
                  <button
                    type="button"
                    className="btn btn-outline btn-sm"
                    style={{ color: 'var(--accent-primary)', borderColor: 'var(--accent-primary)' }}
                    onClick={openAssignTaskInChannel}
                    title="Giao việc tích hợp vào đoạn chat"
                  >
                    <AssignmentTurnedInRoundedIcon sx={{ fontSize: 16 }} />
                    Giao việc
                  </button>
                )}

                <textarea
                  className="form-textarea"
                  rows={1}
                  style={{ minHeight: 38, maxHeight: 110, flex: 1, padding: '8px 12px', resize: 'none' }}
                  placeholder={`Nhắn tin tới ${activeChannel.name}... (@ để nhắc tên, /task @user để giao việc)`}
                  value={draft}
                  onChange={(e) => handleDraftChange(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.nativeEvent.isComposing || e.keyCode === 229) return;
                    if (userSettings.chatEnterToSend && e.key === 'Enter' && !e.shiftKey) {
                      e.preventDefault();
                      handleSendMessage();
                    } else if (!userSettings.chatEnterToSend && e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                      e.preventDefault();
                      handleSendMessage();
                    }
                  }}
                />

                <button type="submit" disabled={sending} className="btn btn-primary btn-sm">
                  <SendRoundedIcon sx={{ fontSize: 16 }} />
                  Gửi
                </button>
              </form>
            </div>
          </>
        ) : (
          <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--text-muted)' }}>
            Chọn một kênh chat bên trái để bắt đầu trao đổi &amp; giao việc.
          </div>
        )}
      </div>

      {/* Right Pane: Channel Integrated Tasks & Members */}
      {showRightPanel && activeChannel && (
        <div
          style={{
            width: 330,
            flexShrink: 0,
            backgroundColor: 'var(--bg-surface)',
            borderLeft: '1px solid var(--border-subtle)',
            display: 'flex',
            flexDirection: 'column',
            overflow: 'hidden',
          }}
        >
          <div style={{ padding: '10px 12px', borderBottom: '1px solid var(--border-light)', display: 'flex', gap: 6 }}>
            <button
              type="button"
              className={`btn btn-xs ${rightTab === 'tasks' ? 'btn-primary' : 'btn-outline'}`}
              style={{ flex: 1 }}
              onClick={() => setRightTab('tasks')}
            >
              Công việc ({channelTasks.length})
            </button>
            <button
              type="button"
              className={`btn btn-xs ${rightTab === 'members' ? 'btn-primary' : 'btn-outline'}`}
              style={{ flex: 1 }}
              onClick={() => setRightTab('members')}
            >
              Thành viên ({activeChannel.members.length})
            </button>
          </div>

          <div style={{ flex: 1, overflowY: 'auto', padding: 12 }}>
            {rightTab === 'tasks' ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6 }}>
                  <div style={{ display: 'flex', gap: 4 }}>
                    {(
                      [
                        ['ALL', 'Tất cả'],
                        ['OPEN', 'Chưa xong'],
                        ['DONE', 'Xong'],
                      ] as const
                    ).map(([k, lbl]) => (
                      <button
                        key={k}
                        type="button"
                        onClick={() => setTaskFilter(k)}
                        style={{
                          padding: '3px 8px',
                          borderRadius: 999,
                          border: '1px solid var(--border-light)',
                          backgroundColor: taskFilter === k ? 'var(--accent-primary-container)' : 'var(--bg-secondary)',
                          color: taskFilter === k ? 'var(--accent-primary)' : 'var(--text-secondary)',
                          fontSize: 11,
                          fontWeight: 600,
                          cursor: 'pointer',
                        }}
                      >
                        {lbl}
                      </button>
                    ))}
                  </div>
                  {systemSettings.allowTaskFromChat && (
                    <button type="button" className="btn btn-outline btn-xs" onClick={openAssignTaskInChannel}>
                      <AddRoundedIcon sx={{ fontSize: 14 }} />
                      Giao việc
                    </button>
                  )}
                </div>

                {displayedChannelTasks.length === 0 ? (
                  <div style={{ textAlign: 'center', padding: 24, fontSize: 12.5, color: 'var(--text-muted)' }}>
                    Chưa có công việc nào trong cuộc trò chuyện này.
                  </div>
                ) : (
                  displayedChannelTasks.map((t) => (
                    <TaskCardInChat key={t.id} task={t} compact onTaskUpdated={handleTaskUpdated} />
                  ))
                )}
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                {activeChannel.channelType !== 'DIRECT' && nonMemberUsers.length > 0 && (
                  <div style={{ display: 'flex', gap: 6 }}>
                    <select
                      className="form-select"
                      style={{ height: 32, fontSize: 12 }}
                      value={addMemberUser}
                      onChange={(e) => setAddMemberUser(e.target.value)}
                    >
                      <option value="">-- Thêm thành viên --</option>
                      {nonMemberUsers.map((u) => (
                        <option key={u.username} value={u.username}>
                          {u.fullName} (@{u.username})
                        </option>
                      ))}
                    </select>
                    <button
                      type="button"
                      className="btn btn-primary btn-xs"
                      disabled={!addMemberUser}
                      onClick={async () => {
                        if (!addMemberUser) return;
                        await fetch(`/api/chat/channels/${encodeURIComponent(activeChannel.id)}/members`, {
                          method: 'POST',
                          headers: { 'Content-Type': 'application/json' },
                          credentials: 'include',
                          body: JSON.stringify({ username: addMemberUser }),
                        });
                        setAddMemberUser('');
                        await loadChannelsAndUsers();
                        await loadChannelContent(activeChannel.id, true);
                      }}
                    >
                      <GroupAddRoundedIcon sx={{ fontSize: 15 }} />
                    </button>
                  </div>
                )}

                {activeChannel.members.map((m) => (
                  <div
                    key={m.username}
                    style={{
                      padding: '8px 10px',
                      borderRadius: 10,
                      backgroundColor: 'var(--bg-secondary)',
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      gap: 8,
                    }}
                  >
                    <div>
                      <div style={{ fontSize: 12.5, fontWeight: 700, color: 'var(--text-primary)' }}>{m.fullName}</div>
                      <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>@{m.username} • {m.department}</div>
                    </div>
                    <span className="badge badge-info" style={{ fontSize: 10 }}>{m.roleCode}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      )}

      {/* Create Group Channel Modal */}
      {showCreateChannel && (
        <div className="modal-overlay" onClick={() => setShowCreateChannel(false)}>
          <div className="modal-dialog" style={{ maxWidth: 480 }} onClick={(e) => e.stopPropagation()}>
            <h3 style={{ fontSize: 16.5, fontWeight: 700, marginBottom: 12 }}>Tạo nhóm Chat nội bộ</h3>
            <form onSubmit={handleCreateChannel} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <div>
                <label className="form-label">Tên kênh / nhóm *</label>
                <input
                  className="form-input"
                  placeholder="Ví dụ: Dự án Chuyển đổi số Q4"
                  value={newChName}
                  onChange={(e) => setNewChName(e.target.value)}
                  required
                  autoFocus
                />
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                <div>
                  <label className="form-label">Loại kênh</label>
                  <select className="form-select" value={newChType} onChange={(e) => setNewChType(e.target.value as 'GROUP' | 'PUBLIC')}>
                    <option value="GROUP">Nhóm riêng (Theo thành viên)</option>
                    <option value="PUBLIC">Công khai (Toàn công ty)</option>
                  </select>
                </div>
                <div>
                  <label className="form-label">Liên kết App (tùy chọn)</label>
                  <select className="form-select" value={newChApp} onChange={(e) => setNewChApp(e.target.value)}>
                    <option value="">-- Không --</option>
                    {apps.map((a) => (
                      <option key={a.id} value={a.code}>{a.code} — {a.name}</option>
                    ))}
                  </select>
                </div>
              </div>
              <div>
                <label className="form-label">Mô tả mục đích nhóm</label>
                <input
                  className="form-input"
                  placeholder="Mô tả ngắn..."
                  value={newChDesc}
                  onChange={(e) => setNewChDesc(e.target.value)}
                />
              </div>
              {newChType === 'GROUP' && (
                <div>
                  <label className="form-label">Chọn thành viên tham gia</label>
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                    {users
                      .filter((u) => u.username !== user?.username)
                      .map((u) => {
                        const checked = newChMembers.includes(u.username);
                        return (
                          <button
                            key={u.username}
                            type="button"
                            onClick={() =>
                              setNewChMembers((prev) =>
                                checked ? prev.filter((x) => x !== u.username) : [...prev, u.username]
                              )
                            }
                            style={{
                              padding: '5px 10px',
                              borderRadius: 999,
                              border: `1px solid ${checked ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
                              backgroundColor: checked ? 'var(--accent-primary-container)' : 'var(--bg-secondary)',
                              color: checked ? 'var(--accent-primary)' : 'var(--text-primary)',
                              fontSize: 12,
                              fontWeight: 600,
                              cursor: 'pointer',
                            }}
                          >
                            {u.fullName} (@{u.username})
                          </button>
                        );
                      })}
                  </div>
                </div>
              )}
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 6 }}>
                <button type="button" className="btn btn-outline btn-sm" onClick={() => setShowCreateChannel(false)}>
                  Hủy
                </button>
                <button type="submit" className="btn btn-primary btn-sm">
                  Tạo nhóm Chat
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Assign Task Inside Chat Modal */}
      <AssignTaskModal
        open={taskModalOpen}
        users={users}
        defaultChannelId={activeChannelId}
        defaultAssignee={taskDefaultAssignee}
        initialTitle={taskInitialTitle}
        initialDescription={taskInitialDesc}
        submitting={submittingTask}
        onClose={() => {
          setTaskModalOpen(false);
          setConvertingMsgId(null);
        }}
        onSubmit={handleSubmitTask}
      />

      <NotificationSnackbar
        open={snack.open}
        message={snack.message}
        severity={snack.severity}
        onClose={() => setSnack((s) => ({ ...s, open: false }))}
      />
    </div>
  );
}

export default function ChatPage() {
  return (
    <Suspense fallback={<div style={{ padding: 32, color: 'var(--text-muted)' }}>Đang mở Chat nội bộ...</div>}>
      <ChatWorkspaceInner />
    </Suspense>
  );
}
