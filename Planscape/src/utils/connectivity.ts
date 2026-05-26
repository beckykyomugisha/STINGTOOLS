import { syncQueue } from './offlineQueue';

/**
 * N-G17 connectivity bridge: reconnects the offline queue to the network state.
 *
 * Uses @react-native-community/netinfo if present (checked lazily via require
 * so non-mobile test environments still import this module). When the app
 * regains connectivity, the offline queue is drained automatically — the user
 * doesn't need to pull-to-refresh or tap a sync button.
 *
 * A debounce prevents storm-draining on flaky networks that flap between
 * states multiple times per second.
 */

type Unsubscribe = () => void;
type NetInfoState = { isConnected?: boolean | null; isInternetReachable?: boolean | null };

let _unsubscribe: Unsubscribe | null = null;
let _lastDrainAt = 0;
const DEBOUNCE_MS = 5_000;

function tryRequireNetInfo(): any | null {
  try {
    // Dynamic require so web/test builds without NetInfo still compile.
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    return require('@react-native-community/netinfo');
  } catch {
    return null;
  }
}

/**
 * Start listening for connectivity changes. Safe to call multiple times —
 * subsequent calls are ignored while a listener is already active.
 * Returns a disposer you can call from a screen unmount hook.
 */
export function startConnectivityListener(): Unsubscribe {
  if (_unsubscribe) return _unsubscribe;
  const NetInfo = tryRequireNetInfo();
  if (!NetInfo) {
    // No NetInfo — silently no-op. The queue still drains on manual sync.
    _unsubscribe = () => {};
    return _unsubscribe;
  }

  let wasOffline = false;
  _unsubscribe = NetInfo.default.addEventListener((state: NetInfoState) => {
    const online = !!state.isConnected && state.isInternetReachable !== false;
    if (!online) { wasOffline = true; return; }
    if (!wasOffline) return;
    wasOffline = false;
    const now = Date.now();
    if (now - _lastDrainAt < DEBOUNCE_MS) return;
    _lastDrainAt = now;
    // Fire-and-forget — the syncQueue broadcast will update any subscribed UI.
    syncQueue().catch((err) => { console.warn('[OfflineQueue] Drain failed:', err); });
  });
  return _unsubscribe ?? (() => {});
}

export function stopConnectivityListener(): void {
  if (_unsubscribe) { _unsubscribe(); _unsubscribe = null; }
}

/** Returns true if NetInfo reports an active internet-reachable connection. */
export async function isOnline(): Promise<boolean> {
  const NetInfo = tryRequireNetInfo();
  if (!NetInfo) return true; // Optimistic — assume online when we can't tell.
  try {
    const state: NetInfoState = await NetInfo.default.fetch();
    return !!state.isConnected && state.isInternetReachable !== false;
  } catch {
    return true;
  }
}
