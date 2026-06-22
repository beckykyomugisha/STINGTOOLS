import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  test: {
    environment: 'jsdom',
    // Pretend we're already on /login so api()'s 401 redirect is a no-op
    // (it guards on pathname !== '/login') — keeps test output clean.
    environmentOptions: { jsdom: { url: 'http://localhost/login' } },
    globals: true,
    include: ['lib/**/*.test.ts', 'lib/**/*.test.tsx'],
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('.', import.meta.url)),
    },
  },
});
