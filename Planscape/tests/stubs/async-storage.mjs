// Test stub for @react-native-async-storage/async-storage — in-memory.
const store = new Map();
const AsyncStorage = {
  async getItem(k) { return store.has(k) ? store.get(k) : null; },
  async setItem(k, v) { store.set(k, String(v)); },
  async removeItem(k) { store.delete(k); },
  async clear() { store.clear(); },
};
export default AsyncStorage;
