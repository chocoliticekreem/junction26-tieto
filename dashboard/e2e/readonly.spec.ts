import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { E2E_TMP } from "../playwright.config";

const HUMAN = "Demo Visitor";
const MESSAGE = "Public demo: decisions are recorded by the named reviewer on their own machine.";
const NOTE = "Demo site — signing happens on the reviewer's machine";

test.describe.configure({ mode: "serial" });

async function signIn(page: Page) {
  await page.goto("/");
  await page.getByTestId("human-input").fill(HUMAN);
  await page.getByTestId("human-save").click();
  await expect(page.getByTestId("human-display")).toContainText(HUMAN);
}

function mainSha(): string {
  return execFileSync("git", ["-C", path.join(E2E_TMP, "mock-repo.git"), "rev-parse", "main"], { encoding: "utf8" }).trim();
}

test("the name picker still works: visitors see who they would sign as", async ({ page }) => {
  await signIn(page);
  const cookies = await page.context().cookies();
  expect(decodeURIComponent(cookies.find((c) => c.name === "receipts_human")?.value ?? "")).toBe(HUMAN);
});

test("every write route refuses with 403 and writes nothing, even with a named human", async ({ page }) => {
  await signIn(page);
  const before = mainSha();
  const ledger = path.join(E2E_TMP, "context", "ledger.jsonl");
  const ledgerBefore = fs.existsSync(ledger) ? fs.readFileSync(ledger, "utf8") : null;
  const calls = [
    page.request.post("/api/decisions/sign-off", { data: { change_id: "ju26-issue-2283" } }),
    page.request.post("/api/decisions/merge", { data: { change_id: "ju26-issue-2297", choice: "A" } }),
    page.request.post("/api/questions/answer", { data: { question_id: "Q-2297-1", change_id: "ju26-issue-2297", answer: "x" } }),
    page.request.post("/api/context/confirm"),
  ];
  for (const res of await Promise.all(calls)) {
    expect(res.status()).toBe(403);
    expect((await res.json()).error).toBe(MESSAGE);
  }
  expect(fs.readdirSync(path.join(E2E_TMP, "decisions")).filter((f) => f.endsWith(".json"))).toEqual([]);
  expect(fs.existsSync(ledger) ? fs.readFileSync(ledger, "utf8") : null).toBe(ledgerBefore);
  expect(JSON.parse(fs.readFileSync(path.join(E2E_TMP, "context", "status.json"), "utf8")).status).toBe("draft");
  expect(mainSha()).toBe(before);
});

test("change page: sign-off visible but disabled, one demo note", async ({ page }) => {
  await signIn(page);
  await page.goto("/change/ju26-issue-2283");
  await expect(page.getByTestId("sign-off")).toBeVisible();
  await expect(page.getByTestId("sign-off")).toBeDisabled();
  await expect(page.getByTestId("sign-off")).toHaveText(`Sign off as ${HUMAN}`);
  await expect(page.getByTestId("readonly-note")).toHaveCount(1);
  await expect(page.getByTestId("readonly-note")).toHaveText(NOTE);
});

test("change page with a pending question: answers disabled, still one demo note", async ({ page }) => {
  await signIn(page);
  await page.goto("/change/ju26-issue-2297");
  await expect(page.getByTestId("pending-question").first()).toBeVisible();
  for (const b of await page.getByTestId("answer-option").all()) await expect(b).toBeDisabled();
  await expect(page.getByTestId("readonly-note")).toHaveCount(1);
});

test("decision page: merge buttons visible but disabled, one demo note", async ({ page }) => {
  await signIn(page);
  await page.goto("/decision/ju26-issue-2297");
  await expect(page.getByTestId("merge-A")).toBeVisible();
  await expect(page.getByTestId("merge-A")).toBeDisabled();
  await expect(page.getByTestId("merge-B")).toBeDisabled();
  await expect(page.getByTestId("readonly-note")).toHaveCount(1);
});

test("context and questions pages: actions disabled, one demo note each", async ({ page }) => {
  await signIn(page);
  await page.goto("/context");
  await expect(page.getByTestId("confirm-context")).toBeDisabled();
  await expect(page.getByTestId("confirm-context")).toHaveText(`Confirm as ${HUMAN}`);
  await expect(page.getByTestId("readonly-note")).toHaveCount(1);
  await page.goto("/questions");
  await expect(page.getByTestId("answer-submit").first()).toBeDisabled();
  await expect(page.getByTestId("readonly-note")).toHaveCount(1);
});

test("read-only pages and routes still serve", async ({ page }) => {
  for (const p of ["/", "/track-record", "/context", "/questions"]) {
    const res = await page.goto(p);
    expect(res?.status()).toBe(200);
  }
  await expect(page.getByTestId("readonly-note")).toHaveCount(1);
  const md = await page.request.get("/api/receipt-markdown?change=ju26-issue-2283");
  expect(md.status()).toBe(200);
  await page.goto("/");
  await expect(page.getByTestId("readonly-note")).toHaveCount(0);
});
