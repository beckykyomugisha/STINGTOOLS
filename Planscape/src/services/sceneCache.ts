// S5.4 — on-device cache for scene chunks. Keyed by content hash so
// chunks shared across project versions reuse one file on disk. Uses
// expo-file-system rather than IndexedDB because we need binary
// streaming for GLBs; AsyncStorage holds the metadata.
//
// Storage layout:
//   ${cacheDir}/scenes/<sha256-prefix>/<sha256>.glb   — chunk bytes
//   AsyncStorage key 'planscape_scene_cache'         — { hash → meta }
//
// Eviction policy: LRU with a 500 MB cap. When usage exceeds the
// cap, drop oldest-touched entries until we're under 80% of cap.
//
// Starred-project preload: starredProjectIds are pre-fetched the next
// time the app sees a network connection so morning-of-site visit is
// instant. Triggered from the dashboard tile.

import * as FileSystem from 'expo-file-system/legacy';
import AsyncStorage from '@react-native-async-storage/async-storage';

const META_KEY  = 'planscape_scene_cache';
const STAR_KEY  = 'planscape_starred_projects';
const CACHE_DIR = (FileSystem.cacheDirectory ?? '') + 'planscape-scenes/';
const CAP_BYTES = 500 * 1024 * 1024; // 500 MB

interface CacheMeta {
  hash: string;
  url: string;
  sizeBytes: number;
  lastUsedAt: number;
  projectId: string;
}

interface CacheState {
  [hash: string]: CacheMeta;
}

async function loadMeta(): Promise<CacheState> {
  const raw = await AsyncStorage.getItem(META_KEY);
  if (!raw) return {};
  try { return JSON.parse(raw); } catch { return {}; }
}

async function saveMeta(state: CacheState): Promise<void> {
  await AsyncStorage.setItem(META_KEY, JSON.stringify(state));
}

function pathFor(hash: string): string {
  return `${CACHE_DIR}${hash.slice(0, 2)}/${hash}.glb`;
}

async function ensureDir(hash: string): Promise<void> {
  const dir = `${CACHE_DIR}${hash.slice(0, 2)}/`;
  const info = await FileSystem.getInfoAsync(dir);
  if (!info.exists) await FileSystem.makeDirectoryAsync(dir, { intermediates: true });
}

/**
 * Resolve a chunk URL — return a local file:// URI if cached, or fetch
 * + cache + return the local URI. The mobile viewer always loads from
 * the local URI so re-opening a project is instant offline.
 */
export async function getChunkLocal(projectId: string, hash: string, url: string): Promise<string> {
  const local = pathFor(hash);
  const info  = await FileSystem.getInfoAsync(local);
  const meta  = await loadMeta();
  if (info.exists) {
    meta[hash] = { ...meta[hash], lastUsedAt: Date.now(), projectId };
    await saveMeta(meta);
    return local;
  }
  await ensureDir(hash);
  const dl = await FileSystem.downloadAsync(url, local);
  meta[hash] = { hash, url, sizeBytes: dl.headers['content-length'] ? Number(dl.headers['content-length']) : 0, lastUsedAt: Date.now(), projectId };
  await saveMeta(meta);
  await maybeEvict();
  return local;
}

/** Star a project so its chunks pre-fetch in the background. */
export async function starProject(projectId: string): Promise<void> {
  const list = JSON.parse((await AsyncStorage.getItem(STAR_KEY)) ?? '[]') as string[];
  if (!list.includes(projectId)) list.push(projectId);
  await AsyncStorage.setItem(STAR_KEY, JSON.stringify(list));
}

export async function unstarProject(projectId: string): Promise<void> {
  const list = JSON.parse((await AsyncStorage.getItem(STAR_KEY)) ?? '[]') as string[];
  await AsyncStorage.setItem(STAR_KEY, JSON.stringify(list.filter((p) => p !== projectId)));
}

export async function listStarredProjects(): Promise<string[]> {
  return JSON.parse((await AsyncStorage.getItem(STAR_KEY)) ?? '[]') as string[];
}

/** Drop the LRU until cache is under 80% of cap. */
async function maybeEvict(): Promise<void> {
  const meta = await loadMeta();
  let total = Object.values(meta).reduce((s, m) => s + (m.sizeBytes || 0), 0);
  if (total < CAP_BYTES) return;
  const target = CAP_BYTES * 0.8;
  const oldestFirst = Object.values(meta).sort((a, b) => a.lastUsedAt - b.lastUsedAt);
  for (const m of oldestFirst) {
    if (total < target) break;
    try { await FileSystem.deleteAsync(pathFor(m.hash), { idempotent: true }); } catch { /* ignore */ }
    delete meta[m.hash];
    total -= m.sizeBytes || 0;
  }
  await saveMeta(meta);
}

export async function cacheStats(): Promise<{ entries: number; bytes: number; capBytes: number }> {
  const meta = await loadMeta();
  const entries = Object.keys(meta).length;
  const bytes   = Object.values(meta).reduce((s, m) => s + (m.sizeBytes || 0), 0);
  return { entries, bytes, capBytes: CAP_BYTES };
}

export async function clearCache(): Promise<void> {
  try { await FileSystem.deleteAsync(CACHE_DIR, { idempotent: true }); } catch { /* ignore */ }
  await AsyncStorage.removeItem(META_KEY);
}
