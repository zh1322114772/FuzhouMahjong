import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e',
  timeout: 180_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:5271',
    locale: 'zh-CN',
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure'
  },
  webServer: {
    command: 'dotnet run --project src/ProjectMahjong.Server -c Release --no-build --urls http://127.0.0.1:5271',
    url: 'http://127.0.0.1:5271',
    env: {
      Kestrel__Endpoints__Http__Url: 'http://127.0.0.1:5271'
    },
    reuseExistingServer: true,
    timeout: 120_000
  }
});
