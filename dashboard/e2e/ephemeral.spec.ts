import fs from "node:fs";
import path from "node:path";
import { expect, test, type BrowserContext, type Page } from "@playwright/test";
import { E2E_TMP } from "../playwright.config";

const HUMAN = "Demo Visitor";
const NOTE = "Public demo: your clicks are kept in a cookie in your browser only; nothing is saved on the server.";
const SIMULATED = "Merge recorded (simulated on the public demo: no git repository here)";
const STATE_COOKIE = "receipts_demo_state";

// One visitor across the serial tests: state lives only in this context's cookies.
test.describe.configure({ mode: "serial" });

let visitor: BrowserContext;
let page: Page;

/** Every file under the e2e data root (except the git fixture) with its content, to prove the server never writes to disk. */
function diskSnapshot(): Record<string, string> {
  const out: Record<string, string> = {};
  const walk = (dir: string) => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) {
        if (e.name !== "mock-repo.git") walk(p);
      } else out[path.relative(E2E_TMP, p)] = fs.readFileSync(p, "utf8");
    }
  };
  walk(E2E_TMP);
  return out;
}

let diskBefore: Record<string, string>;

test.beforeAll(async ({ browser }) => {
  diskBefore = diskSnapshot();
  visitor = await browser.newContext();
  page = await visitor.newPage();
});

test.afterAll(async () => {
  await visitor.close();
});

async function stateCookie(ctx: BrowserContext) {
  return (await ctx.cookies()).find((c) => c.name === STATE_COOKIE);
}

test("one quiet footer line with a reset link, no read-only note, actions enabled", async () => {
  await page.goto("/");
  await page.getByTestId("human-input").fill(HUMAN);
  await page.getByTestId("human-save").click();
  await expect(page.getByTestId("human-display")).toContainText(HUMAN);
  await expect(page.getByTestId("ephemeral-note")).toHaveCount(1);
  await expect(page.getByTestId("ephemeral-note")).toContainText(NOTE);
  await expect(page.getByTestId("demo-reset")).toHaveText("Reset my demo clicks");
  await page.goto("/change/ju26-issue-2283");
  await expect(page.getByTestId("ephemeral-note")).toHaveCount(1);
  await expect(page.getByTestId("readonly-note")).toHaveCount(0);
  await expect(page.getByTestId("sign-off")).toBeEnabled();
});

test("sign-off persists across reloads and separate requests via the cookie", async () => {
  await page.goto("/change/ju26-issue-2283");
  await page.getByTestId("sign-off").click();
  await expect(page.getByTestId("decision-banner")).toContainText(`Signed off by ${HUMAN}`);
  expect(await stateCookie(visitor)).toBeTruthy();
  for (let i = 0; i < 3; i++) {
    await page.reload();
    await expect(page.getByTestId("decision-banner")).toContainText(`Signed off by ${HUMAN}`);
  }
  const md = await page.request.get("/api/receipt-markdown?change=ju26-issue-2283");
  expect(await md.text()).toContain(HUMAN);
  const again = await page.request.post("/api/decisions/sign-off", { data: { change_id: "ju26-issue-2283" } });
  expect(again.status()).toBe(409);
  await page.goto("/");
  await expect(page.getByTestId("queue-card").filter({ hasText: "ju26-issue-2283" })).toContainText(HUMAN);
});

test("another visitor without the cookie sees none of it; the same cookie elsewhere sees all of it", async ({ browser }) => {
  const stranger = await browser.newContext();
  const p2 = await stranger.newPage();
  await p2.goto("/change/ju26-issue-2283");
  await expect(p2.getByTestId("decision-banner")).toHaveCount(0);
  await stranger.close();

  const twin = await browser.newContext();
  await twin.addCookies((await visitor.cookies()).map((c) => ({ ...c })));
  const p3 = await twin.newPage();
  await p3.goto("/change/ju26-issue-2283");
  await expect(p3.getByTestId("decision-banner")).toContainText(`Signed off by ${HUMAN}`);
  await twin.close();
});

test("question answer and context confirm persist across reloads", async () => {
  await page.goto("/change/ju26-issue-2297");
  await page.getByTestId("pending-question").getByTestId("answer-option").filter({ hasText: "All generated types" }).click();
  await expect(page.getByTestId("pending-question")).toHaveCount(0);
  await page.reload();
  await expect(page.getByTestId("pending-question")).toHaveCount(0);
  const dup = await page.request.post("/api/questions/answer", { data: { question_id: "Q-2297-1", change_id: "ju26-issue-2297", answer: "x" } });
  expect(dup.status()).toBe(409);

  await page.goto("/context");
  await expect(page.getByTestId("context-status")).toHaveText("draft");
  await page.getByTestId("confirm-context").click();
  await expect(page.getByTestId("context-status")).toHaveText("confirmed");
  await page.reload();
  await expect(page.getByTestId("context-status")).toHaveText("confirmed");
  await expect(page.getByTestId("context-ledger")).toContainText("All generated types");
  await expect(page.getByTestId("context-ledger")).toContainText(HUMAN);
});

test("merge with no git repo records a simulated decision with no sha, kept across reloads", async () => {
  await page.goto("/decision/ju26-issue-2297");
  await page.getByTestId("merge-A").click();
  await expect(page.getByTestId("merge-result")).toContainText("Merged A into main");
  await expect(page.getByTestId("merge-simulated")).toHaveText(SIMULATED);
  await expect(page.getByTestId("merge-sha")).toHaveCount(0);
  await page.reload();
  await expect(page.getByTestId("merge-simulated")).toHaveText(SIMULATED);

  const again = await page.request.post("/api/decisions/merge", { data: { change_id: "ju26-issue-2297", choice: "B" } });
  expect(again.status()).toBe(409);
  await page.goto("/change/ju26-issue-2297");
  await expect(page.getByTestId("decision-banner").getByTestId("merge-simulated")).toHaveText(SIMULATED);
});

test("merge still refuses without a named human and on a change that is not decision-A-B", async ({ request }) => {
  const anon = await request.post("/api/decisions/merge", { data: { change_id: "ju26-issue-2297", choice: "A" } });
  expect(anon.status()).toBe(400);
  const res = await page.request.post("/api/decisions/merge", { data: { change_id: "sql-bad-down", choice: "A" } });
  expect(res.status()).toBe(409);
  expect((await res.json()).error).toMatch(/not decision-A-B/);
});

test("reset clears every click", async () => {
  await page.goto("/");
  await page.getByTestId("demo-reset").click();
  await expect(page).toHaveURL(/\/$/);
  expect(await stateCookie(visitor)).toBeUndefined();
  await page.goto("/change/ju26-issue-2283");
  await expect(page.getByTestId("decision-banner")).toHaveCount(0);
  await page.goto("/change/ju26-issue-2297");
  await expect(page.getByTestId("pending-question")).toHaveCount(1);
  await page.goto("/context");
  await expect(page.getByTestId("context-status")).toHaveText("draft");
});

test("nothing was written to disk", () => {
  expect(diskSnapshot()).toEqual(diskBefore);
});
