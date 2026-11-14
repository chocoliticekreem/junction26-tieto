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
  await expect(rows).toHaveCount(6);
  await expect(page.getByRole("heading", { name: /Tieto sample changes/ })).toBeVisible();
  await expect(page.getByRole("heading", { name: /^Bench/ })).toBeVisible();
  await expect(page.getByRole("heading", { name: /^SQL migrations/ })).toBeVisible();
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
  const suiteOnly = page.getByTestId("finding").getByTestId("mutant-table").getByText("caught by existing suite");
  await expect(suiteOnly).toHaveCount(1);
  await expect(suiteOnly).toHaveAttribute("title", "caught by the existing suite, not by the change's tests");

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

test("SQL adapter change page shows the schema diff and row counts behind the block", async ({ page }) => {
  await page.goto("/change/sql-bad-down");
  await expect(page.getByTestId("verdict-pill").first()).toHaveText("Blocked");
  await expect(page.getByTestId("verdict-reason")).toContainText("F-sql-reversible");
  const finding = page.getByTestId("finding");
  await expect(finding).toHaveCount(1);
  const reversible = finding.locator("#evidence-sql-reversible");
  await expect(reversible).toContainText("Migration not reversible");
  await expect(reversible).toContainText("+column orders.discount_cents");
  const rows = page.locator("#evidence-sql-rows").getByTestId("evidence-row_counts");
  await expect(rows.getByRole("columnheader")).toHaveText(["table", "base", "after up", "after down"]);
  await expect(rows.getByRole("cell", { name: "5" })).toHaveCount(3);
  await expect(page.getByTestId("not-checked-list")).toContainText("locking behaviour under concurrent writes");
  await expect(page.getByTestId("not-checked-list")).not.toContainText("NativeAOT");
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

test("sign-off is refused on a blocked change, in the UI and the API", async ({ page }) => {
  await signIn(page);
  await page.goto(`/change/${encodeURIComponent("mock/2283/logic")}`);
  await expect(page.getByTestId("sign-off")).toBeDisabled();
  await expect(page.getByTestId("sign-off-refused")).toContainText("blocked (A verified finding");
  const res = await page.request.post("/api/decisions/sign-off", { data: { change_id: "mock/2283/logic" } });
  expect(res.status()).toBe(409);
  expect((await res.json()).error).toContain("A verified finding: two edge tests");
  const ab = await page.request.post("/api/decisions/sign-off", { data: { change_id: "ju26-issue-2297" } });
  expect(ab.status()).toBe(409);
  expect(fs.existsSync(path.join(E2E_TMP, "decisions", "mock", "2283", "logic.json"))).toBe(false);
});

test("merge is refused when the parent verdict is not decision-A-B", async ({ page }) => {
  await signIn(page);
  const res = await page.request.post("/api/decisions/merge", { data: { change_id: "ju26-issue-2283", choice: "A" } });
  expect(res.status()).toBe(409);
  expect((await res.json()).error).toContain("not decision-A-B");
  expect(fs.existsSync(path.join(E2E_TMP, "decisions", "ju26-issue-2283.json"))).toBe(false);
});

test("merge is refused when the branch moved after its review", async ({ page }) => {
  await signIn(page);
  const repo = path.join(E2E_TMP, "mock-repo.git");
  const ref = "refs/heads/review/ju26-issue-2297/B";
  const reviewed = execFileSync("git", ["-C", repo, "rev-parse", ref], { encoding: "utf8" }).trim();
  const tree = execFileSync("git", ["-C", repo, "rev-parse", `${reviewed}^{tree}`], { encoding: "utf8" }).trim();
  const moved = execFileSync(
    "git",
    ["-C", repo, "-c", "user.name=Intruder", "-c", "user.email=i@x", "commit-tree", tree, "-p", reviewed, "-m", "unreviewed"],
    { encoding: "utf8" },
  ).trim();
  execFileSync("git", ["-C", repo, "update-ref", ref, moved]);
  const mainBefore = execFileSync("git", ["-C", repo, "rev-parse", "main"], { encoding: "utf8" }).trim();
  try {
    const res = await page.request.post("/api/decisions/merge", { data: { change_id: "ju26-issue-2297", choice: "B" } });
    expect(res.status()).toBe(409);
    expect((await res.json()).error).toContain("moved since it was reviewed");
    expect(execFileSync("git", ["-C", repo, "rev-parse", "main"], { encoding: "utf8" }).trim()).toBe(mainBefore);
    expect(fs.existsSync(path.join(E2E_TMP, "decisions", "ju26-issue-2297.json"))).toBe(false);
  } finally {
    execFileSync("git", ["-C", repo, "update-ref", ref, reviewed]);
  }
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
  const md = await (await page.request.get("/api/receipt-markdown?change=ju26-issue-2283")).text();
  expect(md).toContain(`**Signed off by: ${HUMAN}** at ${decision.ts}`);
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
  await page.getByTestId("t5-toggle").click();
  await expect(page.getByTestId("table-T5")).toContainText("warm");
  await expect(page.getByRole("img", { name: "bar chart" })).toBeVisible();
  await expect(page.getByRole("img", { name: "line chart" })).toBeVisible();
  await expect(page.getByTestId("trust-levels")).toContainText("annotations/public-api");
});

test("track record leads with headline tiles from summary.json and the new tables", async ({ page }) => {
  await page.goto("/track-record");
  const headline = page.getByTestId("headline");
  const tiles = headline.getByTestId("stat-tile");
  await expect(tiles).toHaveCount(6);
  await expect(tiles.filter({ hasText: "Planted defects caught" }).getByTestId("stat-value")).toHaveText("19/24");
  await expect(tiles.filter({ hasText: "Escapes" }).getByTestId("stat-value")).toHaveText("0");
  await expect(tiles.filter({ hasText: "False alarms" }).getByTestId("stat-value")).toHaveText("4/8");
  await expect(tiles.filter({ hasText: "Questions per change" }).getByTestId("stat-value")).toHaveText("0.25 → 0.04");
  await expect(tiles.filter({ hasText: "routed away from humans" }).getByTestId("stat-value")).toHaveText("94%");
  await expect(tiles.filter({ hasText: "Model cost per change" }).getByTestId("stat-value")).toHaveText("$0.68");
  await expect(page.getByTestId("provenance")).toContainText("PR #11");
  await expect(page.getByTestId("provenance")).toContainText("simulated human");

  await expect(page.getByTestId("t5-summary")).toContainText("bait false alarms");
  await expect(page.getByTestId("trust-history")).toContainText("source-generator/tests");
  await expect(page.getByTestId("trust-history")).toContainText("Stayed at L1 for the whole bench: security, source-generator/analyzer");
  await expect(page.getByRole("img", { name: /trust level over the bench/ })).toHaveCount(1);

  const sample = page.getByTestId("t4-sample");
  await expect(sample).toContainText("ju26-issue-2297");
  await expect(sample).toContainText("15");
  await expect(sample).toContainText("of 237 files to a human");
  await expect(page.getByTestId("table-T4")).toBeHidden();
  await page.getByTestId("t4-bench-toggle").click();
  await expect(page.getByTestId("table-T4")).toContainText("mock/2283/logic");

  await expect(page.getByTestId("table-T2")).toContainText("correctness");
  await expect(page.getByTestId("t6-headline")).toContainText("233 of 233 findings shown have an executed receipt");
  await expect(page.getByTestId("table-T7")).toContainText("PR #2305");
});

test("Copy as PR comment puts the receipt markdown on the clipboard", async ({ page, context }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.goto("/change/ju26-issue-2283");
  await page.getByTestId("copy-pr-comment").click();
  await expect(page.getByTestId("copy-pr-comment")).toHaveText("Copied");
  const text = await page.evaluate(() => navigator.clipboard.readText());
  expect(text).toContain("## Trust receipt: REQ-2283");
  expect(text).toContain("**Verdict: Needs sign-off**");
  await expect(page.getByTestId("receipt-markdown-link")).toHaveAttribute("href", "/api/receipt-markdown?change=ju26-issue-2283");
});

test("receipt markdown API returns a PR comment with the verdict and requirement id", async ({ request }) => {
  const res = await request.get("/api/receipt-markdown?change=ju26-issue-2283");
  expect(res.status()).toBe(200);
  expect(res.headers()["content-type"]).toContain("text/markdown");
  const md = await res.text();
  expect(md).toContain("## Trust receipt: REQ-2283 — Fix IndexOutOfRangeException");
  expect(md).toContain("**Verdict: Needs sign-off**");
  expect(md).toContain("| Requirement tests fail on base, pass on head | requirement-tests | pass | `E-req` |");
  expect(md).toContain("`F-2283-1`");
  expect(md).toContain("`E-mut` mutation: Mutants on changed lines: 3 killed, 1 caught only by the existing suite, 1 survived — partial");
  expect(md).toContain("2 hunches withheld");
  expect(md).toContain("Signed off by:");

  const missing = await request.get("/api/receipt-markdown?change=nope");
  expect(missing.status()).toBe(404);
  const bad = await request.get("/api/receipt-markdown");
  expect(bad.status()).toBe(400);
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
