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
  await expect(page.getByTestId("finding").getByTestId("evidence-card")).toHaveCount(0);
  await page.getByTestId("finding-toggle").click();
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

  // Routing map (collapsed) and fail-before/pass-after table (inside the receipts disclosure).
  await expect(page.getByTestId("routing-tree")).toHaveCount(0);
  await page.getByTestId("routing-toggle").click();
  await expect(page.getByTestId("routing-tree")).toContainText("5 files");
  await page.getByTestId("receipts-toggle").click();
  const reqCard = page.locator("#evidence-E-req");
  await expect(reqCard).toContainText("Requirement tests fail on base, pass on head");
  await reqCard.getByTestId("evidence-toggle").click();
  await expect(reqCard.getByRole("cell", { name: "failed" })).toHaveCount(2);
  await expect(reqCard.getByRole("cell", { name: "passed" })).toHaveCount(2);

  // Log excerpt via the API route.
  const toggle = page.getByTestId("log-toggle").filter({ hasText: "test-base-req.log" });
  await toggle.click();
  await expect(page.getByTestId("log-excerpt")).toContainText("IndexOutOfRangeException");
});

test("Proven block lists passing positive receipts and opens the evidence card on click", async ({ page }) => {
  await page.goto("/change/ju26-issue-2283");
  const lines = page.getByTestId("proven").getByTestId("proven-line");
  await expect(lines).toHaveText(["Requirement tests fail on base, pass on head · 2/2", "Serialized bytes identical on base and head · 12/12 cases"]);
  const reqCard = page.locator("#evidence-E-req");
  await expect(reqCard).not.toBeVisible();
  await lines.first().click();
  await expect(reqCard).toBeVisible();
  await expect(reqCard.getByTestId("evidence-toggle")).toHaveAttribute("aria-expanded", "true");
  await expect(reqCard.getByRole("cell", { name: "passed" })).toHaveCount(2);
  await expect(page).toHaveURL(/#evidence-E-req$/);

  await page.goto("/change/ju26-issue-2297");
  await expect(page.getByTestId("proven-line")).toHaveText(["Generated files verified against head · 222/222 files"]);

  await page.goto("/change/sql-bad-down");
  await expect(page.getByTestId("proven-line")).toHaveText(["No rows lost across the migration · 1 table"]);

  await page.goto(`/change/${encodeURIComponent("mock/2283/logic")}`);
  await expect(page.getByTestId("verdict-pill").first()).toBeVisible();
  await expect(page.getByTestId("proven")).toHaveCount(0);
});

test("SQL adapter change page shows the schema diff and row counts behind the block", async ({ page }) => {
  await page.goto("/change/sql-bad-down");
  await expect(page.getByTestId("verdict-pill").first()).toHaveText("Blocked");
  await expect(page.getByTestId("verdict-headline")).toHaveText("Blocked: 1 defect proven by running the migration");
  await expect(page.getByTestId("verdict-reason")).toHaveCount(0);
  await page.getByTestId("verdict-raw-toggle").click();
  await expect(page.getByTestId("verdict-reason")).toContainText("F-sql-reversible");
  const finding = page.getByTestId("finding");
  await expect(finding).toHaveCount(1);
  await finding.getByTestId("finding-toggle").click();
  const reversible = finding.locator("#evidence-sql-reversible");
  await expect(reversible).toContainText("Migration not reversible");
  await expect(reversible).toContainText("+column orders.discount_cents");
  await page.getByTestId("receipts-toggle").click();
  await page.locator("#evidence-sql-rows").getByTestId("evidence-toggle").click();
  const rows = page.locator("#evidence-sql-rows").getByTestId("evidence-row_counts");
  await expect(rows.getByRole("columnheader")).toHaveText(["table", "base", "after up", "after down"]);
  await expect(rows.getByRole("cell", { name: "5" })).toHaveCount(3);
  await expect(page.getByTestId("not-checked-list")).toContainText("locking behaviour under concurrent writes");
  await expect(page.getByTestId("not-checked-list")).not.toContainText("NativeAOT");
});

test("2297 routing map collapses 222 generated files into one block", async ({ page }) => {
  await page.goto("/change/ju26-issue-2297");
  await page.getByTestId("routing-toggle").click();
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
  await expect(page.getByTestId("sign-off")).toHaveText("Sign-off locked");
  await expect(page.getByTestId("sign-off-refused")).toHaveText("Blocked: a proven defect must be fixed first");
  await expect(page.getByTestId("sign-off-refused")).toHaveAttribute("title", /blocked \(A verified finding/);
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

test("machine strings never reach the default view: verdict reasons, lock reasons, HTML comments", async ({ page }) => {
  for (const url of ["/change/sql-bad-down", "/change/ju26-issue-2297", "/decision/ju26-issue-2297"]) {
    await page.goto(url);
    await expect(page.locator("body")).not.toContainText("backed by receipts:");
    await expect(page.locator("body")).not.toContainText("not decision-A-B");
    await expect(page.locator("body")).not.toContainText("lens output from recorded fixtures");
  }
  await page.goto("/change/sql-bad-down");
  await expect(page.getByTestId("trust-receipt")).toContainText("Down migration doesn't restore the schema");
  await expect(page.getByTestId("verdict-stamp")).toHaveText("Blocked");
  await page.goto("/context");
  await expect(page.getByTestId("context-markdown")).not.toContainText("<!--");
  await expect(page.getByTestId("context-markdown")).not.toContainText("seeded convention");
  await expect(page.getByTestId("context-markdown")).toContainText("their authors cannot act on them.");
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
  await expect(page.getByTestId("table-T3")).toBeHidden();
  await page.getByTestId("t3-toggle").click();
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
  await expect(page.getByTestId("stat-tile").first()).toHaveAttribute("title", /Cold run, empty ledger/);

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

  await page.getByTestId("t2-toggle").click();
  await expect(page.getByTestId("table-T2")).toContainText("correctness");
  await expect(page.getByTestId("t6-headline")).toContainText("233 of 233 findings shown have an executed receipt");
  await page.getByTestId("t7-toggle").click();
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

test("plain-words summary and product context on the change page; predates fallback without context.json", async ({ page }) => {
  await page.goto("/change/ju26-issue-2283");
  const summary = page.getByTestId("plain-summary");
  await expect(summary).toBeVisible();
  await expect(summary.getByTestId("plain-purpose")).toContainText("This change is about: fix IndexOutOfRangeException on closed generic struct from referenced assembly.");
  await expect(summary.getByTestId("plain-proved")).toContainText("We wrote 2 tests from the issue description alone. They failed before the change and pass after it");
  await expect(summary.getByTestId("plain-wrong")).toContainText("1 part of the new code is never tested");
  await expect(summary.getByTestId("plain-unchecked")).toContainText("We did not check 3 things");
  // "sign-off writes a decision record" ran earlier in this serial file, so the last line reports the decision.
  await expect(summary.getByTestId("plain-decide")).toContainText(`Decided: ${HUMAN} signed this off on`);
  await expect(summary.getByTestId("plain-proved").locator("a")).toHaveAttribute("href", "#proven");
  await expect(summary.getByTestId("plain-wrong").locator("a")).toHaveAttribute("href", "#findings");

  // The one-time strip and its dismissal.
  await expect(page.getByTestId("how-to-read")).toContainText("How to read this page");
  await page.getByTestId("how-to-read-dismiss").click();
  await expect(page.getByTestId("how-to-read")).toHaveCount(0);
  await page.reload();
  await expect(page.getByTestId("plain-summary")).toBeVisible();
  await expect(page.getByTestId("how-to-read")).toHaveCount(0);

  // Glossary terms: hover shows the definition, headings keep their words.
  await expect(page.getByRole("heading", { name: "Findings" })).toBeVisible();
  await page.getByTestId("term-finding").first().hover();
  await expect(page.getByRole("tooltip").filter({ hasText: "A problem with a receipt behind it" })).toBeVisible();

  // Product context from context.json: draft pill, nudge, entries, the ledger badge and a citation.
  await expect(page.getByTestId("run-context-status")).toHaveText("draft");
  await expect(page.getByTestId("run-context-nudge")).toHaveAttribute("href", "/context");
  const citation = page.getByTestId("context-citation");
  await expect(citation).toHaveCount(1);
  await expect(citation).toContainText("Your context says");
  await expect(citation).toContainText("their authors cannot act on them");
  await expect(citation).toContainText("not confirmed yet, so this stays a hunch");
  await expect(citation).toContainText("silently skips closed generic structs");
  await expect(page.getByTestId("context-entry")).toHaveCount(0);
  await page.getByTestId("context-entries-toggle").click();
  await expect(page.getByTestId("context-entry")).toHaveCount(2);
  await expect(page.getByTestId("context-entry-ledger")).toHaveCount(1);
  await expect(page.getByTestId("context-predates")).toHaveCount(0);

  // A run without context.json says so instead of pretending.
  await page.goto("/change/ju26-issue-2297");
  await expect(page.getByTestId("context-predates")).toHaveText("This run predates context recording.");
  await expect(page.getByTestId("context-citation")).toHaveCount(0);
  // "Merge A" ran earlier in this serial file; the undecided A/B wording is covered in plain-summary.spec.ts.
  await expect(page.getByTestId("plain-decide")).toContainText(`Decided: ${HUMAN} picked reading A on`);
  await expect(page.getByTestId("plain-decide").locator("a")).toHaveAttribute("href", "#trust-receipt");
});

test("inbox legend explains the three verdicts in plain words", async ({ page }) => {
  await page.goto("/");
  const legend = page.getByTestId("verdict-legend");
  await expect(legend).toContainText("a defect is proven; fix it first");
  await expect(legend).toContainText("nothing proven wrong; a person decides");
  await expect(legend).toContainText("two readings; a person picks one");
  await expect(legend.getByTestId("verdict-pill")).toHaveCount(3);
});
