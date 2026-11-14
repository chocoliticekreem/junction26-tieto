import { defineConfig } from "@playwright/test";
import base from "./playwright.config";

// Same build, fixtures and global setup as the main suite; the server runs as the public read-only demo.
const PORT = 3112;

export default defineConfig({
  ...base,
  testMatch: /readonly\.spec\.ts/,
  testIgnore: undefined,
  use: { ...base.use, baseURL: `http://localhost:${PORT}` },
  webServer: {
    ...(base.webServer as object),
    command: `npm run start -- -p ${PORT}`,
    url: `http://localhost:${PORT}/`,
    env: { ...(base.webServer as { env: Record<string, string> }).env, RECEIPTS_READONLY: "1" },
  },
});
