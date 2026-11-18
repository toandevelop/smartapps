import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e/specs',
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  failOnFlakyTests: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: 2,
  timeout: 90_000,
  globalTimeout: 10 * 60_000,
  expect: { timeout: 10_000 },
  outputDir: '../artifacts/e2e/test-results',
  reporter: [
    ['list'],
    ['html', { outputFolder: '../artifacts/e2e/report', open: 'never' }],
    ['junit', { outputFile: '../artifacts/e2e/results.xml' }],
  ],
  use: {
    baseURL: 'http://127.0.0.1:5510',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
    // Keep first-failure screenshots/traces; encode video only for a CI retry.
    video: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'node e2e/start-backend.mjs',
      url: 'http://127.0.0.1:5520/health',
      reuseExistingServer: false,
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: 'node e2e/start-frontend.mjs',
      url: 'http://127.0.0.1:5510/login',
      reuseExistingServer: false,
      // Includes production compilation and TypeScript on slower CI/local machines.
      timeout: 300_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
  ],
});
