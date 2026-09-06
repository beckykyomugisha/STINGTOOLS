// Test stub for expo-file-system/legacy — no filesystem in the harness.
export async function getInfoAsync() { return { exists: true, size: 1 }; }
export async function deleteAsync() { /* no-op */ }
export async function copyAsync() { /* no-op */ }
export async function moveAsync() { /* no-op */ }
export async function makeDirectoryAsync() { /* no-op */ }
export async function readAsStringAsync() { return ''; }
export async function writeAsStringAsync() { /* no-op */ }
export const documentDirectory = '/tmp/';
export const cacheDirectory = '/tmp/';
