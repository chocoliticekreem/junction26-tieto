import fs from "node:fs";
import path from "node:path";
import { expect, test } from "@playwright/test";
import { decideSentence, plainSummary, purposeSentence, uncheckedSentence } from "../lib/plain-summary";
import type { Evidence, Finding, ImpactReport, Question, RunBundle, TrustReceipt } from "../lib/types";

// Unit tests for the plain-words summary, on the real committed runs (runs/<id>/<latest>/) and the e2e fixtures.

const REAL = path.resolve(__dirname, "../../runs");
const FIXTURES = path.resolve(__dirname, "fixtures/runs");

function load(root: string, changeId: string): RunBundle {
  const ts = fs.readFileSync(path.join(root, changeId, "latest"), "utf8").trim();
  const dir = path.join(root, changeId, ts);
  const json = <T>(f: string, fallback: T): T => (fs.existsSync(path.join(dir, f)) ? (JSON.parse(fs.readFileSync(path.join(dir, f), "utf8")) as T) : fallback);
  const receipt = json<TrustReceipt>("receipt.json", undefined as unknown as TrustReceipt);
  const impact = json<ImpactReport | undefined>("impact.json", undefined);
  return { changeId, runTs: ts, receipt, commands: [], tests: [], evidence: json<Evidence[]>("evidence.json", []), findings: json<Finding[]>("findings.json", []), mutants: [], questions: json<Question[]>("questions.json", []), impact };
}

const texts = (run: RunBundle, opts?: Parameters<typeof plainSummary>[1]) => Object.fromEntries(plainSummary(run, opts).map((s) => [s.key, s.text]));

test("2283 (needs sign-off): fix proven, two untested parts, one test that did not compile", () => {
  const run = load(REAL, "ju26-issue-2283");
  const s = texts(run);
  expect(s.purpose).toBe("This change is about: generator and analyzer crash with IndexOutOfRangeException on a closed generic struct from a referenced assembly.");
  expect(s.proved).toBe(
    "We wrote 4 tests from the issue description alone. They failed before the change and pass after it, so the change does what the issue asks. The 170 tests the project already had still pass.",
  );
  expect(s.wrong).toBe(
    "2 parts of the new code are never tested: if someone broke them, no test would notice. 1 extra test we wrote did not compile, so it proves nothing either way. The reviewer also has 15 suspicions it could not prove; they are listed as hunches and did not count against the change.",
  );
  expect(s.unchecked).toBe("We did not check 3 things, including whether saved data still reads the same and the app's other test suites.");
  expect(s.decide).toBe("Your call: accept it as it is, or send it back for the missing tests.");
  expect(plainSummary(run).map((x) => x.href)).toEqual(["#requirement", "#proven", "#findings", "#not-checked", "#trust-receipt"]);
});

test("2297 (blocked, two readings): requirement tests fail on the change, generated files verified", () => {
  const run = load(REAL, "ju26-issue-2297");
  const s = texts(run);
  expect(s.purpose).toBe("This change is about: add ExcludeFromCodeCoverage attribute to generated code.");
  expect(s.proved).toBe("222 generated files were rebuilt from their templates and came out identical, so nobody had to read them by hand. The 169 tests the project already had still pass.");
  expect(s.wrong).toBe(
    "5 tests we wrote from the requirement fail on the change, so it does not fully do what was asked. The reviewer also has 12 suspicions it could not prove; they are listed as hunches and did not count against the change.",
  );
  expect(s.unchecked).toBe("We did not check 5 things, including planting small breaks to see if the tests notice and the bytes of the generated code.");
  expect(s.decide).toBe("Blocked: it has to be fixed before anyone can approve it.");
  expect(texts(run, { pendingQuestions: 1 }).decide).toBe("Blocked: it has to be fixed before anyone can approve it. 1 question for you is waiting.");
});

test("2129 (blocked): new-API tests are called weaker proof, saved data unchanged, untested parts counted from survivors", () => {
  const run = load(REAL, "ju26-issue-2129");
  const s = texts(run);
  expect(s.purpose).toBe("This change is about: allow generation of Formatter for a type in a referenced assembly.");
  expect(s.proved).toBe(
    "We wrote 1 test for the new behaviour and they pass; that is weaker proof, since there was nothing to fail before the change. Saved data reads exactly the same before and after the change (124 cases compared).",
  );
  expect(s.wrong).toBe(
    "3 tests we wrote from the requirement fail on the change, so it does not fully do what was asked. 6 parts of the new code are never tested: if someone broke them, no test would notice. The reviewer also has 17 suspicions it could not prove; they are listed as hunches and did not count against the change.",
  );
  expect(s.decide).toBe("Blocked: it has to be fixed before anyone can approve it.");
});

test("sql-bad-down (blocked): the migration cannot be undone; lenses collapse into one unchecked item", () => {
  const run = load(REAL, "sql-bad-down");
  const s = texts(run);
  expect(s.purpose).toBe("This change is about: add a discount column to orders.");
  expect(s.proved).toBe("No database rows were lost. The app's own database queries still work on the changed database.");
  expect(s.wrong).toBe("The database change cannot be undone: rolling it back leaves the database different from before.");
  expect(s.unchecked).toBe("We did not check 12 things, including performance of the migration on production-size data, locking behaviour under concurrent writes and the AI reading passes.");
  expect(s.decide).toBe("Blocked: it has to be fixed before anyone can approve it.");
  expect(plainSummary(run).map((x) => x.href)).toEqual(["#requirement", "#proven", "#findings", "#not-checked", "#trust-receipt"]);
});

test("fixture 2283 counts the written tests from details.tests and the survivor from the mutation receipt", () => {
  const run = load(FIXTURES, "ju26-issue-2283");
  const s = texts(run);
  expect(s.proved).toContain("We wrote 2 tests from the issue description alone.");
  expect(s.wrong).toContain("1 part of the new code is never tested: if someone broke it, no test would notice.");
  expect(s.wrong).toContain("TypeCollector.IsOpenGenericType now behaves differently, and 4 other places rely on it.");
  expect(s.wrong).toContain("2 suspicions");
  expect(s.impact).toBe("This change modifies 2 existing functions that 7 other places use. 5 of those places are tested; 2 are not. The old behaviour we pinned changed for one of them.");
  expect(plainSummary(run).map((x) => x.href)).toEqual(["#requirement", "#impact", "#proven", "#findings", "#not-checked", "#trust-receipt"]);
});

test("fixture 2297 (decision A/B) links the decision page and names the choice", () => {
  const run = load(FIXTURES, "ju26-issue-2297");
  const items = plainSummary(run);
  expect(items.find((x) => x.key === "decide")?.text).toBe("Your call: the requirement can be read two ways, so a named person picks reading A or B.");
  expect(items.find((x) => x.key === "decide")?.href).toBe("/decision/ju26-issue-2297");
  expect(items.find((x) => x.key === "wrong")?.text).toBe("Nothing was proven wrong. The reviewer also has 1 suspicion it could not prove; it is listed as a hunch and did not count against the change.");
});

test("edge shapes: titles, decisions, empty lists", () => {
  expect(purposeSentence("Feature Request: Allow X.")).toBe("This change is about: allow X.");
  expect(purposeSentence("IndexOutOfRangeException in TypeCollector")).toBe("This change is about: IndexOutOfRangeException in TypeCollector.");
  expect(purposeSentence("  ")).toBe("This change has no requirement title.");
  expect(uncheckedSentence([])).toContain("declared nothing as unchecked");
  expect(uncheckedSentence([{ what: "Public API surface diff", why: "" }])).toBe("We did not check Public API surface diff.");
  expect(decideSentence("needs-sign-off", { decision: { change_id: "x", requirement_id: "R", who: "Anson", choice: "sign-off", evidence_ids: [], ts: "2026-11-14T10:00:00+00:00" } })).toBe("Decided: Anson signed this off on 2026-11-14.");
  expect(decideSentence("decision-A-B", { decision: { change_id: "x", requirement_id: "R", who: "Anson", choice: "B", evidence_ids: [], ts: "2026-11-14T10:00:00+00:00" } })).toBe("Decided: Anson picked reading B on 2026-11-14.");
  expect(decideSentence("auto-ready")).toContain("no sign-off is needed");
});
