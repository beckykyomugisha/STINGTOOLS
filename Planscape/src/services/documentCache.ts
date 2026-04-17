import * as FileSystem from 'expo-file-system/legacy';
import { secureStorage } from './secureStorage';

const CACHE_DIR = `${FileSystem.cacheDirectory}documents/`;

async function ensureDir(): Promise<void> {
  const info = await FileSystem.getInfoAsync(CACHE_DIR);
  if (!info.exists) {
    await FileSystem.makeDirectoryAsync(CACHE_DIR, { intermediates: true });
  }
}

export const documentCache = {
  async downloadToCache(documentId: string, url: string): Promise<string> {
    await ensureDir();
    const dest = `${CACHE_DIR}${documentId}`;
    const info = await FileSystem.getInfoAsync(dest);
    if (info.exists) return dest;
    const token = await secureStorage.getToken();
    const { uri } = await FileSystem.downloadAsync(url, dest, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
    return uri;
  },

  async clear(): Promise<void> {
    const info = await FileSystem.getInfoAsync(CACHE_DIR);
    if (info.exists) await FileSystem.deleteAsync(CACHE_DIR, { idempotent: true });
  },
};
