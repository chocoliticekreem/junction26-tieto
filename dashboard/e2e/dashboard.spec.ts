import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { E2E_TMP } from "../playwright.config";

const HUMAN = "Anson Test";

async function signIn(page: Page) {
  await page.goto("/");
  await page.getByTestId("human-input").fill(HUMAN);
  await page.getByTestId("human-save").click();
  await expect(page.getByTestId("human-display")).toContainText(HUMAN);
}

test.describe.configure({ mode: "serial" });

test("inbox lists sample and bench changes with verdict pills", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Inbox" })).toBeVisible();
  const rows = page.getByTestId("inbox-row");
  await expect(rows).toHaveCount(5);
  await expect(page.getByRole("heading", { name: /Tieto sample changes/ })).toBeVisible();
  await expect(page.getByRole("heading", { name: /^Bench/ })).toBeVisible();
  await expect(page.getByText("ju26-issue-2297__A")).toHaveCount(0);
  const row2283 = rows.filter({ hasText: "ju26-issue-2283" });
  await expect(row2283.getByTestId("verdict-pill")).toHaveText("Needs sign-off");
  await expect(row2283.getByTestId("receipt-counts")).toContainText("5 green");
  await expect(row2283.getByTestId("receipt-counts")).toContainText("0 red");
  const row2297 = rows.filter({ hasText: "ju26-issue-2297" });
  await expect(row2297.getByTestId("verdict-pill")).toHaveText("Decision A/B");
  await expect(row2297).toContainText("1 pending");
  await expect(rows.filter({ hasText: "mock/2283/logic" }).getByTestId("verdict-pill")).toHaveText("Blocked");
});

test("change page shows receipts, collapsed hunches, not-checked list and log excerpt", async ({ page }) => {
  await page.goto("/change/ju26-issue-2283");
  await expect(page.getByRole("heading", { name: /Fix IndexOutOfRangeException/ })).toBeVisible();
  await expect(page.getByTestId("verdict-reason")).toContainText("surviving mutant");

  await expect(page.getByTestId("finding")).toHaveCount(1);
  await expect(page.getByTestId("finding").getByTestId("evidence-card")).toHaveCount(1);
  await expect(page.getByTestId("finding").getByTestId("mutant-table")).toContainText("survived");

  // Hunches collapsed by default, visually labelled when opened.
  await expect(page.getByTestId("hunches-toggle")).toContainText("2 unverified hunches withheld");
  await expect(page.getByTestId("hunch")).toHaveCount(0);
  await page.getByTestId("hunches-toggle").click();
  await expect(page.getByTestId("hunch")).toHaveCount(2);
  await expect(page.getByTestId("hunch").first()).toContainText("unverified hunch — no receipt");

  await expect(page.getByTestId("not-checked-list").locator("li")).toHaveCount(3);
  await expect(page.getByTestId("not-checked-list")).toContainText("ExecutionTests");
  await expect(page.getByTestId("residual-risk")).toContainText("surviving mutant");

  // Routing map and fail-before/pass-after table.
  await expect(page.getByTestId("routing-tree")).toContainText("5 files");
  const reqCard = page.locator("#evidence-E-req");
  await expect(reqCard).toContainText("Requirement tests fail on base, pass on head");
  await expect(reqCard.getByRole("cell", { name: "failed" })).toHaveCount(2);
  await expect(reqCard.getByRole("cell", { name: "passed" })).toHaveCount(2);

  // Log excerpt via the API route.
  const toggle = page.getByTestId("log-toggle").filter({ hasText: "test-base-req.log" });
  await toggle.click();
  await expect(page.getByTestId("log-excerpt")).toContainText("IndexOutOfRangeException");
});

test("2297 routing map collapses 222 generated files into one block", async ({ page }) => {
  await page.goto("/change/ju26-issue-2297");
  const tree = page.getByTestId("routing-tree");
  await expect(tree).toContainText("237 files");
  await expect(tree).toContainText("222");
  await expect(tree.getByText("222 files")).toBeVisible();
  // Collapsed: individual snapshot files are not rendered until expanded.
  await expect(tree.getByText("Generation001.verified.cs")).toHaveCount(0);
  await expect(page.getByText("222 generated files routed to regeneration check; 15 reviewed")).toBeVisible();
  await expect(page.getByTestId("refused-list")).toContainText("Pick the scope");
  await expect(page.getByTestId("go-decision")).toBeVisible();
});

test("writes are refused without a named human", async ({ request }) => {
  const res = await request.post("/api/decisions/sign-off", { data: { change_id: "ju26-issue-2283" } });
  expect(res.status()).toBe(400);
  const q = await request.post("/api/questions/answer", { data: { question_id: "Q-2297-1", change_id: "ju26-issue-2297", answer: "x" } });
  expect(q.status()).toBe(400);
  const m = await request.post("/api/decisions/merge", { data: { change_id: "ju26-issue-2297", choice: "A" } });
  expect(m.status()).toBe(400);
});

test("answering a question appends to ledger.jsonl and removes it from pending", async ({ page }) => {
  await signIn(page);
  await page.goto("/questions");
  const pending = page.getByTestId("pending-question");
  await expect(pending).toHaveCount(1);
  await expect(pending).toContainText("Q-2297-1");
  await pending.getByTestId("answer-option").filter({ hasText: "All generated types" }).click();
  await expect(page.getByTestId("pending-question")).toHaveCount(0);
  await expect(page.getByTestId("ledger-table")).toContainText("All generated types");
  await expect(page.getByTestId("ledger-table")).toContainText(HUMAN);

  const lines = fs
    .readFileSync(path.join(E2E_TMP, "context", "ledger.jsonl"), "utf8")
    .split("\n")
    .filter(Boolean)
    .map((l) => JSON.parse(l));
  expect(lines).toHaveLength(2);
  const last = lines[1];
  expect(last).toMatchObject({ question_id: "Q-2297-1", change_id: "ju26-issue-2297", area: "source-generator/emit", answer: "All generated types", answered_by: HUMAN });
  expect(typeof last.ts).toBe("string");
  expect(typeof last.question).toBe("string");

  // Inbox no longer shows it pending.
  await page.goto("/");
  await expect(page.getByTestId("inbox-row").filter({ hasText: "ju26-issue-2297" })).not.toContainText("pending");
});

test("Merge A creates a --no-ff commit in the bare repo authored by the human and writes the decision", async ({ page }) => {
  await signIn(page);
  await page.goto("/decision/ju26-issue-2297");
  await expect(page.getByTestId("side-A")).toContainText("review/ju26-issue-2297/A");
  await expect(page.getByTestId("side-B")).toContainText("169/169");
  await page.getByTestId("merge-A").click();
  await expect(page.getByTestId("merge-result")).toContainText("Merged A into main");
  const sha = (await page.getByTestId("merge-sha").textContent())!.trim();
  expect(sha).toMatch(/^[0-9a-f]{40}$/);

  const repo = path.join(E2E_TMP, "mock-repo.git");
  const head = execFileSync("git", ["-C", repo, "rev-parse", "main"], { encoding: "utf8" }).trim();
  expect(head).toBe(sha);
  const parents = execFileSync("git", ["-C", repo, "log", "-1", "--format=%P", "main"], { encoding: "utf8" }).trim().split(" ");
  expect(parents).toHaveLength(2);
  const author = execFileSync("git", ["-C", repo, "log", "-1", "--format=%an", "main"], { encoding: "utf8" }).trim();
  expect(author).toBe(HUMAN);
  const branchA = execFileSync("git", ["-C", repo, "rev-parse", "review/ju26-issue-2297/A"], { encoding: "utf8" }).trim();
  expect(parents).toContain(branchA);
  expect(execFileSync("git", ["-C", repo, "worktree", "list"], { encoding: "utf8" }).trim().split("\n")).toHaveLength(1);

  const decision = JSON.parse(fs.readFileSync(path.join(E2E_TMP, "decisions", "ju26-issue-2297.json"), "utf8"));
  expect(decision).toMatchObject({ change_id: "ju26-issue-2297", requirement_id: "REQ-2297", who: HUMAN, choice: "A", merge_sha: sha });
  expect(decision.evidence_ids).toEqual(["E-build-head", "E-existing", "E-regen"]);
  expect(typeof decision.ts).toBe("string");

  // Second merge refused.
  const res = await page.request.post("/api/decisions/merge", { data: { change_id: "ju26-issue-2297", choice: "B" } });
  expect(res.status()).toBe(409);
  await page.reload();
  await expect(page.getByTestId("merge-result")).toContainText(sha);
  await expect(page.getByTestId("merge-A")).toHaveCount(0);
});

test("sign-off writes a decision record", async ({ page }) => {
  await signIn(page);
  await page.goto("/change/ju26-issue-2283");
  await page.getByTestId("sign-off").click();
  await expect(page.getByTestId("decision-banner")).toContainText(`Signed off by ${HUMAN}`);
  const decision = JSON.parse(fs.readFileSync(path.join(E2E_TMP, "decisions", "ju26-issue-2283.json"), "utf8"));
  expect(decision).toMatchObject({ change_id: "ju26-issue-2283", requirement_id: "REQ-2283", who: HUMAN, choice: "sign-off", merge_sha: "" });
  expect(decision.evidence_ids).toContain("E-req");
  const again = await page.request.post("/api/decisions/sign-off", { data: { change_id: "ju26-issue-2283" } });
  expect(again.status()).toBe(409);
  await page.goto("/");
  await expect(page.getByTestId("inbox-row").filter({ hasText: "ju26-issue-2283" })).toContainText(`signed off by ${HUMAN}`);
});

test("context page renders markdown and confirms as the human", async ({ page }) => {
  await signIn(page);
  await page.goto("/context");
  await expect(page.getByTestId("context-status")).toHaveText("draft");
  await expect(page.getByTestId("context-markdown").getByRole("heading", { name: "Deliberate conventions" })).toBeVisible();
  await page.getByTestId("confirm-context").click();
  await expect(page.getByTestId("context-status")).toHaveText("confirmed");
  const status = JSON.parse(fs.readFileSync(path.join(E2E_TMP, "context", "status.json"), "utf8"));
  expect(status).toMatchObject({ status: "confirmed", confirmed_by: HUMAN });
});

test("track record renders tables, charts and trust levels", async ({ page }) => {
  await page.goto("/track-record");
  await expect(page.getByTestId("table-T1")).toContainText("requirement-tests");
  await expect(page.getByTestId("table-T1")).toContainText("88%");
  await expect(page.getByTestId("table-T3")).toContainText("full review");
  await expect(page.getByTestId("table-T5")).toContainText("warm");
  await expect(page.getByRole("img", { name: "bar chart" })).toBeVisible();
  await expect(page.getByRole("img", { name: "line chart" })).toBeVisible();
  await expect(page.getByTestId("trust-levels")).toContainText("annotations/public-api");
});

test("log API refuses path traversal", async ({ request }) => {
  const res = await request.get("/api/log?change=ju26-issue-2283&run=20261114T063000Z&path=../../../../etc/passwd");
  expect(res.status()).toBe(404);
  const ok = await request.get("/api/log?change=ju26-issue-2283&run=20261114T063000Z&path=logs/build-head.log&lines=3");
  expect(ok.status()).toBe(200);
  const body = await ok.json();
  expect(body.truncated).toBe(true);
  expect(body.text.split("\n")).toHaveLength(3);
});
