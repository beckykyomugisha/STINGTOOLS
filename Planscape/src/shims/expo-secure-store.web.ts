// Web shim — expo-secure-store uses native Keychain/Keystore APIs unavailable
// in the browser. Fall back to localStorage (acceptable for web dev/preview).
const store: Record<string, string> = {};

export async function setItemAsync(key: string, value: string): Promise<void> {
  try { localStorage.setItem(key, value); } catch { store[key] = value; }
}

export async function getItemAsync(key: string): Promise<string | null> {
  try { return localStorage.getItem(key); } catch { return store[key] ?? null; }
}

export async function deleteItemAsync(key: string): Promise<void> {
  try { localStorage.removeItem(key); } catch { delete store[key]; }
}
