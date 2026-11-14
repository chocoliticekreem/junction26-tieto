import fs from "node:fs";
import path from "node:path";
import { test, type Page } from "@playwright/test";
import { E2E_TMP } from "../playwright.config";

// Runs last (alphabetical after dashboard.spec.ts) so the pages show decisions and answers.
const OUT = path.resolve(__dirname, "screenshots");
const pages = [
  ["inbox", "/"],
  ["change-2283", "/change/ju26-issue-2283"],
  ["change-2297", "/change/ju26-issue-2297"],
  ["change-bench-logic", "/change/mock%2F2283%2Flogic"],
  ["decision-2297", "/decision/ju26-issue-2297"],
  ["questions", "/questions"],
  ["context", "/context"],
  ["track-record", "/track-record"],
] as const;

async function shoot(page: Page, name: string, url: string, width: number) {
  if (name === "questions") {
    // Restore the fixture ledger so the shot shows a pending question, not the answered state left by dashboard.spec.ts.
    fs.copyFileSync(path.resolve(__dirname, "fixtures/context/ledger.jsonl"), path.join(E2E_TMP, "context/ledger.jsonl"));
  }
  await page.setViewportSize({ width, height: 900 });
  await page.goto(url);
  await page.waitForLoadState("networkidle");
  if (name === "change-2283") {
    await page.getByTestId("hunches-toggle").click();
    await page.getByTestId("log-toggle").filter({ hasText: "test-base-req.log" }).click();
    await page.getByTestId("log-excerpt").waitFor();
  }
  await page.screenshot({ path: path.join(OUT, `${name}-${width}.png`), fullPage: true });
}

test.beforeAll(() => fs.mkdirSync(OUT, { recursive: true }));

for (const [name, url] of pages) {
  test(`screenshot ${name}`, async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("human-input").fill("Anson Test");
    await page.getByTestId("human-save").click();
    await page.getByTestId("human-display").waitFor();
    await shoot(page, name, url, 1440);
    await shoot(page, name, url, 390);
  });
}
