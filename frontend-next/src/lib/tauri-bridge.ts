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
  payload: any;
  createdAt: string;
}

const LOCAL_QUEUE_KEY = 'smartapps_offline_queue_v1';

function getTauriInvoke(): ((cmd: string, args?: Record<string, any>) => Promise<any>) | null {
  if (typeof window === 'undefined') return null;
  const w = window as any;
  return w.__TAURI__?.core?.invoke || w.__TAURI_INTERNALS__?.invoke || null;
}

export function isTauriDesktop(): boolean {
  return Boolean(getTauriInvoke());
}

export async function getDesktopInfo(): Promise<DesktopInfo | null> {
  const invoke = getTauriInvoke();
  if (!invoke) return null;
  try {
    return await invoke('get_desktop_info');
  } catch {
    return null;
  }
}

export async function getDesktopServerConfig(): Promise<DesktopServerConfig> {
  const invoke = getTauriInvoke();
  if (invoke) {
    try {
      return await invoke('get_server_config');
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
  return invoke('set_server_config', { webUrl, apiUrl });
}

export async function openAppDesktopWindow(appCode: string, appName: string): Promise<string | null> {
  const invoke = getTauriInvoke();
  if (!invoke) return null;
  return invoke('open_app_window', {
    appCode,
    appName,
    baseUrl: typeof window !== 'undefined' ? window.location.origin : 'http://localhost:4100',
  });
}

export async function saveOfflineSnapshot(appCode: string, payload: unknown): Promise<string | null> {
  const invoke = getTauriInvoke();
  if (!invoke) return null;
  return invoke('save_offline_snapshot', {
    appCode,
    jsonPayload: JSON.stringify(payload, null, 2),
  });
}

export async function exportDesktopReport(filename: string, content: string): Promise<string | null> {
  const invoke = getTauriInvoke();
  if (!invoke) return null;
  return invoke('export_report_file', { filename, content });
}

export async function queueOfflineMutation(item: OfflineMutationItem): Promise<OfflineMutationItem[]> {
  const invoke = getTauriInvoke();
  if (invoke) {
    try {
      return await invoke('queue_offline_mutation', { item });
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
      return await invoke('list_offline_queue');
    } catch {
      // fall through
    }
  }
  if (typeof window === 'undefined') return [];
  try {
    return JSON.parse(localStorage.getItem(LOCAL_QUEUE_KEY) || '[]');
  } catch {
    return [];
  }
}

export async function removeOfflineMutation(id: string): Promise<OfflineMutationItem[]> {
  const invoke = getTauriInvoke();
  if (invoke) {
    try {
      return await invoke('remove_offline_mutation', { id });
    } catch {
      // fall through
    }
  }
  const existing = await listOfflineQueue();
  const next = existing.filter((x) => x.id !== id);
  if (typeof window !== 'undefined') localStorage.setItem(LOCAL_QUEUE_KEY, JSON.stringify(next));
  return next;
}

export async function syncAllOfflineMutations(): Promise<{ synced: number; failed: number }> {
  const items = await listOfflineQueue();
  let synced = 0;
  let failed = 0;
  for (const item of items) {
    try {
      const res = await fetch(item.endpoint, {
        method: item.method || 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(item.payload),
      });
      if (res.ok) {
        await removeOfflineMutation(item.id);
        synced++;
      } else {
        failed++;
      }
    } catch {
      failed++;
    }
  }
  return { synced, failed };
}
