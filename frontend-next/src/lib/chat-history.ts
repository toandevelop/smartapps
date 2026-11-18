import type { ChatMessage } from './types';

// Keep PostgreSQL's sub-millisecond precision; Date.parse alone can reorder tied messages.
function timeKey(value: string): string {
  return value.replace(/(?:\.(\d+))?Z$/, (_, fraction: string | undefined) => `.${(fraction || '').padEnd(7, '0')}Z`);
}

export function mergeChatHistory(previous: ChatMessage[], incoming: ChatMessage[], replaceLatest = false): ChatMessage[] {
  if (replaceLatest && incoming.length === 0) return [];
  const firstIncoming = incoming[0];
  const compare = (a: ChatMessage, b: ChatMessage) => {
    const aTime = timeKey(a.createdAt);
    const bTime = timeKey(b.createdAt);
    return aTime < bTime ? -1 : aTime > bTime ? 1 : a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
  };
  // Refreshing the latest window also removes deleted messages in that window.
  const kept = replaceLatest && firstIncoming ? previous.filter((m) => compare(m, firstIncoming) < 0) : previous;
  const merged = new Map(kept.map((m) => [m.id, m]));
  for (const message of incoming) merged.set(message.id, message);
  return Array.from(merged.values()).sort(compare);
}
