export interface DesktopInfo {
  os: string;
  arch: string;
  runtime: string;
  appVersion: string;
  dataDir: string;
}

export interface DesktopServerConfig {
  webUrl: string;
  apiUrl: string;
}

export interface OfflineMutationItem {
  id: string;
  appCode: string;
  tableId: string;
  method: string;
  endpoint: string;
  payload: Record<string, unknown>;
  createdAt: string;
}

export interface OfflineSnapshotEnvelope<T = unknown> {
  savedAt: string;
  payload: T;
}

export interface LocalSignRequest {
  documentId: string;
  documentHash: string;
  providerCode?: string;
  signerPinHint?: string;
}

export interface LocalSignResult {
  success: boolean;
  provider: string;
  signTransactionId?: string;
  signatureBase64?: string;
  certificateSubject?: string;
  certificateSerial?: string;
  signedAt?: string;
  message: string;
}

type TauriInvokeFn = (cmd: string, args?: Record<string, unknown>) => Promise<unknown>;

interface TauriWindowLike {
  __TAURI__?: { core?: { invoke?: TauriInvokeFn } };
  __TAURI_INTERNALS__?: { invoke?: TauriInvokeFn };
}

const LOCAL_QUEUE_KEY = 'smartapps_offline_queue_v1';

function getTauriInvoke(): TauriInvokeFn | null {
  if (typeof window === 'undefined') return null;
  const w = window as unknown as TauriWindowLike;
  return w.__TAURI__?.core?.invoke || w.__TAURI_INTERNALS__?.invoke || null;
}

export function isTauriDesktop(): boolean {
  return Boolean(getTauriInvoke());
}

export async function getDesktopInfo(): Promise<DesktopInfo | null> {
  const invoke = getTauriInvoke();
  if (!invoke) return null;
  try {
    return (await invoke('get_desktop_info')) as DesktopInfo;
  } catch {
    return null;
  }
}

export async function getDesktopServerConfig(): Promise<DesktopServerConfig> {
  const invoke = getTauriInvoke();
  if (invoke) {
    try {
      return (await invoke('get_server_config')) as DesktopServerConfig;
    } catch {
      // fallback below
    }
  }
  return {
    webUrl: typeof window !== 'undefined' ? window.location.origin : 'http://localhost:4100',
    apiUrl: 'http://localhost:5200',
  };
}

export async function setDesktopServerConfig(webUrl: string, apiUrl: string): Promise<DesktopServerConfig | null> {
  const invoke = getTauriInvoke();
  if (!invoke) return null;
  return (await invoke('set_server_config', { webUrl, apiUrl })) as DesktopServerConfig;
}

export async function openAppDesktopWindow(appCode: string, appName: string): Promise<string | null> {
  const invoke = getTauriInvoke();
  if (!invoke) return null;
  return (await invoke('open_app_window', {
    appCode,
    appName,
    baseUrl: typeof window !== 'undefined' ? window.location.origin : 'http://localhost:4100',
  })) as string;
}

export async function saveOfflineSnapshot(appCode: string, payload: unknown, userKey?: string): Promise<string | null> {
  const targetKey = userKey !== undefined ? scopeOfflineKey(appCode, userKey) : appCode;
  const envelope: OfflineSnapshotEnvelope = {
    savedAt: new Date().toISOString(),
    payload,
  };
  const serialized = JSON.stringify(envelope);

  let nativePath: string | null = null;
  const invoke = getTauriInvoke();
  if (invoke) {
    try {
      nativePath = (await invoke('save_offline_snapshot', {
        appCode: targetKey,
        jsonPayload: serialized,
      })) as string;
    } catch {
      // fall through to localStorage persistence
    }
  }

  if (typeof window !== 'undefined') {
    try {
      localStorage.setItem(`smartapps_offline_snapshot_${targetKey}`, serialized);
      if (!nativePath) return `localStorage:${targetKey}`;
    } catch {
      // ignore storage quota errors
    }
  }
  return nativePath;
}

export async function exportDesktopReport(filename: string, content: string): Promise<string | null> {
  const invoke = getTauriInvoke();
  if (!invoke) return null;
  return (await invoke('export_report_file', { filename, content })) as string;
}

export async function queueOfflineMutation(item: OfflineMutationItem): Promise<OfflineMutationItem[]> {
  const invoke = getTauriInvoke();
  if (invoke) {
    try {
      return (await invoke('queue_offline_mutation', { item })) as OfflineMutationItem[];
    } catch {
      // fall through to localStorage fallback
    }
  }
  const existing = await listOfflineQueue();
  const next = [...existing, item];
  if (typeof window !== 'undefined') localStorage.setItem(LOCAL_QUEUE_KEY, JSON.stringify(next));
  return next;
}

export async function listOfflineQueue(): Promise<OfflineMutationItem[]> {
  const invoke = getTauriInvoke();
  if (invoke) {
    try {
      return (await invoke('list_offline_queue')) as OfflineMutationItem[];
    } catch {
      // fall through
    }
  }
  if (typeof window === 'undefined') return [];
  try {
    return JSON.parse(localStorage.getItem(LOCAL_QUEUE_KEY) || '[]') as OfflineMutationItem[];
  } catch {
    return [];
  }
}

export async function removeOfflineMutation(id: string): Promise<OfflineMutationItem[]> {
  const invoke = getTauriInvoke();
  if (invoke) {
    try {
      return (await invoke('remove_offline_mutation', { id })) as OfflineMutationItem[];
    } catch {
      // fall through
    }
  }
  const existing = await listOfflineQueue();
  const next = existing.filter((x) => x.id !== id);
  if (typeof window !== 'undefined') localStorage.setItem(LOCAL_QUEUE_KEY, JSON.stringify(next));
  return next;
}

const OFFLINE_CONFLICT_KEY = 'smartapps_offline_conflicts_v1';

export interface OfflineSyncErrorMeta {
  status: 'conflict' | 'failed';
  message: string;
  updatedAt: string;
}

export function scopeOfflineKey(appCode: string, userKey?: string): string {
  const cleanApp = (appCode || 'APP').replace(/[^a-zA-Z0-9_-]/g, '_');
  const originHost =
    typeof window !== 'undefined'
      ? window.location.host.replace(/[^a-zA-Z0-9_-]/g, '_')
      : 'ssr';
  const cleanUser = (userKey || 'anon').replace(/[^a-zA-Z0-9_-]/g, '_');
  return `${cleanApp}_${originHost}_${cleanUser}`;
}

export function getOfflineConflictMap(): Record<string, OfflineSyncErrorMeta> {
  if (typeof window === 'undefined') return {};
  try {
    const raw = localStorage.getItem(OFFLINE_CONFLICT_KEY);
    return raw ? (JSON.parse(raw) as Record<string, OfflineSyncErrorMeta>) : {};
  } catch {
    return {};
  }
}

function saveOfflineConflictMap(map: Record<string, OfflineSyncErrorMeta>): void {
  if (typeof window === 'undefined') return;
  try {
    localStorage.setItem(OFFLINE_CONFLICT_KEY, JSON.stringify(map));
  } catch {
    // ignore storage quota errors
  }
}

function unwrapOfflineSnapshot<T>(raw: string | null): { payload: T | null; savedAt: string | null } {
  if (!raw) return { payload: null, savedAt: null };
  try {
    const parsed = JSON.parse(raw) as unknown;
    if (
      parsed &&
      typeof parsed === 'object' &&
      'savedAt' in parsed &&
      'payload' in parsed &&
      typeof (parsed as { savedAt: unknown }).savedAt === 'string'
    ) {
      const env = parsed as OfflineSnapshotEnvelope<T>;
      return { payload: env.payload, savedAt: env.savedAt };
    }
    return { payload: parsed as T, savedAt: null };
  } catch {
    return { payload: null, savedAt: null };
  }
}

export async function loadOfflineSnapshot<T = unknown>(appCode: string, userKey?: string): Promise<T | null> {
  const scoped = scopeOfflineKey(appCode, userKey);
  if (typeof window === 'undefined') return null;
  const invoke = getTauriInvoke();
  if (invoke) {
    try {
      const raw = (await invoke('load_offline_snapshot', { appCode: scoped })) as string;
      const unwrapped = unwrapOfflineSnapshot<T>(raw);
      if (unwrapped.payload !== null) return unwrapped.payload;
    } catch {
      // fallback to localStorage
    }
  }
  const raw =
    localStorage.getItem(`smartapps_offline_snapshot_${scoped}`) ??
    localStorage.getItem(`smartapps_offline_snapshot_${appCode}`);
  return unwrapOfflineSnapshot<T>(raw).payload;
}

export function getOfflineSnapshotInfo(appCode: string, userKey?: string): { savedAt: string | null } {
  if (typeof window === 'undefined') return { savedAt: null };
  const scoped = scopeOfflineKey(appCode, userKey);
  const raw =
    localStorage.getItem(`smartapps_offline_snapshot_${scoped}`) ??
    localStorage.getItem(`smartapps_offline_snapshot_${appCode}`);
  return { savedAt: unwrapOfflineSnapshot(raw).savedAt };
}

async function syncOneOfflineItem(
  item: OfflineMutationItem,
  conflictMap: Record<string, OfflineSyncErrorMeta>,
  options?: { forceOverwrite?: boolean }
): Promise<'synced' | 'conflict' | 'failed'> {
  const payload = options?.forceOverwrite && item.payload && typeof item.payload === 'object'
    ? Object.fromEntries(Object.entries(item.payload).filter(([k]) => k !== 'expectedVersion'))
    : item.payload;
  const idempotencyKey = options?.forceOverwrite ? `${item.id}-force-${Date.now()}` : item.id;

  try {
    const res = await fetch(item.endpoint, {
      method: item.method || 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      credentials: 'include',
      body: JSON.stringify(payload),
    });
    if (res.ok) {
      await removeOfflineMutation(item.id);
      delete conflictMap[item.id];
      return 'synced';
    }
    if (res.status === 409) {
      const body = (await res.json().catch(() => ({ message: 'Dữ liệu máy chủ đã thay đổi (409).' }))) as { message?: string };
      conflictMap[item.id] = {
        status: 'conflict',
        message: String(body?.message || 'Xung đột phiên bản trên máy chủ (409).'),
        updatedAt: new Date().toISOString(),
      };
      return 'conflict';
    }
    const body = (await res.json().catch(() => ({ message: `Lỗi HTTP ${res.status}` }))) as { message?: string };
    conflictMap[item.id] = {
      status: 'failed',
      message: String(body?.message || `Lỗi đồng bộ HTTP ${res.status}`),
      updatedAt: new Date().toISOString(),
    };
    return 'failed';
  } catch {
    conflictMap[item.id] = {
      status: 'failed',
      message: 'Chưa kết nối được tới máy chủ.',
      updatedAt: new Date().toISOString(),
    };
    return 'failed';
  }
}

export async function syncSingleOfflineMutation(
  id: string,
  options?: { forceOverwrite?: boolean }
): Promise<{ status: 'synced' | 'conflict' | 'failed' | 'not_found'; message?: string }> {
  const items = await listOfflineQueue();
  const target = items.find((x) => x.id === id);
  if (!target) return { status: 'not_found', message: 'Không tìm thấy mục trong hàng đợi.' };
  const conflictMap = getOfflineConflictMap();
  const status = await syncOneOfflineItem(target, conflictMap, options);
  saveOfflineConflictMap(conflictMap);
  return { status, message: conflictMap[id]?.message };
}

export async function discardOfflineMutation(id: string): Promise<OfflineMutationItem[]> {
  const left = await removeOfflineMutation(id);
  const conflictMap = getOfflineConflictMap();
  if (id in conflictMap) {
    delete conflictMap[id];
    saveOfflineConflictMap(conflictMap);
  }
  return left;
}

export async function syncAllOfflineMutations(): Promise<{ synced: number; failed: number; conflicts: number }> {
  const items = await listOfflineQueue();
  const conflictMap = getOfflineConflictMap();
  let synced = 0;
  let failed = 0;
  let conflicts = 0;
  for (const item of items) {
    const outcome = await syncOneOfflineItem(item, conflictMap);
    if (outcome === 'synced') synced++;
    else if (outcome === 'conflict') {
      conflicts++;
      failed++;
    } else {
      failed++;
    }
  }
  saveOfflineConflictMap(conflictMap);
  return { synced, failed, conflicts };
}

/**
 * Gửi yêu cầu ký số qua Tauri USB Token PKCS#11 bridge hoặc agent ký cục bộ (VNPT-CA / Viettel-CA / BKAV-CA).
 */
export async function requestLocalTokenSign(
  req: LocalSignRequest,
  localAgentUrl = 'http://127.0.0.1:18080/api/sign'
): Promise<LocalSignResult> {
  const invoke = getTauriInvoke();
  if (invoke) {
    try {
      return (await invoke('sign_document_pkcs11', { request: req })) as LocalSignResult;
    } catch {
      // Fallback to localhost CA agent if Tauri PKCS#11 command is unavailable
    }
  }

  try {
    const controller = typeof AbortController !== 'undefined' ? new AbortController() : null;
    const timer = controller ? setTimeout(() => controller.abort(), 8000) : null;
    const res = await fetch(localAgentUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        documentId: req.documentId,
        documentHash: req.documentHash,
        providerCode: req.providerCode || 'usb-token-pkcs11',
        algorithm: 'SHA256withRSA',
      }),
      signal: controller?.signal,
    });
    if (timer) clearTimeout(timer);
    if (!res.ok) {
      return {
        success: false,
        provider: req.providerCode || 'usb-token-pkcs11',
        message: `Local Token Agent trả mã HTTP ${res.status}.`,
      };
    }
    const data = (await res.json()) as Partial<LocalSignResult>;
    return {
      success: Boolean(data.success && data.signatureBase64),
      provider: data.provider || req.providerCode || 'usb-token-pkcs11',
      signTransactionId: data.signTransactionId,
      signatureBase64: data.signatureBase64,
      certificateSubject: data.certificateSubject,
      certificateSerial: data.certificateSerial,
      signedAt: data.signedAt || new Date().toISOString(),
      message: data.message || (data.signatureBase64 ? 'Đã ký số thành công bằng USB Token.' : 'Không nhận được chữ ký số.'),
    };
  } catch {
    return {
      success: false,
      provider: req.providerCode || 'usb-token-pkcs11',
      message: 'Chưa kết nối được USB Token Agent cục bộ (Tauri PKCS#11 hoặc dịch vụ ký cổng 18080).',
    };
  }
}
