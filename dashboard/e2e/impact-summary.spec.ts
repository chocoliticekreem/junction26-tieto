import fs from "node:fs";
import path from "node:path";
import { expect, test } from "@playwright/test";
import { impactFootnote, impactHeadline, impactQueueClause, impactStatus, pinState } from "../lib/impact-summary";
import type { ImpactCaller, ImpactMember, ImpactReport } from "../lib/types";

// Unit tests for the "Impact on other features" sentences. No server needed; Playwright is the dashboard's only test runner.

const FIXTURES = path.resolve(__dirname, "fixtures/runs");

function fixture(changeId: string): ImpactReport {
  const ts = fs.readFileSync(path.join(FIXTURES, changeId, "latest"), "utf8").trim();
  return JSON.parse(fs.readFileSync(path.join(FIXTURES, changeId, ts, "impact.json"), "utf8")) as ImpactReport;
}

const caller = (member: string, tested: boolean | null, in_change = false): ImpactCaller => ({ file: `src/${member.split(".")[0]}.cs`, line: 10, member, in_change, tested });

function member(name: string, kind: ImpactMember["kind"], callers: ImpactCaller[], extra: Partial<ImpactMember> = {}): ImpactMember {
  const outside = callers.filter((c) => !c.in_change);
  return {
    name,
    file: "src/A.cs",
    kind,
    signature_changed: false,
    callers,
    callers_outside_change: outside.length,
    untested_callers: outside.filter((c) => c.tested === false).length,
    ambiguous: false,
    ...extra,
  };
}

function report(members: ImpactMember[], characterization: ImpactReport["characterization"] = [], coverage_available = true): ImpactReport {
  const touched = members.filter((m) => m.kind !== "new");
  return {
    method: "name-based call sites (Roslyn syntax, not semantic)",
    members,
    characterization,
    coverage_available,
    summary: {
      modified_existing: members.filter((m) => m.kind === "modified").length,
      new: members.filter((m) => m.kind === "new").length,
      deleted: members.filter((m) => m.kind === "deleted").length,
      callers_outside_change: touched.reduce((s, m) => s + m.callers_outside_change, 0),
      untested_callers: touched.reduce((s, m) => s + m.untested_callers, 0),
      behaviour_changes: characterization.filter((c) => c.result === "changed").length,
    },
  };
}

const pin = (m: string, result: "preserved" | "changed" | "invalid", n = 2): ImpactReport["characterization"][number] => ({
  member: m,
  test_names: Array.from({ length: n }, (_, i) => `Pin.${m}_${i}`),
  base: result === "invalid" ? "compile-error" : "pass",
  head: result === "preserved" ? "pass" : result === "changed" ? "fail" : "not-run",
  result,
  evidence_id: `E-pin-${m}`,
});

test("only new code: nothing else can break, PASS, no queue clause", () => {
  const r = report([member("Fmt.Emit", "new", [caller("Fmt.Transform", true, true)]), member("Res.Emit", "new", [])]);
  expect(impactHeadline(r)).toBe("This change only adds new code; nothing else in the product calls it, so it can't break other features.");
  expect(impactStatus(r)).toEqual({ result: "pass", note: "only new code" });
  expect(impactQueueClause(r)).toBeUndefined();
});

test("modified with callers, all tested, behaviour preserved: PASS", () => {
  const r = report([member("TC.Collect", "modified", [caller("TC.A", true), caller("TC.B", true), caller("An.X", true, true)])], [pin("TC.Collect", "preserved", 3)]);
  expect(impactHeadline(r)).toBe("This change modifies 1 existing function that 2 other places use. All 2 of those places are tested. The old behaviour we pinned still holds.");
  expect(impactStatus(r)).toEqual({ result: "pass", note: "2 places, 0 untested" });
  expect(impactQueueClause(r)).toBeUndefined();
  expect(pinState(r, r.members[0])).toEqual({ state: "preserved", evidenceId: "E-pin-TC.Collect", tests: 3 });
});

test("behaviour changed: FAIL, red clause on the queue card", () => {
  const r = report(
    [member("TC.Open", "modified", [caller("TC.A", true), caller("TC.B", true), caller("TC.C", true)]), member("TC.Other", "modified", [caller("TC.D", true)])],
    [pin("TC.Open", "changed"), pin("TC.Other", "preserved")],
  );
  expect(impactHeadline(r)).toBe("This change modifies 2 existing functions that 4 other places use. All 4 of those places are tested. The old behaviour we pinned changed for one of them.");
  expect(impactStatus(r).result).toBe("fail");
  expect(impactStatus(r).note).toBe("4 places, 0 untested");
  expect(impactQueueClause(r)).toBe("Changes code 4 other places use; old behaviour changed for 1 function.");
  expect(pinState(r, r.members[0]).state).toBe("changed");
});

test("untested callers: PARTIAL, counts tested vs not, queue clause names the untested ones", () => {
  const r = report([member("TC.Collect", "modified", [caller("TC.A", true), caller("TC.B", false), caller("TC.C", false)])], [pin("TC.Collect", "preserved")]);
  expect(impactHeadline(r)).toBe("This change modifies 1 existing function that 3 other places use. 1 of those places is tested; 2 are not. The old behaviour we pinned still holds.");
  expect(impactStatus(r)).toEqual({ result: "partial", note: "3 places, 2 untested" });
  expect(impactQueueClause(r)).toBe("Changes code 3 other places use; 2 of them are untested.");
});

test("coverage unavailable: says so, PARTIAL, grey dots", () => {
  const r = report([member("TC.Collect", "modified", [caller("TC.A", null), caller("TC.B", null)])], [pin("TC.Collect", "preserved")], false);
  expect(impactHeadline(r)).toBe("This change modifies 1 existing function that 2 other places use. We could not tell which of those places are tested. The old behaviour we pinned still holds.");
  expect(impactStatus(r)).toEqual({ result: "partial", note: "2 places, 0 untested" });
  expect(impactFootnote(r)).toBe("Call sites found by name, not by full type analysis; test coverage was not available for this run.");
});

test("edge shapes: unpinned member, deleted member, single caller, invalid pin, no members", () => {
  const unpinned = report([member("TC.Collect", "modified", [caller("TC.A", true)])]);
  expect(impactHeadline(unpinned)).toBe("This change modifies 1 existing function that 1 other place uses. That place is tested. We did not manage to pin the old behaviour, so this rests on the existing tests alone.");
  expect(impactStatus(unpinned).result).toBe("partial");
  expect(pinState(unpinned, unpinned.members[0])).toEqual({ state: "none", tests: 0 });

  const deleted = report([member("TC.Old", "deleted", []), member("TC.Collect", "modified", [caller("TC.A", true)])], [pin("TC.Collect", "preserved")]);
  expect(impactHeadline(deleted)).toMatch(/^This change modifies 1 existing function and deletes 1 that 1 other place uses\./);

  const invalid = report([member("TC.Collect", "modified", [caller("TC.A", true)])], [pin("TC.Collect", "invalid")]);
  expect(impactHeadline(invalid)).toContain("We did not manage to pin the old behaviour");
  expect(impactStatus(invalid).result).toBe("partial");

  const orphan = report([member("TC.Collect", "modified", [])]);
  expect(impactHeadline(orphan)).toBe("This change modifies 1 existing function that nothing else in the product uses. We did not manage to pin the old behaviour, so this rests on the existing tests alone.");
  expect(impactQueueClause(orphan)).toBeUndefined();

  const empty = report([]);
  expect(impactHeadline(empty)).toBe("This change touches no functions we could trace, so there is nothing for other features to depend on.");
  expect(impactStatus(empty)).toEqual({ result: "pass", note: "only new code" });
});

test("fixtures: 2283 (modified, one changed, two untested) and 2297 (only new)", () => {
  const r2283 = fixture("ju26-issue-2283");
  expect(impactHeadline(r2283)).toBe("This change modifies 2 existing functions that 7 other places use. 5 of those places are tested; 2 are not. The old behaviour we pinned changed for one of them.");
  expect(impactStatus(r2283)).toEqual({ result: "fail", note: "7 places, 2 untested" });
  expect(impactQueueClause(r2283)).toBe("Changes code 7 other places use; old behaviour changed for 1 function, 2 of them are untested.");
  expect(impactFootnote(r2283)).toBe("Call sites found by name, not by full type analysis; test coverage from the source-generator test project only.");

  const r2297 = fixture("ju26-issue-2297");
  expect(impactHeadline(r2297)).toBe("This change only adds new code; nothing else in the product calls it, so it can't break other features.");
  expect(impactStatus(r2297).result).toBe("pass");
});
