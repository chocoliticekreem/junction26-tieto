// Plain-language renderings of the pipeline's machine strings. Pure functions; the raw text is always
// returned too so the UI can keep it one hover away. Unknown shapes fall back to the raw text.

import type { Evidence, Finding, VerdictKind } from "./types";

export interface HumanVerdict {
  headline: string;
  reasons: string[];
  raw: string;
  /** false when nothing in the reason matched a known shape and `reasons` is just the raw text. */
  mapped: boolean;
}

export interface HumanizeCtx {
  findings?: Finding[];
  evidence?: Evidence[];
}

export const verdictHeadline: Record<VerdictKind, string> = {
  "auto-ready": "Auto-ready",
  "needs-sign-off": "Needs a human sign-off",
  "decision-A-B": "Two readings: a human picks A or B",
  blocked: "Blocked",
};

function plural(n: number, one: string, many = `${one}s`): string {
  return `${n} ${n === 1 ? one : many}`;
}

function num(v: unknown): number | undefined {
  return typeof v === "number" ? v : undefined;
}

const TEST_KINDS = new Set(["fail-before-pass-after", "new-api", "existing-tests"]);
const MIGRATION_KINDS = new Set(["migration-up-down", "row-preservation"]);

/** "proven by failing tests" when every receipt behind the listed findings is a test run, etc. */
function proofPhrase(findingIds: string[], ctx?: HumanizeCtx): string {
  const kinds = new Set<string>();
  for (const id of findingIds) {
    const f = ctx?.findings?.find((x) => x.id === id);
    for (const eid of f?.evidence_ids ?? []) {
      const ev = ctx?.evidence?.find((e) => e.id === eid);
      if (ev) kinds.add(ev.kind);
    }
  }
  if (kinds.size === 0) return "backed by receipts";
  const all = (set: Set<string>) => [...kinds].every((k) => set.has(k));
  if (all(TEST_KINDS)) return "proven by failing tests";
  if (all(new Set(["mutation"]))) return "proven by surviving mutants";
  if (all(MIGRATION_KINDS)) return "proven by running the migration";
  if (all(new Set(["byte-diff"]))) return "proven by a byte diff";
  return "proven by receipts";
}

function describeNotGreen(ids: string[], ctx?: HumanizeCtx): string[] {
  if (!ctx?.evidence) return [`${plural(ids.length, "receipt")} not green`];
  const out: string[] = [];
  let noCompile = 0;
  for (const id of ids) {
    const ev = ctx.evidence.find((e) => e.id === id);
    if (!ev) {
      out.push(`Receipt ${id} not green`);
      continue;
    }
    if (ev.kind === "mutation") {
      const survived = num(ev.details?.survived) ?? parseSurvived(ev.title);
      out.push(survived !== undefined ? `${plural(survived, "mutant")} ${survived === 1 ? "survives" : "survive"} the suite` : "Mutants survive the suite");
      continue;
    }
    if (ev.result === "error" && /does not compile|compile/i.test(ev.title)) {
      noCompile += 1;
      continue;
    }
    if (ev.result === "error") {
      out.push(`${humanizeTitle(ev.title).text} couldn't run`);
      continue;
    }
    out.push(`Failed: ${humanizeTitle(ev.title).text}`);
  }
  if (noCompile) out.unshift(`${plural(noCompile, "test")} didn't compile`);
  return out;
}

function parseSurvived(title: string): number | undefined {
  const m = title.match(/(\d+) surviv/);
  return m ? Number(m[1]) : undefined;
}

function mapClause(clause: string, ctx?: HumanizeCtx): string[] | null {
  let m: RegExpMatchArray | null;
  if ((m = clause.match(/^max routed risk (\d+) >= (\d+)$/))) return [`High-risk code (${m[1]}/10)`];
  if ((m = clause.match(/^not every receipt is green \((.+)\)$/))) return describeNotGreen(m[1].split(/,\s*/), ctx);
  if ((m = clause.match(/^(\d+) unverified high-severity hunch\(es\)$/))) return [`${plural(Number(m[1]), "suspicion")} without proof`];
  if ((m = clause.match(/^area trust below 3: (.+)$/))) {
    const areas = m[1].split(/,\s*/).map((p) => p.split("=")[0]);
    return [`Trust not yet earned: ${areas.join(", ")}`];
  }
  if (/^no AI lens ran$/.test(clause)) return ["No AI lens ran"];
  if ((m = clause.match(/^risky area touched: (.+)$/))) return [`Never auto-ready area: ${m[1]}`];
  return null;
}

export function humanizeVerdict(verdict: VerdictKind, reason: string, ctx?: HumanizeCtx): HumanVerdict {
  const raw = reason.trim();
  const base = verdictHeadline[verdict] ?? verdict;
  let m: RegExpMatchArray | null;

  if ((m = raw.match(/^verified (high|critical) finding\(s\) backed by receipts: (.+)$/))) {
    const ids = m[2].split(/,\s*/);
    const n = ids.length;
    return { headline: `Blocked: ${plural(n, "defect")} ${proofPhrase(ids, ctx)}`, reasons: [], raw, mapped: true };
  }

  if ((m = raw.match(/^(\d+) test\(s\) fail on head \(passing on base, or added by the change\): (.+)$/))) {
    const n = Number(m[1]);
    return { headline: `Blocked: ${plural(n, "test")} ${n === 1 ? "fails" : "fail"} after the change`, reasons: [], raw, mapped: true };
  }

  if ((m = raw.match(/^the requirement has (\d+) plausible readings that lead to different code; a named human must choose$/))) {
    return { headline: verdictHeadline["decision-A-B"], reasons: ["Both readings were built and checked. The reviewer refuses to guess."], raw, mapped: true };
  }

  const clauses = raw.split(/;\s*/).filter(Boolean);
  const reasons: string[] = [];
  let hits = 0;
  for (const c of clauses) {
    const mapped = mapClause(c, ctx);
    if (mapped) {
      hits += 1;
      reasons.push(...mapped);
    } else reasons.push(c);
  }
  if (hits > 0) return { headline: base, reasons, raw, mapped: true };
  return { headline: base, reasons: [raw], raw, mapped: false };
}

/** The API's refusal string stays the tooltip; this is the one-line human reason next to a locked button. */
export function humanizeRefusal(verdict: VerdictKind): string | undefined {
  if (verdict === "blocked") return "Blocked: a proven defect must be fixed first";
  if (verdict === "decision-A-B") return "Needs an A or B decision, not a sign-off";
  return undefined;
}

/** Why an A/B page is shown for a change whose verdict is not decision-A-B. */
export function abMismatchMessage(verdict: VerdictKind, bothBlocked: boolean): string {
  if (verdict === "blocked" && bothBlocked) return "Both readings were built and checked, but neither can be merged yet: they share a proven defect.";
  if (verdict === "blocked") return "Both readings were built and checked, but the change is blocked by a proven defect, so neither can be merged yet.";
  return `This change was reviewed as "${verdictHeadline[verdict] ?? verdict}", not as an A/B decision. Both readings are shown anyway.`;
}

export interface HumanTitle {
  text: string;
  /** e.g. "4/4", "222/222 files" */
  count?: string;
  /** Secondary detail: a file path, project name, list of files. */
  note?: string;
}

/** Evidence titles and checked-item labels, as the pipeline writes them, into a short sentence + count + note. */
export function humanizeTitle(title: string): HumanTitle {
  const t = title.trim();
  let m: RegExpMatchArray | null;

  if ((m = t.match(/^Build (base|head) \((.+)\)$/))) return { text: `${m[1] === "base" ? "Base" : "Head"} builds`, note: m[2] };
  if ((m = t.match(/^Existing .*tests on (base|head): (\d+)\/(\d+) passed, (\d+) failed$/))) {
    const failed = Number(m[4]);
    return { text: failed ? `Existing tests fail on ${m[1]}` : `Existing tests pass on ${m[1]}`, count: `${m[2]}/${m[3]}` };
  }
  if ((m = t.match(/^Existing suite(?: on (base|head))?: (\d+)\/(\d+)$/))) return { text: `Existing tests pass on ${m[1] ?? "head"}`, count: `${m[2]}/${m[3]}` };
  if ((m = t.match(/^Compatibility bytes: (\d+)\/(\d+) cases differ, (\d+) base payloads fail to read on head, public API \+(\d+)\/-(\d+)$/))) {
    const differ = Number(m[1]);
    const total = Number(m[2]);
    const unreadable = Number(m[3]);
    const text = differ === 0 && unreadable === 0 ? "Serialized bytes identical on base and head" : "Serialized bytes differ between base and head";
    return { text, count: `${total - differ}/${total} cases`, note: `public API +${m[4]}/−${m[5]}` };
  }
  if (
    (m = t.match(/^Mutation: (\d+) killed by the change's tests, (\d+) caught only by the existing suite, (\d+) survive the full suite, (\d+) compile errors?, (\d+) timeouts? \((\d+) mutants on (.+)\)$/)) ||
    (m = t.match(/^Mutation: (\d+) killed, ()(\d+) survived, (\d+) compile errors?, (\d+) timeouts? \((\d+) mutants on (.+)\)$/))
  ) {
    const survived = Number(m[3]);
    const total = Number(m[6]);
    return {
      text: survived ? `${plural(survived, "mutant")} on changed lines ${survived === 1 ? "survives" : "survive"} the suite` : "Every mutant on changed lines is killed",
      count: `${total - survived}/${total} killed`,
      note: m[7],
    };
  }
  if ((m = t.match(/^Mutation not meaningful: (.+)$/))) return { text: "Mutation skipped", note: m[1] };
  if ((m = t.match(/^Regeneration: (\d+)\/(\d+) generated files verified by (\d+) passing snapshot tests on head(?:; (\d+) head test\(s\) fail)?$/))) {
    const fails = m[4] ? Number(m[4]) : 0;
    return {
      text: fails ? "Some generated files do not regenerate" : "Generated files regenerate identically",
      count: `${m[1]}/${m[2]} files`,
      note: `${m[3]} snapshot tests${fails ? `, ${plural(fails, "test")} failing` : ""}`,
    };
  }
  if ((m = t.match(/^(.+\.\w+): requirement test fails on base \((\d+)\/(\d+)\), passes on head \((\d+)\/(\d+)\)(?:; refutes (\d+) lens claim\(s\): (.+))?$/))) {
    return { text: "Requirement tests fail before, pass after", count: `${m[4]}/${m[5]}`, note: m[6] ? `${m[1]} · refutes ${m[7]}` : m[1] };
  }
  if ((m = t.match(/^(.+\.\w+): requirement test fails on head: (\d+)\/(\d+) failed$/))) return { text: "Requirement tests fail after the change", count: `${m[2]}/${m[3]} failing`, note: m[1] };
  if ((m = t.match(/^(.+\.\w+): edge-case test fails on head: (\d+)\/(\d+) failed$/))) return { text: "Edge-case tests fail after the change", count: `${m[2]}/${m[3]} failing`, note: m[1] };
  if ((m = t.match(/^(.+\.\w+): edge-case test passes on head \((\d+) tests?\): no defect shown(?:; refutes (\d+) lens claim\(s\): (.+))?$/))) {
    return { text: "Edge-case tests pass: no defect shown", count: `${m[2]}/${m[2]}`, note: m[3] ? `${m[1]} · refutes ${m[4]}` : m[1] };
  }
  if ((m = t.match(/^(.+\.\w+): (edge-case|requirement) test does not compile on head$/))) return { text: `${m[2] === "edge-case" ? "Edge-case" : "Requirement"} test didn't compile`, note: m[1] };
  if ((m = t.match(/^Migration not reversible: schema after down differs from base \((\d+) line\(s\)\)$/))) return { text: "Down migration doesn't restore the schema", count: `${plural(Number(m[1]), "line")} differ` };
  if ((m = t.match(/^No rows lost across the up migration \((.+)\)$/))) return { text: "No rows lost across the migration", note: m[1] };
  if ((m = t.match(/^Repo queries on the migrated schema: (\d+)\/(\d+) succeeded$/))) return { text: "Application queries run on the migrated schema", count: `${m[1]}/${m[2]}` };
  if ((m = t.match(/^sql-([\w-]+): (.+) exit (\d+) in ([\d.]+)s$/))) {
    const step: Record<string, string> = { start: "Database sandbox started", ready: "Database sandbox ready", port: "Database port mapped", teardown: "Database sandbox removed" };
    const text = step[m[1]] ?? (m[1].startsWith("base-up-") ? `Base migration applied: ${m[1].slice("base-up-".length)}` : `Sandbox step: ${m[1]}`);
    return { text, count: `${m[4]}s`, note: `${m[2]} · exit ${m[3]}` };
  }
  if ((m = t.match(/^routed (\d+) hunks across (\d+) files$/))) return { text: "Every hunk routed to a review depth", count: `${m[1]} hunks, ${m[2]} files` };
  if ((m = t.match(/^(requirement-tests|logic-edge|compatibility|security|product-context|test-quality) lens$/))) return { text: `${lensLabel[m[1]]} lens ran` };
  return { text: t };
}

const operatorWords: Record<string, string> = {
  "logical-operator": "an and/or swapped",
  "negate-condition": "a condition flipped",
  "remove-guard": "a safety check removed",
  "linq-first": "First swapped for FirstOrDefault",
  "off-by-one": "an off-by-one change",
  "swap-boolean": "true and false swapped",
  "delete-statement": "a line deleted",
};

/** Machine-written finding claims (mutation survivors) in plain words; prose claims pass through untouched. */
export function humanizeClaim(claim: string): string {
  const t = claim.trim();
  let m: RegExpMatchArray | null;
  if ((m = t.match(/^no test in the suite detects ([\w-]+) at (.+?):(\d+)$/))) {
    const file = m[2].split("/").pop() ?? m[2];
    return `No test would notice if this line were broken (${operatorWords[m[1]] ?? m[1]} in ${file}, line ${m[3]}).`;
  }
  if ((m = t.match(/^the change's own tests do not catch mutants at (.+?):([\d, ]+); the existing suite does$/))) {
    const file = m[1].split("/").pop() ?? m[1];
    return `The change's own tests miss breaks in ${file} (lines ${m[2]}); only the older tests catch them.`;
  }
  return t;
}

export const lensLabel: Record<string, string> = {
  "requirement-tests": "Requirement tests",
  "logic-edge": "Logic and edge cases",
  compatibility: "Compatibility",
  security: "Security",
  "product-context": "Product context",
  "test-quality": "Test quality",
};

export function stripHtmlComments(markdown: string): string {
  return markdown.replace(/<!--[\s\S]*?-->/g, "");
}
