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
const PIN_KINDS = new Set(["behaviour-changed", "behaviour-preserved"]);

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
  if (all(PIN_KINDS)) return "proven by pinned tests";
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
  if ((m = clause.match(/^modifies existing code used elsewhere \(blast radius\): (.+)$/))) {
    const hits = [...m[1].matchAll(/\((\d+) caller(?:s|\(s\))? outside the change\)/g)];
    const callers = hits.reduce((s, x) => s + Number(x[1]), 0);
    const functions = hits.length;
    return [`Changes ${plural(functions || 1, "function")} that ${plural(callers, "other place")} ${callers === 1 ? "uses" : "use"}`];
  }
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

/** "3 of 3 independent readers found two readings", parsed from a refusal; undefined when no refusal says so. */
export function readerVote(refused: { what: string; why: string }[]): { saw: number; of: number } | undefined {
  for (const r of refused) {
    const m = r.why.match(/(\d+) of (\d+) independent readers found two readings/);
    if (m) return { saw: Number(m[1]), of: Number(m[2]) };
  }
  return undefined;
}

/** The first sentence of a long interpretation summary; the rest is returned for a "More" disclosure. */
export function firstSentence(text: string): { lead: string; rest: string } {
  const t = text.trim();
  // A sentence ends at ". " unless the dot belongs to an abbreviation such as "e.g." or "i.e.".
  const re = /[.!?](?=\s|$)/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(t))) {
    const end = m.index + 1;
    if (end < 24) continue;
    if (/\b(e\.g|i\.e|etc|vs|cf)\.$/i.test(t.slice(0, end))) continue;
    return { lead: t.slice(0, end), rest: t.slice(end).trim() };
  }
  return { lead: t, rest: "" };
}

/** The requirement text a refusal quotes ("the requirement says: '...'"), if any. */
export function quotedRequirement(refused: { why: string }[]): string | undefined {
  for (const r of refused) {
    const m = r.why.match(/the requirement says:\s*'([^']+)'/i);
    if (m) return m[1].trim();
  }
  return undefined;
}

/** Why an A/B page is shown for a change whose verdict is not decision-A-B. */
export function abMismatchMessage(verdict: VerdictKind, bothBlocked: boolean): string {
  if (verdict === "blocked" && bothBlocked) return "Both readings were built and checked, but neither can be merged yet: they share a proven defect.";
  if (verdict === "blocked") return "Both readings were built and checked, but the change is blocked by a proven defect, so neither can be merged yet.";
  return `This change was reviewed as "${verdictHeadline[verdict] ?? verdict}", not as an A/B decision. Both readings are shown anyway.`;
}

/** "1.3 s" / "17 s" / "2 min 10 s" for a sandbox command duration. */
export function duration(seconds: number): string {
  if (seconds >= 90) return `${Math.floor(seconds / 60)} min ${Math.round(seconds % 60)} s`;
  if (seconds >= 10) return `${Math.round(seconds)} s`;
  return `${seconds.toFixed(1)} s`;
}

/** What a sandbox command (by its CommandReceipt id) did, in plain words. Unknown ids fall back to "Sandbox step: <id>". */
export function commandSubject(id: string, ok = true): string {
  let m: RegExpMatchArray | null;
  const side = (s: string) => (s === "base" ? "before the change" : "after the change");
  if (id === "impact") return "Found who calls the changed code";
  if (id === "coverage-head") return "Measured which callers tests reach";
  if ((m = id.match(/^build-(base|head)$/))) return `Built the code ${side(m[1])}`;
  if ((m = id.match(/^characterize-(base|head)$/))) return `Pinned the old behaviour ${side(m[1])}`;
  if ((m = id.match(/^test-(base|head)-existing$/))) return `Ran the existing tests ${side(m[1])}`;
  if ((m = id.match(/^gentest-(\d+)-(base|head)$/))) return `Ran written test ${m[1]} ${side(m[2])}`;
  if ((m = id.match(/^mutant-(\d+)-suite$/))) return `Ran the full suite against mutant ${m[1]}`;
  if ((m = id.match(/^mutant-(\d+)$/))) return `Ran the change's tests against mutant ${m[1]}`;
  if ((m = id.match(/^ab-(\w)-test-(\d+)$/))) return `Ran reading ${m[1]}'s tests (attempt ${m[2]})`;
  if ((m = id.match(/^compat-build-harness-(base|head)$/))) return `Built the compatibility harness ${side(m[1])}`;
  if ((m = id.match(/^compat-build-mp-(base|head)$/))) return `Built the runtime library ${side(m[1])}`;
  if ((m = id.match(/^compat-api-(base|head)$/))) return `Listed the public API ${side(m[1])}`;
  if ((m = id.match(/^compat-bytes-(base|head)$/))) return `Serialized the fixtures ${side(m[1])}`;
  if (id === "compat-roundtrip-head") return "Read old saved data after the change";
  if (id === "sql-start") return "Database sandbox started";
  if (id === "sql-ready") return "Database sandbox ready";
  if (id === "sql-port") return "Database port mapped";
  if (id === "sql-teardown") return "Database sandbox removed";
  if (id === "sql-query-report") return "Ran the app's queries on the migrated schema";
  if ((m = id.match(/^sql-snapshot-(base|up|down)$/))) return `Snapshot of the schema ${m[1] === "base" ? "before" : `after ${m[1]}`}`;
  if ((m = id.match(/^sql-base-up-(.+)$/))) return `Base migration applied: ${m[1]}`;
  if ((m = id.match(/^sql-(up|down)-(.+)$/))) return `Migration ${m[1]} applied: ${m[2]}`;
  return ok ? `Sandbox step: ${id}` : `Sandbox step failed: ${id}`;
}

export interface HumanGap {
  /** Short plain title, e.g. "Mutation testing". */
  title: string;
  /** One short plain reason, e.g. "none of the risky new lines were the kind it can break". */
  why: string;
  /** The pipeline's what and why, for the "More" disclosure; undefined when the plain text already says it all. */
  raw?: string;
}

const PROJECT_WORDS: [RegExp, string][] = [
  [/SourceGenerator\.Unity/, "Unity generator"],
  [/UnityShims/, "Unity shims"],
  [/SourceGenerator\.Tests/, "generator tests"],
  [/SourceGenerator/, "source generator"],
  [/Annotations/, "annotations"],
];

/** "Unity generator, CompositeResolverGenerator.cs:44" from a repo path. */
function placeName(loc: string): string {
  const [file, line] = loc.trim().split(":");
  const name = file.split("/").pop() ?? file;
  const project = PROJECT_WORDS.find(([re]) => re.test(file))?.[1] ?? file.split("/").slice(-2, -1)[0];
  return `${project ? `${project}, ` : ""}${name}${line ? `:${line}` : ""}`;
}

function capitalize(s: string): string {
  return s ? s[0].toUpperCase() + s.slice(1) : s;
}

function lensName(id: string): string {
  return lensLabel[id] ?? capitalize(id.replace(/-/g, " "));
}

/** The pipeline's not-checked / refused entries as a short plain title and reason; the raw text stays behind "More". */
export function humanizeGap(item: { what: string; why: string }): HumanGap {
  const what = item.what.trim();
  const why = item.why.trim();
  const raw = `${what} — ${why}`;
  let m: RegExpMatchArray | null;

  if ((m = what.match(/^(\d+) callers? of (\S+) (?:are|is) not exercised by any test \(legacy coverage gap\): (.+)$/))) {
    const n = Number(m[1]);
    const places = [...new Set(m[3].split(/,\s*/).map(placeName))];
    const shown = places.slice(0, 3).join("; ") + (places.length > 3 ? ` and ${places.length - 3} more` : "");
    return { title: `${plural(n, "place")} that call${n === 1 ? "s" : ""} ${m[2]} ${n === 1 ? "has" : "have"} no test`, why: shown, raw };
  }
  if (/^semantic call graph of modified members$/.test(what)) return { title: "Exact call graph", why: "callers are found by name, so shared names may over-count and framework or reflection callers are missed", raw };
  if (/^test coverage of callers \(legacy coverage gap\)$/.test(what)) return { title: "Which callers are tested", why: "coverage could not be collected, so every caller counts as unknown", raw };
  if (/^blast radius/.test(what)) return { title: "Who calls the changed code", why: why.replace(/^the impact analysis did not run$/, "the impact analysis did not run"), raw };
  if (/^mutation testing$/i.test(what)) {
    if ((m = why.match(/^(\d+) source hunk\(s\) routed at risk >= \d+, but none has an added line the mutation operators apply to$/))) {
      return { title: "Mutation testing", why: `none of the ${m[1]} risky changed blocks adds a line it can break`, raw };
    }
    if (/^no changed source hunk routed at risk/.test(why)) return { title: "Mutation testing", why: "no changed code was risky enough to need it", raw };
    if (/^head does not build$/.test(why)) return { title: "Mutation testing", why: "the changed code does not build", raw };
    return { title: "Mutation testing", why: firstClause(why), raw };
  }
  if ((m = what.match(/^([\w-]+) lens$/))) {
    const title = `${lensName(m[1])} reading`;
    if (/^not run: deterministic adapter demo/.test(why)) return { title, why: "not run: no AI pass on database changes in this build", raw };
    if (/connection error|timed out|disconnected/i.test(why)) return { title, why: "the AI provider did not answer", raw };
    if (/output invalid/i.test(why)) return { title, why: "the AI's answer was malformed and could not be repaired", raw };
    if (/^unavailable/i.test(why)) return { title, why: "the AI provider was unavailable", raw };
    return { title, why: firstClause(why), raw };
  }
  if ((m = what.match(/^tests\/([\w.]+) \(changed by this diff\)$/))) {
    const title = `${m[1].replace(/^MessagePack\./, "")} test project (also changed here)`;
    if (/^not built or run: only .* in sandbox scope$/.test(why)) return { title, why: "only the source generator tests run in the sandbox", raw };
    if (/does not compile in this sandbox even on base/.test(why)) return { title, why: "it does not compile in the sandbox, even before the change", raw };
    return { title, why: firstClause(why), raw };
  }
  if (/^tests\/MessagePack\.Tests and other runtime test suites$/.test(what)) return { title: "The runtime test suites", why: "only the source generator tests run in the sandbox", raw };
  if (/^NativeAOT, Unity and non-net8\.0 target frameworks$/.test(what)) return { title: "Other build targets (NativeAOT, Unity, older .NET)", why: "the sandbox builds and tests .NET 8 only", raw };
  if (/^compatibility byte diff/.test(what)) {
    const title = "Whether saved data still reads the same";
    if (/byte diff not needed/.test(why)) return { title, why: "nothing in the runtime library changed, so there is nothing to compare", raw };
    if (/none in src\/MessagePack/.test(why)) return { title, why: "the changed code is outside the runtime library the comparison covers", raw };
    return { title, why: firstClause(why), raw };
  }
  if (/^generated formatter bytes/.test(what)) return { title: "The bytes of the generated code", why: "the comparison covers the runtime library, not generated code", raw };
  if (/^requirement tests \(fail on base, pass on head\)$/.test(what)) return { title: "Tests written from the requirement", why: "none were generated and run", raw };
  if (/^A\/B review branches$/.test(what)) return { title: "Building both readings", why: "the two readings were recorded but not built in this run", raw };
  if ((m = what.match(/^reading (\w) branch and its receipts$/))) {
    const attempts = why.match(/after (\d+) attempt/);
    return { title: `Reading ${m[1]}`, why: `it did not build or pass tests${attempts ? ` after ${plural(Number(attempts[1]), "attempt")}` : ""}`, raw };
  }
  if ((m = why.match(/(\d+) of (\d+) independent readers found two readings/))) {
    return { title: capitalize(what), why: `${m[1]} of ${m[2]} independent readers saw two readings, so the AI will not pick`, raw };
  }
  const plainWhy = firstClause(why).replace(/\s*\([^)]*\)/g, "");
  return { title: capitalize(what.replace(/\s*\([^)]*\)/g, "")), why: plainWhy, raw: plainWhy === why && !/\(/.test(what) ? undefined : raw };
}

/** The first sentence or semicolon clause of a long pipeline reason. */
function firstClause(s: string): string {
  const m = s.match(/^(.+?)(?:;|\.\s|$)/);
  return (m ? m[1] : s).trim();
}

export interface HumanTitle {
  text: string;
  /** e.g. "4/4", "222/222 files" */
  count?: string;
  /** Secondary detail: a file path, project name, list of files. */
  note?: string;
  /** The command line behind a sandbox step: tooltip and technical drawer only, never printed on the receipt. */
  raw?: string;
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
  if ((m = t.match(/^(.+\.\w+): requirement test does not discriminate: passes before the change \((\d+) tests? pass on base and head\); weak receipt$/))) {
    return { text: "Requirement tests already passed before the change (weak proof)", count: `${m[2]}/${m[2]}`, note: m[1] };
  }
  if ((m = t.match(/^(.+?): (\d+) characterization test\(?s?\)? pass on base and head$/))) return { text: `Old behaviour of ${m[1]} still holds`, count: `${m[2]}/${m[2]} pinned tests` };
  if ((m = t.match(/^Down migration restores the base schema exactly \((\d+) schema objects? compared\)$/))) return { text: "Down migration restores the schema", count: `${m[1]} objects` };
  if ((m = t.match(/^No rows lost across the up migration \((.+)\)$/))) return { text: "No rows lost across the migration", note: m[1] };
  if ((m = t.match(/^Values lost in rename: (\S+) -> (\S+) \(.+\)$/))) return { text: "Values lost in the column rename", note: `${m[1]} → ${m[2]}` };
  if ((m = t.match(/^Reading (\w) built as (\S+) \((\w+)\): builds and the .* tests pass \(attempt (\d+) of (\d+)\)$/))) {
    return { text: `Reading ${m[1]} builds and passes the tests`, note: `attempt ${m[4]} of ${m[5]} · ${m[2]}` };
  }
  if ((m = t.match(/^Reading (\w) could not be built as a working branch after (\d+) attempt\(s\)$/))) return { text: `Reading ${m[1]} could not be built`, count: `${plural(Number(m[2]), "attempt")}` };
  if ((m = t.match(/^([\w.@-]+): (\d+)\/(\d+) passed, (\d+) failed \((base|head)\)$/))) {
    const failed = Number(m[4]);
    return { text: `${commandSubject(m[1])}: ${failed ? "some fail" : "all pass"}`, count: `${m[2]}/${m[3]}`, note: m[1] };
  }
  if ((m = t.match(/^([\w.@-]+): tests did not compile$/))) return { text: `${commandSubject(m[1])} didn't compile`, note: m[1] };
  if ((m = t.match(/^Migration not reversible: schema after down differs from base \((\d+) line\(s\)\)$/))) return { text: "Down migration doesn't restore the schema", count: `${plural(Number(m[1]), "line")} differ` };
  if ((m = t.match(/^No rows lost across the up migration \((.+)\)$/))) return { text: "No rows lost across the migration", note: m[1] };
  if ((m = t.match(/^Repo queries on the migrated schema: (\d+)\/(\d+) succeeded$/))) return { text: "Application queries run on the migrated schema", count: `${m[1]}/${m[2]}` };
  if ((m = t.match(/^([\w.@-]+): (.+) exit (\d+) in ([\d.]+)s$/))) {
    return { text: commandSubject(m[1], Number(m[3]) === 0), count: duration(Number(m[4])), raw: `${m[1]}: ${m[2]} · exit ${m[3]}` };
  }
  if ((m = t.match(/^Characterization: (\S+) behaviour preserved \((\d+)\/(\d+) pinned tests? pass on base and head\)$/))) return { text: `Old behaviour of ${m[1]} still holds`, count: `${m[2]}/${m[3]} pinned tests` };
  if ((m = t.match(/^Characterization: (\S+) behaviour changed \((\d+)\/(\d+) pinned tests? pass on base, (\d+) fails? on head\)$/))) return { text: `Old behaviour of ${m[1]} changed`, count: `${m[4]}/${m[3]} pinned tests fail` };
  if ((m = t.match(/^routed (\d+) hunks across (\d+) files$/))) return { text: "Every changed block routed to a review depth", count: `${m[1]} blocks, ${m[2]} files` };
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
  if ((m = t.match(/^behaviour of (\S+) changed for (\d+) callers? outside the change/))) {
    const n = Number(m[2]);
    return `Changes how ${m[1]} behaves for ${n} other ${n === 1 ? "place that relies" : "places that rely"} on it.`;
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
