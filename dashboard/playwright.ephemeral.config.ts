import { defineConfig } from "@playwright/test";
import path from "node:path";
import base, { E2E_TMP } from "./playwright.config";

// The public demo on a serverless host: no git repo, and a visitor's clicks live in their own cookie, never on disk.
const PORT = 3113;

export default defineConfig({
  ...base,
  testMatch: /ephemeral\.spec\.ts/,
  testIgnore: undefined,
  use: { ...base.use, baseURL: `http://localhost:${PORT}` },
  webServer: {
    ...(base.webServer as object),
    command: `npm run start -- -p ${PORT}`,
    url: `http://localhost:${PORT}/`,
    env: {
      ...(base.webServer as { env: Record<string, string> }).env,
      RECEIPTS_EPHEMERAL: "1",
      RECEIPTS_MOCK_REPO: path.join(E2E_TMP, "no-mock-repo"),
    },
  },
});
