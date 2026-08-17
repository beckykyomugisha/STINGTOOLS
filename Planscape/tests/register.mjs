// Node module-resolution hooks so the offline-queue harness can load the REAL
// TypeScript sources (src/utils/offlineQueue.ts, src/api/client.ts) outside
// React Native.
//
// Two jobs:
//   1. Resolve the `@/*` -> `src/*` tsconfig path alias, which Node does not
//      know about.
//   2. Redirect the handful of native-only modules (SecureStore, AsyncStorage,
//      expo-file-system) and the endpoint layer to in-memory stubs.
//
// Everything else — including offlineQueue's classification logic and the
// ApiError class under test — is loaded from real source.

import { registerHooks } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(here, '..');
const stub = (f) => pathToFileURL(path.join(here, 'stubs', f)).href;

const REDIRECTS = new Map([
  ['@react-native-async-storage/async-storage', stub('async-storage.mjs')],
  ['expo-secure-store', stub('expo-secure-store.mjs')],
  ['expo-file-system/legacy', stub('expo-file-system-legacy.mjs')],
  ['@/api/endpoints', stub('endpoints.mjs')],
]);

registerHooks({
  resolve(specifier, context, nextResolve) {
    const redirect = REDIRECTS.get(specifier);
    if (redirect) return { url: redirect, shortCircuit: true };

    // `../i18n` / `@/i18n` — imported by client.ts for the X-Language header.
    if (specifier === '../i18n' || specifier === '@/i18n') {
      return { url: stub('i18n.mjs'), shortCircuit: true };
    }

    // tsconfig path alias: @/foo -> <root>/src/foo(.ts)
    if (specifier.startsWith('@/')) {
      const rel = specifier.slice(2);
      const candidate = path.join(projectRoot, 'src', rel);
      for (const ext of ['.ts', '.tsx', '/index.ts', '']) {
        const full = candidate + ext;
        try {
          return { url: pathToFileURL(full).href, shortCircuit: true };
        } catch { /* try next extension */ }
      }
    }

    return nextResolve(specifier, context);
  },
});
