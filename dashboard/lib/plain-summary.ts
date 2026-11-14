// "In plain words": 3-5 short sentences a non-engineer can read in 30 seconds, derived only from the
// receipt and its evidence. Pure, deterministic, no LLM. Every sentence anchors to the section that
// holds the evidence it summarises.

import { provenLines } from "./proven";
import type { Decision, Evidence, Finding, NotChecked, RunBundle, VerdictKind } from "./types";

export interface PlainSentence {
  key: "purpose" | "proved" | "wrong" | "unchecked" | "decide";
  text: string;
  href: string;
}

export interface PlainSummaryOpts {
  decision?: Decision;
  pendingQuestions?: number;
}

function plural(n: number, one: string, many = `${one}s`): string {
  return `${n} ${n === 1 ? one : many}`;
}

function num(v: unknown): number | undefined {
  return typeof v === "number" ? v : undefined;
}

function list(items: string[]): string {
  if (items.length <= 1) return items.join("");
  if (items.length === 2) return `${items[0]} and ${items[1]}`;
  return `${items.slice(0, -1).join(", ")} and ${items[items.length - 1]}`;
}

function endWithPeriod(s: string): string {
  return /[.!?]$/.test(s) ? s : `${s}.`;
}

// Purpose -----------------------------------------------------------------------

export function purposeSentence(requirementTitle: string): string {
  let t = requirementTitle.trim().replace(/^(feature request|bug|fix|issue)\s*:\s*/i, "").replace(/[.\s]+$/, "");
  if (t && /^[A-Z][a-z]/.test(t) && !/^[A-Z][a-z]*[A-Z]/.test(t)) t = t[0].toLowerCase() + t.slice(1);
  return t ? `This change is about: ${t}.` : "This change has no requirement title.";
}

// Proved -----------------------------------------------------------------------

/** Tests the pipeline wrote: last "N/M" in the title, else the length of details.tests / test_names. */
function writtenTestCount(ev: Evidence): number | undefined {
  const m = [...ev.title.matchAll(/(\d+)\/(\d+)/g)].pop();
  if (m) return Number(m[2]);
  const d = ev.details ?? {};
  if (Array.isArray(d.tests)) return d.tests.length;
  if (Array.isArray(d.test_names)) return d.test_names.length;
  const single = ev.title.match(/\((\d+) tests?\)/);
  return single ? Number(single[1]) : undefined;
}

function expects(ev: Evidence): string {
  return typeof ev.details?.expects === "string" ? ev.details.expects : "";
}

function describeProved(evidence: Evidence[]): string[] {
  const passed = evidence.filter((e) => e.result === "pass");
  const out: string[] = [];

  const reqTests = passed.filter((e) => e.kind === "fail-before-pass-after" && expects(e) !== "fail-on-head" && expects(e) !== "new-api");
  if (reqTests.length) {
    const n = reqTests.reduce((s, e) => s + (writtenTestCount(e) ?? 0), 0);
    const count = n > 0 ? plural(n, "test") : "tests";
    out.push(`We wrote ${count} from the issue description alone. They failed before the change and pass after it, so the change does what the issue asks.`);
  }
  const newApi = passed.filter((e) => e.kind === "fail-before-pass-after" && expects(e) === "new-api").concat(passed.filter((e) => e.kind === "new-api"));
  if (newApi.length) {
    const n = newApi.reduce((s, e) => s + (writtenTestCount(e) ?? 0), 0);
    out.push(`We wrote ${n > 0 ? plural(n, "test") : "tests"} for the new behaviour and they pass; that is weaker proof, since there was nothing to fail before the change.`);
  }
  const edge = passed.filter((e) => e.kind === "fail-before-pass-after" && expects(e) === "fail-on-head");
  if (edge.length) {
    const n = edge.reduce((s, e) => s + (writtenTestCount(e) ?? 0), 0);
    out.push(`${n > 0 ? plural(n, "extra test") : "Extra tests"} for tricky cases pass too, so no defect was shown there.`);
  }
  for (const e of passed.filter((x) => x.kind === "regeneration")) {
    const d = e.details ?? {};
    const total = num(d.generated_files) ?? num(d.generated_files_count);
    out.push(`${total !== undefined ? `${total} generated files` : "The generated files"} were rebuilt from their templates and came out identical, so nobody had to read them by hand.`);
  }
  for (const e of passed.filter((x) => x.kind === "byte-diff")) {
    const d = e.details ?? {};
    const total = num(d.cases) ?? num(d.fixtures);
    out.push(`Saved data reads exactly the same before and after the change${total !== undefined ? ` (${plural(total, "case")} compared)` : ""}.`);
  }
  if (passed.some((x) => x.kind === "migration-up-down")) out.push("The database change can be undone cleanly.");
  for (const e of passed.filter((x) => x.kind === "row-preservation")) {
    const d = e.details ?? {};
    out.push(Array.isArray(d.value_checksums) ? "The values in the renamed columns survived the change." : "No database rows were lost.");
  }
  const existing = passed.filter((x) => x.kind === "existing-tests");
  if (existing.length) {
    const head = existing.find((e) => /head/i.test(e.title) || !/base/i.test(e.title)) ?? existing[0];
    if (/quer/i.test(head.title)) out.push("The app's own database queries still work on the changed database.");
    else {
      const total = num(head.details?.total) ?? writtenTestCount(head);
      out.push(total !== undefined ? `The ${plural(total, "test")} the project already had still ${total === 1 ? "passes" : "pass"}.` : "The tests the project already had still pass.");
    }
  }
  return out;
}

export function provedSentence(evidence: Evidence[]): string {
  const facts = describeProved(evidence);
  if (facts.length === 0) return "Nothing was proven in the change's favour: no test, diff or migration check passed.";
  return facts.slice(0, 2).join(" ");
}

// Wrong or weak -------------------------------------------------------------------

function failingTestCount(ev: Evidence): number | undefined {
  const m = ev.title.match(/(\d+)\/(\d+) failed/);
  return m ? Number(m[1]) : undefined;
}

function describeWrong(findings: Finding[], evidence: Evidence[]): string[] {
  const out: string[] = [];
  const byId = new Map(evidence.map((e) => [e.id, e]));
  const cited = new Set<string>();
  const kinds = new Map<Evidence["kind"], Evidence[]>();
  for (const f of findings) {
    for (const id of f.evidence_ids ?? []) {
      if (cited.has(id)) continue;
      const ev = byId.get(id);
      if (!ev) continue;
      cited.add(id);
      kinds.set(ev.kind, [...(kinds.get(ev.kind) ?? []), ev]);
    }
  }

  const tests = [...(kinds.get("fail-before-pass-after") ?? []), ...(kinds.get("new-api") ?? []), ...(kinds.get("existing-tests") ?? [])];
  if (tests.length) {
    const failing = tests.reduce((s, e) => s + (failingTestCount(e) ?? 0), 0);
    const files = tests.length;
    out.push(
      failing > 0
        ? `${plural(failing, "test")} we wrote from the requirement ${failing === 1 ? "fails" : "fail"} on the change, so it does not fully do what was asked.`
        : `Tests we wrote from the requirement fail on the change${files > 1 ? ` (${files} test files)` : ""}, so it does not fully do what was asked.`,
    );
  }

  const mutation = kinds.get("mutation") ?? [];
  if (mutation.length) {
    const survived = mutation.reduce((s, e) => s + (num(e.details?.survived) ?? 0), 0);
    const n = survived > 0 ? survived : findings.filter((f) => f.evidence_ids?.some((id) => byId.get(id)?.kind === "mutation")).length;
    out.push(`${plural(n, "part")} of the new code ${n === 1 ? "is" : "are"} never tested: if someone broke ${n === 1 ? "it" : "them"}, no test would notice.`);
  }

  if (kinds.has("byte-diff")) out.push("Old saved data would read differently after this change.");
  if (kinds.has("regeneration")) out.push("The generated code no longer matches its templates.");
  if (kinds.has("migration-up-down")) out.push("The database change cannot be undone: rolling it back leaves the database different from before.");
  if (kinds.has("row-preservation")) out.push("Data would be lost: values come out different after the change.");

  const known = new Set<Evidence["kind"]>(["fail-before-pass-after", "new-api", "existing-tests", "mutation", "byte-diff", "regeneration", "migration-up-down", "row-preservation"]);
  const other = findings.filter((f) => !(f.evidence_ids ?? []).some((id) => known.has(byId.get(id)?.kind as Evidence["kind"])));
  if (other.length) out.push(`${plural(other.length, "other problem")} ${other.length === 1 ? "was" : "were"} proven.`);

  return out;
}

export function wrongSentence(findings: Finding[], hunches: Finding[], evidence: Evidence[]): string {
  const problems = describeWrong(findings, evidence);
  const broken = evidence.filter((e) => e.kind === "fail-before-pass-after" && e.result === "error");
  const parts: string[] = [];
  if (problems.length) parts.push(problems.slice(0, 2).join(" "));
  else parts.push("Nothing was proven wrong.");
  if (broken.length) parts.push(`${plural(broken.length, "extra test")} we wrote did not compile, so ${broken.length === 1 ? "it proves" : "they prove"} nothing either way.`);
  if (hunches.length) {
    parts.push(
      `The reviewer also has ${plural(hunches.length, "suspicion")} it could not prove; ${hunches.length === 1 ? "it is listed as a hunch" : "they are listed as hunches"} and did not count against the change.`,
    );
  }
  return parts.join(" ");
}

// Not checked ----------------------------------------------------------------------

/** Pipeline not-checked labels, in everyday words. Unknown labels keep their text minus any parenthetical. */
const UNCHECKED_WORDS: [RegExp, string][] = [
  [/^compatibility byte diff/i, "whether saved data still reads the same"],
  [/^generated formatter bytes/i, "the bytes of the generated code"],
  [/^mutation testing/i, "planting small breaks to see if the tests notice"],
  [/other runtime test suites/i, "the app's other test suites"],
  [/NativeAOT, Unity/i, "builds for mobile, Unity and older .NET targets"],
  [/^tests\/[\w.]+ \(changed by this diff\)/i, "a test project this change also touches"],
];

function shortWhat(what: string): string {
  for (const [re, words] of UNCHECKED_WORDS) if (re.test(what.trim())) return words;
  return what.replace(/\s*\([^)]*\)\s*/g, " ").replace(/\s+/g, " ").trim();
}

export function uncheckedSentence(notChecked: NotChecked[]): string {
  if (notChecked.length === 0) return "The run declared nothing as unchecked. Treat that with suspicion: every review has gaps.";
  const lenses = notChecked.filter((n) => /\blens$/i.test(n.what.trim()));
  const rest = notChecked.filter((n) => !lenses.includes(n));
  const items = rest.slice(0, 2).map((n) => shortWhat(n.what));
  if (lenses.length) items.push("the AI reading passes");
  const total = notChecked.length;
  const named = Math.min(rest.length, 2) + (lenses.length ? lenses.length : 0);
  const lead = total === named ? `We did not check ${list(items)}.` : `We did not check ${plural(total, "thing")}, including ${list(items)}.`;
  return endWithPeriod(lead);
}

// Decide -----------------------------------------------------------------------

export function decideSentence(verdict: VerdictKind, opts: PlainSummaryOpts = {}): string {
  if (opts.decision) {
    const d = opts.decision;
    return d.choice === "sign-off" ? `Decided: ${d.who} signed this off on ${d.ts.slice(0, 10)}.` : `Decided: ${d.who} picked reading ${d.choice} on ${d.ts.slice(0, 10)}.`;
  }
  const questions = opts.pendingQuestions ? ` ${plural(opts.pendingQuestions, "question")} for you ${opts.pendingQuestions === 1 ? "is" : "are"} waiting.` : "";
  switch (verdict) {
    case "blocked":
      return "Blocked: it has to be fixed before anyone can approve it." + questions;
    case "decision-A-B":
      return "Your call: the requirement can be read two ways, so a named person picks reading A or B." + questions;
    case "auto-ready":
      return "Ready: every check passed in an area that has earned trust, so no sign-off is needed.";
    default:
      return "Your call: accept it as it is, or send it back for the missing tests." + questions;
  }
}

// Together ----------------------------------------------------------------------

export function plainSummary(run: RunBundle, opts: PlainSummaryOpts = {}): PlainSentence[] {
  const r = run.receipt;
  const decideHref = r.verdict === "decision-A-B" && !opts.decision ? `/decision/${encodeURIComponent(run.changeId)}` : "#trust-receipt";
  return [
    { key: "purpose", text: purposeSentence(r.requirement_title), href: "#requirement" },
    { key: "proved", text: provedSentence(run.evidence), href: provenLines(run.evidence).length ? "#proven" : "#receipts" },
    { key: "wrong", text: wrongSentence(r.findings, r.hunches, run.evidence), href: "#findings" },
    { key: "unchecked", text: uncheckedSentence(r.not_checked), href: "#not-checked" },
    { key: "decide", text: decideSentence(r.verdict, opts), href: decideHref },
  ];
}
