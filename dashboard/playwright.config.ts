import { defineConfig, devices } from "@playwright/test";
import path from "node:path";

// Global setup copies e2e/fixtures here and creates the bare mock repo; the server reads from it.
export const E2E_TMP = path.resolve(__dirname, "e2e/.tmp");
const PORT = 3111;

export default defineConfig({
  testDir: "./e2e",
  testMatch: /.*\.spec\.ts/,
  // Each needs its own server: playwright.readonly.config.ts (RECEIPTS_READONLY=1), playwright.ephemeral.config.ts (RECEIPTS_EPHEMERAL=1).
  testIgnore: /(readonly|ephemeral)\.spec\.ts/,
  globalSetup: "./e2e/global-setup.ts",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 60_000,
  reporter: [["list"]],
  use: {
    baseURL: `http://localhost:${PORT}`,
    trace: "retain-on-failure",
    ...devices["Desktop Chrome"],
  },
  webServer: {
    command: `npm run start -- -p ${PORT}`,
    url: `http://localhost:${PORT}/`,
    reuseExistingServer: false,
    timeout: 120_000,
    env: {
      RECEIPTS_RUNS: path.join(E2E_TMP, "runs"),
      RECEIPTS_CONTEXT: path.join(E2E_TMP, "context"),
      RECEIPTS_DECISIONS: path.join(E2E_TMP, "decisions"),
      RECEIPTS_METRICS: path.join(E2E_TMP, "metrics"),
      RECEIPTS_MOCK_REPO: path.join(E2E_TMP, "mock-repo.git"),
    },
  },
});
