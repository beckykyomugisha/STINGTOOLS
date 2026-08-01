import { defineConfig, devices } from '@playwright/test';

/**
 * E2E lives in e2e/ and is kept OUT of the vitest run (vitest owns lib/ and
 * components/). These specs need a running app and a real token, so they are
 * not part of the default `npm test` gate — run them explicitly.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: process.env.TEST_BASE ?? 'http://localhost:3000',
    trace: 'off',
    video: 'off',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
