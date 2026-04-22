import AsyncStorage from '@react-native-async-storage/async-storage';

/**
 * N-G17 read-through cache: stale-while-revalidate wrapper around fetchers.
 *
 *  1. If a fresh entry exists (within TTL) → return it, no network call.
 *  2. If a stale entry exists → return stale immediately, fire off a background
 *     refresh, and emit the new value via the subscriber hook.
 *  3. If no entry exists → await the fetcher, store result, return.
 *
 * Critically: when the network call fails (offline), we still return the
 * cached value (even if stale), so every screen — issues, documents,
 * compliance, meetings — keeps working with no connectivity.
 */

const CACHE_KEY_PREFIX = 'planscape_cache::';

interface CacheEntry<T> {
  value: T;
  storedAt: string;     // ISO
  etag?: string;
}

type Listener = (key: string) => void;
const _listeners = new Set<Listener>();

/** Subscribe to cache updates (background refresh completion). */
export function onCacheUpdate(l: Listener): () => void {
  _listeners.add(l);
  return () => { _listeners.delete(l); };
}
function emit(key: string) {
  for (const l of Array.from(_listeners)) { try { l(key); } catch { /* ignore */ } }
}

function storageKey(key: string): string { return CACHE_KEY_PREFIX + key; }

export async function readCached<T>(key: string): Promise<CacheEntry<T> | null> {
  const raw = await AsyncStorage.getItem(storageKey(key));
  if (!raw) return null;
  try { return JSON.parse(raw) as CacheEntry<T>; } catch { return null; }
}

export async function writeCached<T>(key: string, value: T, etag?: string): Promise<void> {
  const entry: CacheEntry<T> = {
    value,
    storedAt: new Date().toISOString(),
    etag,
  };
  await AsyncStorage.setItem(storageKey(key), JSON.stringify(entry));
}

export async function invalidate(key: string): Promise<void> {
  await AsyncStorage.removeItem(storageKey(key));
}

/** Invalidate every cache entry matching a prefix (e.g. "issues::proj1"). */
export async function invalidatePrefix(prefix: string): Promise<void> {
  const keys = await AsyncStorage.getAllKeys();
  const full = CACHE_KEY_PREFIX + prefix;
  const matches = keys.filter((k) => k.startsWith(full));
  if (matches.length > 0) await AsyncStorage.multiRemove(matches);
}

export interface FetchOptions<T> {
  /** Cache TTL in seconds before the entry is treated as stale. */
  ttlSec: number;
  /** Fetcher for the live value. Throws on network/server error. */
  fetcher: () => Promise<T>;
  /**
   * When true (default) and a stale entry exists, return it immediately and
   * refresh in the background. When false, always await the fetcher if stale.
   */
  staleWhileRevalidate?: boolean;
}

function isFresh(entry: CacheEntry<unknown>, ttlSec: number): boolean {
  const storedAt = Date.parse(entry.storedAt);
  if (isNaN(storedAt)) return false;
  return (Date.now() - storedAt) < ttlSec * 1000;
}

/**
 * The main wrapper. Use like:
 *   const issues = await cached('issues::proj1', { ttlSec: 300, fetcher: () => listIssues('proj1') });
 */
export async function cached<T>(key: string, opts: FetchOptions<T>): Promise<T> {
  const entry = await readCached<T>(key);
  const fresh = entry && isFresh(entry, opts.ttlSec);

  // Fresh — return immediately, no network.
  if (fresh) return entry!.value;

  // Stale + SWR — return stale, refresh in background.
  if (entry && (opts.staleWhileRevalidate ?? true)) {
    // Fire-and-forget background refresh. Failures keep the stale entry.
    opts.fetcher()
      .then((v) => writeCached(key, v).then(() => emit(key)))
      .catch(() => { /* offline — keep stale, UI already has it */ });
    return entry.value;
  }

  // No cached entry, or SWR disabled with a stale entry → await.
  try {
    const value = await opts.fetcher();
    await writeCached(key, value);
    return value;
  } catch (err) {
    // Last-resort: if we do have a stale value, return it rather than throwing.
    if (entry) return entry.value;
    throw err;
  }
}

/** Returns true if the cache has a value for the key (fresh or stale). */
export async function has(key: string): Promise<boolean> {
  const entry = await readCached(key);
  return entry != null;
}
