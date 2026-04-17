/**
 * NEW-INFO-13 — Persist saved issue filter sets per project.
 * The filter object is opaque to this module (stored as JSON); callers cast
 * to their preferred shape when reading.
 */
import AsyncStorage from '@react-native-async-storage/async-storage';

const KEY = 'planscape_saved_filters';

export interface SavedFilter<T = unknown> {
  id: string;
  name: string;
  projectId: string;
  filter: T;
  createdAt: string;
}

async function loadAll(): Promise<SavedFilter[]> {
  const raw = await AsyncStorage.getItem(KEY);
  if (!raw) return [];
  try {
    return JSON.parse(raw) as SavedFilter[];
  } catch {
    return [];
  }
}

export async function listSavedFilters(projectId: string): Promise<SavedFilter[]> {
  const all = await loadAll();
  return all.filter(f => f.projectId === projectId);
}

export async function saveFilter<T>(projectId: string, name: string, filter: T): Promise<SavedFilter<T>> {
  const all = await loadAll();
  const entry: SavedFilter<T> = {
    id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    name,
    projectId,
    filter,
    createdAt: new Date().toISOString(),
  };
  all.push(entry);
  await AsyncStorage.setItem(KEY, JSON.stringify(all));
  return entry;
}

export async function deleteSavedFilter(id: string): Promise<void> {
  const all = await loadAll();
  await AsyncStorage.setItem(KEY, JSON.stringify(all.filter(f => f.id !== id)));
}

export async function clearProjectFilters(projectId: string): Promise<void> {
  const all = await loadAll();
  await AsyncStorage.setItem(KEY, JSON.stringify(all.filter(f => f.projectId !== projectId)));
}
