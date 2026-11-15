import { expect, test } from "@playwright/test";
import { abMismatchMessage, firstSentence, humanizeClaim, humanizeGap, humanizeRefusal, humanizeTitle, humanizeVerdict, quotedRequirement, readerVote, stripHtmlComments } from "../lib/humanize";
import type { Evidence, Finding } from "../lib/types";

// Unit tests for the plain-language mapper. Shapes come from grep over runs/**/receipt.json and evidence.json.

test("characterization titles and the behaviour-changed claim read as plain sentences", () => {
  expect(humanizeTitle("Characterization: TypeCollector.CollectGeneric behaviour preserved (3/3 pinned tests pass on base and head)")).toEqual({ text: "Old behaviour of TypeCollector.CollectGeneric still holds", count: "3/3 pinned tests" });
  expect(humanizeTitle("Characterization: TypeCollector.IsOpenGenericType behaviour changed (2/2 pinned tests pass on base, 1 fails on head)")).toEqual({ text: "Old behaviour of TypeCollector.IsOpenGenericType changed", count: "1/2 pinned tests fail" });
  expect(humanizeClaim("behaviour of TypeCollector.IsOpenGenericType changed for 4 callers outside the change: pinned test X passes on base, fails on head")).toBe(
    "Changes how TypeCollector.IsOpenGenericType behaves for 4 other places that rely on it.",
  );
  expect(humanizeClaim("behaviour of A.B changed for 1 caller outside the change")).toBe("Changes how A.B behaves for 1 other place that relies on it.");
});

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
  expect(humanizeTitle("sql-ready: exec receipts-sql-537c33fda3 exit 0 in 1.6s")).toEqual({ text: "Database sandbox ready", count: "1.6 s", raw: "sql-ready: exec receipts-sql-537c33fda3 · exit 0" });
  expect(humanizeTitle("sql-base-up-001_create_orders: exec -i exit 0 in 0.12s").text).toBe("Base migration applied: 001_create_orders");
  expect(humanizeTitle("routed 423 hunks across 237 files")).toEqual({ text: "Every changed block routed to a review depth", count: "423 blocks, 237 files" });
  expect(humanizeTitle("logic-edge lens")).toEqual({ text: "Logic and edge cases lens ran" });
  expect(humanizeTitle("Head builds")).toEqual({ text: "Head builds" });
  expect(humanizeTitle("NativeAOT, Unity and non-net8.0 target frameworks")).toEqual({ text: "NativeAOT, Unity and non-net8.0 target frameworks" });
});

test("sandbox command lines on the receipt become short human lines; the command stays in raw", () => {
  expect(humanizeTitle("impact: <WORK>/impact-tool/Impact.dll --base exit 0 in 1.27s")).toEqual({
    text: "Found who calls the changed code",
    count: "1.3 s",
    raw: "impact: <WORK>/impact-tool/Impact.dll --base · exit 0",
  });
  expect(humanizeTitle("coverage-head: test tests/MessagePack.SourceGenerator.Tests/MessagePack.SourceGenerator.Tests.csproj exit 0 in 17.02s")).toMatchObject({
    text: "Measured which callers tests reach",
    count: "17 s",
  });
  expect(humanizeTitle("characterize-head: test X.csproj exit 0 in 140.4s")).toMatchObject({ text: "Pinned the old behaviour after the change", count: "2 min 20 s" });
  expect(humanizeTitle("gentest-2-base: test X.csproj exit 1 in 3.0s").text).toBe("Ran written test 2 before the change");
  expect(humanizeTitle("mutant-3-suite: test X.csproj exit 0 in 9.9s").text).toBe("Ran the full suite against mutant 3");
  expect(humanizeTitle("ab-B-test-1: test X.csproj exit 0 in 9.9s").text).toBe("Ran reading B's tests (attempt 1)");
  expect(humanizeTitle("compat-roundtrip-head: <WORK>/compat-harness/x.dll --roundtrip exit 0 in 0.4s").text).toBe("Read old saved data after the change");
  expect(humanizeTitle("sql-snapshot-down: exec -i exit 0 in 0.2s").text).toBe("Snapshot of the schema after down");
  expect(humanizeTitle("something-new: foo bar exit 2 in 0.5s")).toMatchObject({ text: "Sandbox step failed: something-new", count: "0.5 s" });
  expect(humanizeTitle("test-head-existing: 169/169 passed, 0 failed (head)")).toEqual({ text: "Ran the existing tests after the change: all pass", count: "169/169", note: "test-head-existing" });
  expect(humanizeTitle("gentest-1-head: tests did not compile")).toEqual({ text: "Ran written test 1 after the change didn't compile", note: "gentest-1-head" });
});

test("blast-radius receipt lines: pins, migrations, A/B builds, weak requirement tests", () => {
  expect(humanizeTitle("TypeCollector.GetObjectInfo: 2 characterization test(s) pass on base and head")).toEqual({ text: "Old behaviour of TypeCollector.GetObjectInfo still holds", count: "2/2 pinned tests" });
  expect(humanizeTitle("TypeCollector constructor: 2 characterization test(s) pass on base and head").text).toBe("Old behaviour of TypeCollector constructor still holds");
  expect(humanizeTitle("Down migration restores the base schema exactly (12 schema objects compared)")).toEqual({ text: "Down migration restores the schema", count: "12 objects" });
  expect(humanizeTitle("No rows lost across the up migration (orders 5 -> 5)")).toEqual({ text: "No rows lost across the migration", note: "orders 5 -> 5" });
  expect(humanizeTitle("Values lost in rename: orders.amount -> orders.amount_cents (checksum of the values differs after up)")).toEqual({ text: "Values lost in the column rename", note: "orders.amount → orders.amount_cents" });
  expect(humanizeTitle("Reading B built as review/ju26-issue-2297/B (380e7af4): builds and the SourceGenerator tests pass (attempt 1 of 2)")).toEqual({
    text: "Reading B builds and passes the tests",
    note: "attempt 1 of 2 · review/ju26-issue-2297/B",
  });
  expect(humanizeTitle("Reading B could not be built as a working branch after 2 attempt(s)")).toEqual({ text: "Reading B could not be built", count: "2 attempts" });
  expect(humanizeTitle("Receipts/X.cs: requirement test does not discriminate: passes before the change (3 tests pass on base and head); weak receipt")).toEqual({
    text: "Requirement tests already passed before the change (weak proof)",
    count: "3/3",
    note: "Receipts/X.cs",
  });
});

test("the blast-radius verdict clause sums callers across functions", () => {
  const h = humanizeVerdict(
    "needs-sign-off",
    "max routed risk 8 >= 5; modifies existing code used elsewhere (blast radius): TypeCollector.GetObjectInfo(INamedTypeSymbol, ISymbol?) (2 caller(s) outside the change), TypeCollector..ctor(Compilation, AnalyzerOptions) (1 caller(s) outside the change)",
  );
  expect(h.reasons).toEqual(["High-risk code (8/10)", "Changes 2 functions that 3 other places use"]);
});

test("not-checked entries become a plain title and reason, with the pipeline wording kept as raw", () => {
  const gap = (what: string, why: string) => humanizeGap({ what, why });
  expect(gap("mutation testing", "19 source hunk(s) routed at risk >= 5, but none has an added line the mutation operators apply to")).toMatchObject({
    title: "Mutation testing",
    why: "none of the 19 risky changed blocks adds a line it can break",
  });
  expect(gap("mutation testing", "no changed source hunk routed at risk >= 5 (mutation threshold)").why).toBe("no changed code was risky enough to need it");
  expect(
    gap(
      "1 callers of CompositeResolverTemplate.TransformText are not exercised by any test (legacy coverage gap): src/MessagePack.SourceGenerator.Unity/CompositeResolverGenerator.cs:44",
      "no test in tests/MessagePack.SourceGenerator.Tests executes these call sites on head (coverlet line coverage); a behaviour change reaching them would go unnoticed by the suite",
    ),
  ).toMatchObject({ title: "1 place that calls CompositeResolverTemplate.TransformText has no test", why: "Unity generator, CompositeResolverGenerator.cs:44" });
  const six = gap(
    "6 callers of ObjectSerializationInfo.Equals are not exercised by any test (legacy coverage gap): src/MessagePack.SourceGenerator/MessagePackGenerator.cs:156, src/MessagePack.UnityShims/Shims.cs:265, src/A/B.cs:1, src/A/C.cs:2, src/A/D.cs:3, src/A/E.cs:4",
    "no test executes these call sites",
  );
  expect(six.title).toBe("6 places that call ObjectSerializationInfo.Equals have no test");
  expect(six.why).toBe("source generator, MessagePackGenerator.cs:156; Unity shims, Shims.cs:265; A, B.cs:1 and 3 more");
  expect(six.raw).toContain("legacy coverage gap");
  expect(gap("semantic call graph of modified members", "call sites are matched by name on syntax trees (Roslyn, no semantic model): common names over-approximate")).toMatchObject({
    title: "Exact call graph",
    why: "callers are found by name, so shared names may over-count and framework or reflection callers are missed",
  });
  expect(gap("logic-edge lens", "unavailable: Verda connection error (lens-logic-edge): The read operation timed out")).toMatchObject({ title: "Logic and edge cases reading", why: "the AI provider did not answer" });
  expect(gap("requirement-tests lens", "unavailable: Verda output invalid after one repair (lens-requirement-tests): $: missing key 'findings'").why).toBe("the AI's answer was malformed and could not be repaired");
  expect(gap("security lens", "not run: deterministic adapter demo (no LLM call on SQL changes in this build)").why).toBe("not run: no AI pass on database changes in this build");
  expect(gap("tests/MessagePack.SourceGenerator.ExternalTypes (changed by this diff)", "not built or run: only MessagePack.SourceGenerator.Tests is in sandbox scope")).toMatchObject({
    title: "SourceGenerator.ExternalTypes test project (also changed here)",
    why: "only the source generator tests run in the sandbox",
  });
  expect(gap("tests/MessagePack.SourceGenerator.ExecutionTests (changed by this diff)", "not run: it does not compile in this sandbox even on base (SDK 8.0.402 ships Roslyn 4.11); see notes/sandbox.md").why).toBe(
    "it does not compile in the sandbox, even before the change",
  );
  expect(gap("NativeAOT, Unity and non-net8.0 target frameworks", "the sandbox builds and tests net8.0 only")).toMatchObject({ title: "Other build targets (NativeAOT, Unity, older .NET)", why: "the sandbox builds and tests .NET 8 only" });
  expect(gap("compatibility byte diff (base vs head serialized bytes)", "no runtime/public API change; byte diff not needed").why).toBe("nothing in the runtime library changed, so there is nothing to compare");
  expect(gap("generated formatter bytes (source generator output, base vs head)", "generated formatter bytes not compared: the harness exercises runtime resolvers, not source-generated formatters").title).toBe("The bytes of the generated code");
  expect(gap("reading B branch and its receipts", "the B variant did not build or pass tests after 2 attempt(s): test failed: X").why).toBe("it did not build or pass tests after 2 attempts");
  expect(gap("A/B review branches", "--no-ab: the two readings are recorded, but branches A and B were not built or reviewed in this run").title).toBe("Building both readings");
  expect(gap("Choosing between reading A and reading B of REQ-2297", "The AI will not pick: 3 of 3 independent readers found two readings: A: x; B: y")).toMatchObject({
    why: "3 of 3 independent readers saw two readings, so the AI will not pick",
  });
  // Unknown shapes: capitalised what, first clause of why, parentheticals dropped; raw only when something was cut.
  expect(gap("locking behaviour under concurrent writes", "migrations ran on an idle database over a single connection")).toEqual({
    title: "Locking behaviour under concurrent writes",
    why: "migrations ran on an idle database over a single connection",
    raw: undefined,
  });
  expect(gap("performance of the migration on production-size data", "the sandbox seeds the repo's own fixture rows (5); duration and lock time on large tables are unknown")).toMatchObject({
    title: "Performance of the migration on production-size data",
    why: "the sandbox seeds the repo's own fixture rows",
  });
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
