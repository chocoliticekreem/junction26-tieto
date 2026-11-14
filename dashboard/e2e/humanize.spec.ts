import { expect, test } from "@playwright/test";
import { abMismatchMessage, firstSentence, humanizeClaim, humanizeRefusal, humanizeTitle, humanizeVerdict, quotedRequirement, readerVote, stripHtmlComments } from "../lib/humanize";
import type { Evidence, Finding } from "../lib/types";

// Unit tests for the plain-language mapper. Shapes come from grep over runs/**/receipt.json and evidence.json.

const finding = (id: string, evidence_ids: string[]): Finding => ({
  id,
  lens: "requirement-tests",
  claim: "",
  location: "",
  severity: "high",
  receipt_plan: { kind: "test", detail: "" },
  evidence_ids,
});
const ev = (id: string, kind: Evidence["kind"], result: Evidence["result"], title = "", details?: Record<string, unknown>): Evidence => ({ id, kind, title, result, details });

test("blocked by verified findings: counts the ids and names the proof kind from the evidence", () => {
  const ctx = {
    findings: [finding("requirement-tests-1", ["gentest-1"]), finding("logic-edge-1", ["gentest-2"])],
    evidence: [ev("gentest-1", "fail-before-pass-after", "fail"), ev("gentest-2", "fail-before-pass-after", "fail")],
  };
  const h = humanizeVerdict("blocked", "verified high finding(s) backed by receipts: requirement-tests-1, logic-edge-1", ctx);
  expect(h.headline).toBe("Blocked: 2 defects proven by failing tests");
  expect(h.reasons).toEqual([]);
  expect(h.mapped).toBe(true);
  expect(h.raw).toContain("backed by receipts");

  const one = humanizeVerdict("blocked", "verified critical finding(s) backed by receipts: F-sql-reversible", {
    findings: [finding("F-sql-reversible", ["sql-reversible"])],
    evidence: [ev("sql-reversible", "migration-up-down", "fail")],
  });
  expect(one.headline).toBe("Blocked: 1 defect proven by running the migration");

  expect(humanizeVerdict("blocked", "verified high finding(s) backed by receipts: compat-break").headline).toBe("Blocked: 1 defect backed by receipts");
});

test("needs-sign-off clauses become short human reasons, with receipt ids resolved against the evidence", () => {
  const ctx = {
    evidence: [
      ev("gentest-2", "fail-before-pass-after", "error", "Receipts/X.cs: edge-case test does not compile on head"),
      ev("mutation", "mutation", "partial", "Mutation: 3 killed, 2 survived, 0 compile errors, 0 timeouts (5 mutants on A.cs)", { survived: 2 }),
    ],
  };
  const h = humanizeVerdict(
    "needs-sign-off",
    "max routed risk 7 >= 5; not every receipt is green (gentest-2, mutation); 3 unverified high-severity hunch(es); area trust below 3: tests=1, type-collector=1; no AI lens ran; risky area touched: public-api",
    ctx,
  );
  expect(h.headline).toBe("Needs a human sign-off");
  expect(h.reasons).toEqual([
    "High-risk code (7/10)",
    "1 test didn't compile",
    "2 mutants survive the suite",
    "3 suspicions without proof",
    "Trust not yet earned: tests, type-collector",
    "No AI lens ran",
    "Never auto-ready area: public-api",
  ]);
  expect(h.mapped).toBe(true);
});

test("not-green receipts without evidence context fall back to a count; survivors parse from the title", () => {
  expect(humanizeVerdict("needs-sign-off", "max routed risk 7 >= 5; not every receipt is green (gentest-1, mutation)").reasons).toEqual(["High-risk code (7/10)", "2 receipts not green"]);
  const h = humanizeVerdict("needs-sign-off", "not every receipt is green (mutation)", {
    evidence: [ev("mutation", "mutation", "partial", "Mutation: 6 killed, 1 survived, 0 compile errors, 0 timeouts (7 mutants on T.cs)")],
  });
  expect(h.reasons).toEqual(["1 mutant survives the suite"]);
});

test("failing tests on head and the two-readings refusal", () => {
  expect(humanizeVerdict("blocked", "9 test(s) fail on head (passing on base, or added by the change): A.B, A.C").headline).toBe("Blocked: 9 tests fail after the change");
  expect(humanizeVerdict("blocked", "1 test(s) fail on head (passing on base, or added by the change): A.B").headline).toBe("Blocked: 1 test fails after the change");
  const ab = humanizeVerdict("decision-A-B", "the requirement has 2 plausible readings that lead to different code; a named human must choose");
  expect(ab.headline).toBe("Two readings: a human picks A or B");
  expect(ab.reasons[0]).toContain("Both readings were built and checked");
});

test("prose reasons fall back to the raw text, unmapped; unknown clauses inside a mapped reason stay verbatim", () => {
  const prose = "All receipts green except one surviving mutant on the new assembly-equality branch.";
  const h = humanizeVerdict("needs-sign-off", prose);
  expect(h.mapped).toBe(false);
  expect(h.reasons).toEqual([prose]);
  const mixed = humanizeVerdict("needs-sign-off", "max routed risk 8 >= 5; something new the mapper has never seen");
  expect(mixed.mapped).toBe(true);
  expect(mixed.reasons).toEqual(["High-risk code (8/10)", "something new the mapper has never seen"]);
});

test("evidence and checked titles get a sentence, a count and a secondary note", () => {
  expect(humanizeTitle("Build head (MessagePack.SourceGenerator.Tests, net8.0)")).toEqual({ text: "Head builds", note: "MessagePack.SourceGenerator.Tests, net8.0" });
  expect(humanizeTitle("Existing SourceGenerator tests on base: 167/167 passed, 0 failed")).toEqual({ text: "Existing tests pass on base", count: "167/167" });
  expect(humanizeTitle("Existing suite on head: 169/169")).toEqual({ text: "Existing tests pass on head", count: "169/169" });
  expect(humanizeTitle("Compatibility bytes: 0/124 cases differ, 0 base payloads fail to read on head, public API +3/-0")).toEqual({
    text: "Serialized bytes identical on base and head",
    count: "124/124 cases",
    note: "public API +3/−0",
  });
  expect(humanizeTitle("Mutation: 11 killed, 3 survived, 3 compile errors, 0 timeouts (17 mutants on A.cs, B.cs)")).toEqual({
    text: "3 mutants on changed lines survive the suite",
    count: "14/17 killed",
    note: "A.cs, B.cs",
  });
  expect(humanizeTitle("Mutation: 10 killed by the change's tests, 0 caught only by the existing suite, 6 survive the full suite, 4 compile errors, 0 timeouts (20 mutants on A.cs)").text).toBe(
    "6 mutants on changed lines survive the suite",
  );
  expect(humanizeTitle("Mutation: 5 killed, 0 survived, 0 compile errors, 0 timeouts (5 mutants on A.cs)").text).toBe("Every mutant on changed lines is killed");
  expect(humanizeTitle("Regeneration: 222/222 generated files verified by 89 passing snapshot tests on head")).toEqual({
    text: "Generated files regenerate identically",
    count: "222/222 files",
    note: "89 snapshot tests",
  });
  expect(humanizeTitle("Regeneration: 192/222 generated files verified by 89 passing snapshot tests on head; 9 head test(s) fail").text).toBe("Some generated files do not regenerate");
  expect(humanizeTitle("Receipts/ClosedGenericStructTests.cs: requirement test fails on base (4/4), passes on head (4/4); refutes 1 lens claim(s): requirement-tests-1")).toEqual({
    text: "Requirement tests fail before, pass after",
    count: "4/4",
    note: "Receipts/ClosedGenericStructTests.cs · refutes requirement-tests-1",
  });
  expect(humanizeTitle("Receipts/Edge.cs: edge-case test fails on head: 2/2 failed")).toEqual({ text: "Edge-case tests fail after the change", count: "2/2 failing", note: "Receipts/Edge.cs" });
  expect(humanizeTitle("Receipts/Edge.cs: edge-case test passes on head (1 tests): no defect shown")).toEqual({ text: "Edge-case tests pass: no defect shown", count: "1/1", note: "Receipts/Edge.cs" });
  expect(humanizeTitle("Receipts/Edge.cs: edge-case test does not compile on head")).toEqual({ text: "Edge-case test didn't compile", note: "Receipts/Edge.cs" });
  expect(humanizeTitle("Migration not reversible: schema after down differs from base (1 line(s))")).toEqual({ text: "Down migration doesn't restore the schema", count: "1 line differ" });
  expect(humanizeTitle("sql-ready: exec receipts-sql-537c33fda3 exit 0 in 1.6s")).toEqual({ text: "Database sandbox ready", count: "1.6s", note: "exec receipts-sql-537c33fda3 · exit 0" });
  expect(humanizeTitle("sql-base-up-001_create_orders: exec -i exit 0 in 0.12s").text).toBe("Base migration applied: 001_create_orders");
  expect(humanizeTitle("routed 423 hunks across 237 files")).toEqual({ text: "Every hunk routed to a review depth", count: "423 hunks, 237 files" });
  expect(humanizeTitle("logic-edge lens")).toEqual({ text: "Logic and edge cases lens ran" });
  expect(humanizeTitle("Head builds")).toEqual({ text: "Head builds" });
  expect(humanizeTitle("NativeAOT, Unity and non-net8.0 target frameworks")).toEqual({ text: "NativeAOT, Unity and non-net8.0 target frameworks" });
});

test("refusal and A/B mismatch copy", () => {
  expect(humanizeRefusal("blocked")).toBe("Blocked: a proven defect must be fixed first");
  expect(humanizeRefusal("decision-A-B")).toBe("Needs an A or B decision, not a sign-off");
  expect(humanizeRefusal("needs-sign-off")).toBeUndefined();
  expect(abMismatchMessage("blocked", true)).toBe("Both readings were built and checked, but neither can be merged yet: they share a proven defect.");
  expect(abMismatchMessage("needs-sign-off", false)).toContain("not as an A/B decision");
});

test("HTML comments are stripped from markdown, across lines too", () => {
  expect(stripHtmlComments("a <!-- seeded convention for the bait bench case --> b")).toBe("a  b");
  expect(stripHtmlComments("x\n<!--\nmulti\n-->\ny")).toBe("x\n\ny");
  expect(stripHtmlComments("no comments")).toBe("no comments");
});

test("mutation-survivor claims become plain sentences; prose claims pass through", () => {
  expect(humanizeClaim("no test in the suite detects linq-first at src/MessagePack.SourceGenerator/CodeAnalysis/TypeCollector.cs:1434")).toBe(
    "No test would notice if this line were broken (First swapped for FirstOrDefault in TypeCollector.cs, line 1434).",
  );
  expect(humanizeClaim("the change's own tests do not catch mutants at src/X/TypeCollector.cs:239, 985, 1448; the existing suite does")).toBe(
    "The change's own tests miss breaks in TypeCollector.cs (lines 239, 985, 1448); only the older tests catch them.",
  );
  expect(humanizeClaim("Migration not reversible: schema after down differs from base (1 line(s))")).toBe("Migration not reversible: schema after down differs from base (1 line(s))");
});

test("firstSentence does not break on e.g.; readerVote and quotedRequirement parse a refusal", () => {
  expect(firstSentence("Add the attribute by default, plus an opt-out setting (e.g. MSBuild property) to disable it. Code: adds an option.")).toEqual({
    lead: "Add the attribute by default, plus an opt-out setting (e.g. MSBuild property) to disable it.",
    rest: "Code: adds an option.",
  });
  expect(firstSentence("Short.")).toEqual({ lead: "Short.", rest: "" });
  const refused = [{ what: "Choosing A or B", why: "The AI will not pick: 3 of 3 independent readers found two readings: A: x; B: y; the requirement says: 'I do not feel like a setting is needed'. Read more." }];
  expect(readerVote(refused)).toEqual({ saw: 3, of: 3 });
  expect(quotedRequirement(refused)).toBe("I do not feel like a setting is needed");
  expect(readerVote([{ what: "Pick the scope", why: "The requirement supports both readings." }])).toBeUndefined();
  expect(quotedRequirement([])).toBeUndefined();
});
