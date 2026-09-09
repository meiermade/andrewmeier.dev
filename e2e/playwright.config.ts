import { defineConfig, devices } from '@playwright/test'

const baseURL = process.env.SITE_E2E_BASE_URL ?? 'http://127.0.0.1:5051'
const analyticsDisabled = process.env.E2E_ANALYTICS_ENABLED === 'false'
const suite = analyticsDisabled ? 'analytics-disabled' : 'analytics-enabled'

export default defineConfig({
  testDir: './tests',
  testMatch: analyticsDisabled ? 'analytics-disabled.spec.ts' : '**/*.spec.ts',
  testIgnore: analyticsDisabled ? [] : ['**/analytics-disabled.spec.ts'],
  outputDir: `test-results/${suite}`,
  timeout: 30_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never', outputFolder: `playwright-report/${suite}` }]] : 'list',
  use: {
    baseURL,
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'firefox', use: { ...devices['Desktop Firefox'] } }],
})
