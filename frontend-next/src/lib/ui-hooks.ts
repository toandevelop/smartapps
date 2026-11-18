import { useEffect, useSyncExternalStore } from 'react';
import { isTauriDesktop } from './tauri-bridge';

/**
 * Invokes `onClose` whenever the user presses the Escape key while `active` is true.
 */
export function useEscapeKey(active: boolean, onClose: () => void) {
  useEffect(() => {
    if (!active) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [active, onClose]);
}

const emptySubscribe = () => () => {};

/**
 * SSR-safe hook returning whether the UI is running inside Tauri Desktop.
 */
export function useIsTauriDesktop(): boolean {
  return useSyncExternalStore(emptySubscribe, isTauriDesktop, () => false);
}

/**
 * SSR-safe hook returning `window.location.origin` on the client.
 */
export function useClientOrigin(fallback = 'http://localhost:4100'): string {
  return useSyncExternalStore(
    emptySubscribe,
    () => (typeof window !== 'undefined' ? window.location.origin : fallback),
    () => fallback
  );
}

function subscribeSidebarCollapsed(cb: () => void) {
  window.addEventListener('smartapps_sidebar_toggle', cb);
  window.addEventListener('storage', cb);
  return () => {
    window.removeEventListener('smartapps_sidebar_toggle', cb);
    window.removeEventListener('storage', cb);
  };
}

function getSidebarCollapsedSnapshot(): boolean {
  return typeof window !== 'undefined' && localStorage.getItem('smartapps_sidebar_collapsed') === 'true';
}

/**
 * Shared SSR-safe subscription to the collapsed state of the main navigation sidebar.
 */
export function useSidebarCollapsed(): boolean {
  return useSyncExternalStore(subscribeSidebarCollapsed, getSidebarCollapsedSnapshot, () => false);
}
