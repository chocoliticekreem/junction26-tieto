// "Impact on other features" in plain words, derived only from impact.json. Pure, deterministic, no LLM.

import type { ImpactMember, ImpactReport, ResultKind } from "./types";

function plural(n: number, one: string, many = `${one}s`): string {
  return `${n} ${n === 1 ? one : many}`;
}

/** The one-paragraph answer to "does this change risk breaking other features?" */
export function impactHeadline(impact: ImpactReport): string {
  const s = impact.summary;
  const touched = s.modified_existing + s.deleted;
  if (touched === 0) {
    if (s.new > 0) return "This change only adds new code; nothing else in the product calls it, so it can't break other features.";
    return "This change touches no functions we could trace, so there is nothing for other features to depend on.";
  }

  const what =
    s.modified_existing && s.deleted
      ? `modifies ${plural(s.modified_existing, "existing function")} and deletes ${s.deleted}`
      : s.deleted
        ? `deletes ${plural(s.deleted, "existing function")}`
        : `modifies ${plural(s.modified_existing, "existing function")}`;
  const places = s.callers_outside_change;
  const first = places === 0 ? `This change ${what} that nothing else in the product uses.` : `This change ${what} that ${plural(places, "other place")} ${places === 1 ? "uses" : "use"}.`;

  const sentences = [first];
  if (places > 0) {
    if (!impact.coverage_available) sentences.push("We could not tell which of those places are tested.");
    else if (s.untested_callers === 0) sentences.push(places === 1 ? "That place is tested." : `All ${places} of those places are tested.`);
    else {
      const tested = places - s.untested_callers;
      sentences.push(`${tested} of those places ${tested === 1 ? "is" : "are"} tested; ${s.untested_callers} ${s.untested_callers === 1 ? "is" : "are"} not.`);
    }
  }

  const pinned = impact.characterization.filter((c) => c.result !== "invalid");
  const changed = impact.characterization.filter((c) => c.result === "changed").length;
  if (changed > 0) sentences.push(changed === 1 ? "The old behaviour we pinned changed for one of them." : `The old behaviour we pinned changed for ${changed} of them.`);
  else if (pinned.length > 0) sentences.push("The old behaviour we pinned still holds.");
  else sentences.push("We did not manage to pin the old behaviour, so this rests on the existing tests alone.");
  return sentences.join(" ");
}

export interface ImpactStatus {
  result: ResultKind;
  /** "7 places, 2 untested" for the receipt sub-line. */
  note: string;
}

/** PASS when nothing outside the change can be hurt or every pinned behaviour held with full coverage; FAIL on a changed behaviour; PARTIAL otherwise. */
export function impactStatus(impact: ImpactReport): ImpactStatus {
  const s = impact.summary;
  const touched = s.modified_existing + s.deleted;
  const places = s.callers_outside_change;
  const changed = impact.characterization.some((c) => c.result === "changed") || s.behaviour_changes > 0;
  const invalid = impact.characterization.some((c) => c.result === "invalid");
  const modified = impact.members.filter((m) => m.kind !== "new");
  const unpinned = modified.some((m) => m.callers_outside_change > 0 && !impact.characterization.some((c) => c.member === m.name));
  const note = touched === 0 ? "only new code" : `${plural(places, "place")}, ${s.untested_callers} untested`;
  if (changed) return { result: "fail", note };
  if (touched === 0) return { result: "pass", note };
  if (!impact.coverage_available || s.untested_callers > 0 || invalid || unpinned) return { result: "partial", note };
  return { result: "pass", note };
}

/** One clause for the queue card, only when there is something to warn about. */
export function impactQueueClause(impact: ImpactReport): string | undefined {
  const s = impact.summary;
  if (s.callers_outside_change === 0) return undefined;
  const changed = impact.characterization.filter((c) => c.result === "changed").length;
  if (changed === 0 && s.untested_callers === 0) return undefined;
  const lead = `Changes code ${plural(s.callers_outside_change, "other place")} ${s.callers_outside_change === 1 ? "uses" : "use"}`;
  const tails: string[] = [];
  if (changed) tails.push(`old behaviour changed for ${plural(changed, "function")}`);
  if (s.untested_callers) tails.push(`${s.untested_callers} of them ${s.untested_callers === 1 ? "is" : "are"} untested`);
  return `${lead}; ${tails.join(", ")}.`;
}

export type PinState = "preserved" | "changed" | "invalid" | "none";

/** What the pinned (characterization) tests said about one member. */
export function pinState(impact: ImpactReport, member: ImpactMember): { state: PinState; evidenceId?: string; tests: number } {
  const c = impact.characterization.find((x) => x.member === member.name);
  if (!c) return { state: "none", tests: 0 };
  return { state: c.result, evidenceId: c.evidence_id, tests: c.test_names.length };
}

/** The honest footnote under the blast radius, from the pipeline's own description of its method. */
export function impactFootnote(impact: ImpactReport): string {
  const byName = /name-based|syntax, not semantic/i.test(impact.method);
  const calls = byName ? "Call sites found by name, not by full type analysis" : impact.method ? `Call sites found by: ${impact.method}` : "Call-site method not recorded";
  const coverage = impact.coverage_available ? "test coverage from the source-generator test project only" : "test coverage was not available for this run";
  return `${calls}; ${coverage}.`;
}
