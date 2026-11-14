import fs from "node:fs";
import path from "node:path";
import { dirs, isSafeId, safeJoin } from "./paths";
import type {
  CommandReceipt,
  ContextStatus,
  Decision,
  Evidence,
  Finding,
  IntakeReport,
  LedgerEntry,
  MetricsSummary,
  MetricsTable,
  MetricsTableName,
  Mutant,
  Question,
  RoutingReport,
  RunBundle,
  TestRun,
  TrustHistoryEvent,
  TrustLevels,
  TrustReceipt,
} from "./types";

function readJson<T>(file: string): T | undefined {
  try {
    return JSON.parse(fs.readFileSync(file, "utf8")) as T;
  } catch {
    return undefined;
  }
}

function readJsonList<T>(file: string): T[] {
  const v = readJson<unknown>(file);
  return Array.isArray(v) ? (v as T[]) : [];
}

// Runs -----------------------------------------------------------------------

/** Every change id that has a `latest` marker, found by walking runs/ recursively. */
export function listChangeIds(): string[] {
  const root = dirs.runs();
  if (!fs.existsSync(root)) return [];
  const out: string[] = [];
  const walk = (dir: string, depth: number) => {
    if (depth > 6) return;
    let entries: fs.Dirent[];
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
      return;
    }
    if (entries.some((e) => e.isFile() && e.name === "latest")) {
      out.push(path.relative(root, dir).split(path.sep).join("/"));
      return;
    }
    for (const e of entries) {
      if (e.isDirectory()) walk(path.join(dir, e.name), depth + 1);
    }
  };
  walk(root, 0);
  return out.sort();
}

export function latestRunTs(changeId: string): string | undefined {
  if (!isSafeId(changeId)) return undefined;
  const f = safeJoin(dirs.runs(), path.join(changeId, "latest"));
  if (!f || !fs.existsSync(f)) return undefined;
  const ts = fs.readFileSync(f, "utf8").trim();
  return ts && isSafeId(ts) && !ts.includes("/") ? ts : undefined;
}

export function runDir(changeId: string, runTs: string): string | undefined {
  if (!isSafeId(changeId) || !isSafeId(runTs) || runTs.includes("/")) return undefined;
  const d = safeJoin(dirs.runs(), path.join(changeId, runTs));
  return d && fs.existsSync(d) ? d : undefined;
}

export function loadRun(changeId: string, runTs?: string): RunBundle | undefined {
  const ts = runTs ?? latestRunTs(changeId);
  if (!ts) return undefined;
  const dir = runDir(changeId, ts);
  if (!dir) return undefined;
  const receipt = readJson<TrustReceipt>(path.join(dir, "receipt.json"));
  if (!receipt) return undefined;
  return {
    changeId,
    runTs: ts,
    receipt,
    intake: readJson<IntakeReport>(path.join(dir, "intake.json")),
    routing: readJson<RoutingReport>(path.join(dir, "routing.json")),
    commands: readJsonList<CommandReceipt>(path.join(dir, "commands.json")),
    tests: readJsonList<TestRun>(path.join(dir, "tests.json")),
    evidence: readJsonList<Evidence>(path.join(dir, "evidence.json")),
    findings: readJsonList<Finding>(path.join(dir, "findings.json")),
    mutants: readJsonList<Mutant>(path.join(dir, "mutants.json")),
    questions: readJsonList<Question>(path.join(dir, "questions.json")),
  };
}

export function loadLatestRuns(): RunBundle[] {
  return listChangeIds()
    .map((id) => loadRun(id))
    .filter((r): r is RunBundle => r !== undefined);
}

/** Read a command log (path relative to the run dir), tail-limited. */
export function readLog(
  changeId: string,
  runTs: string,
  logPath: string,
  maxLines = 200,
): { text: string; truncated: boolean; totalLines: number } | undefined {
  const dir = runDir(changeId, runTs);
  if (!dir) return undefined;
  const f = safeJoin(dir, logPath);
  if (!f || !fs.existsSync(f)) return undefined;
  const lines = fs.readFileSync(f, "utf8").split("\n");
  const truncated = lines.length > maxLines;
  return {
    text: (truncated ? lines.slice(-maxLines) : lines).join("\n"),
    truncated,
    totalLines: lines.length,
  };
}

// Decisions ------------------------------------------------------------------

export function decisionFile(changeId: string): string | undefined {
  if (!isSafeId(changeId)) return undefined;
  return safeJoin(dirs.decisions(), changeId + ".json") ?? undefined;
}

export function readDecision(changeId: string): Decision | undefined {
  const f = decisionFile(changeId);
  return f ? readJson<Decision>(f) : undefined;
}

export function writeDecision(d: Decision): void {
  const f = decisionFile(d.change_id);
  if (!f) throw new Error("bad change id");
  fs.mkdirSync(path.dirname(f), { recursive: true });
  // "wx" refuses to overwrite: one decision per change, even under a double click.
  fs.writeFileSync(f, JSON.stringify(d, null, 2) + "\n", { flag: "wx" });
}

/** Why a sign-off is not allowed for this verdict, or undefined when it is. */
export function signOffRefusal(receipt: TrustReceipt): string | undefined {
  if (receipt.verdict === "blocked") return `Sign-off refused: the change is blocked (${receipt.verdict_reason}).`;
  if (receipt.verdict === "decision-A-B") return "Sign-off refused: this change needs an A/B decision, not a sign-off.";
  return undefined;
}

// Context + ledger -----------------------------------------------------------

export function readLedger(): LedgerEntry[] {
  const f = path.join(dirs.context(), "ledger.jsonl");
  if (!fs.existsSync(f)) return [];
  return fs
    .readFileSync(f, "utf8")
    .split("\n")
    .filter((l) => l.trim())
    .flatMap((l) => {
      try {
        return [JSON.parse(l) as LedgerEntry];
      } catch {
        return [];
      }
    });
}

export function appendLedger(entry: LedgerEntry): void {
  const dir = dirs.context();
  fs.mkdirSync(dir, { recursive: true });
  fs.appendFileSync(path.join(dir, "ledger.jsonl"), JSON.stringify(entry) + "\n");
}

export function readContextMarkdown(): string | undefined {
  const f = path.join(dirs.context(), "REVIEW_CONTEXT.md");
  return fs.existsSync(f) ? fs.readFileSync(f, "utf8") : undefined;
}

export function readContextStatus(): ContextStatus {
  return readJson<ContextStatus>(path.join(dirs.context(), "status.json")) ?? { status: "draft" };
}

export function writeContextStatus(s: ContextStatus): void {
  const dir = dirs.context();
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, "status.json"), JSON.stringify(s, null, 2) + "\n");
}

/** Pending = question ids from all latest runs with no ledger entry. */
export function pendingQuestions(runs?: RunBundle[]): Question[] {
  const answered = new Set(readLedger().map((e) => e.question_id).filter(Boolean));
  const all = (runs ?? loadLatestRuns()).flatMap((r) => r.questions);
  const seen = new Set<string>();
  return all.filter((q) => {
    if (answered.has(q.id) || seen.has(q.id)) return false;
    seen.add(q.id);
    return true;
  });
}

// Metrics --------------------------------------------------------------------

export function readMetricsTable(name: MetricsTableName): MetricsTable | undefined {
  const t = readJson<MetricsTable>(path.join(dirs.metrics(), `${name}.json`));
  if (!t || !Array.isArray(t.columns) || !Array.isArray(t.rows)) return undefined;
  return t;
}

export function readTrustLevels(): TrustLevels | undefined {
  const t = readJson<TrustLevels>(path.join(dirs.metrics(), "trust.json"));
  return t && typeof t === "object" && !Array.isArray(t) ? t : undefined;
}

export function readMetricsSummary(): MetricsSummary | undefined {
  const s = readJson<MetricsSummary>(path.join(dirs.metrics(), "summary.json"));
  return s && typeof s === "object" && !Array.isArray(s) ? s : undefined;
}

export function readTrustHistory(): TrustHistoryEvent[] {
  return readJsonList<TrustHistoryEvent>(path.join(dirs.metrics(), "trust-history.json")).filter(
    (e) => typeof e.seq === "number" && typeof e.area === "string" && typeof e.level === "number",
  );
}

// Derived --------------------------------------------------------------------

export function receiptCounts(evidence: Evidence[]): { green: number; red: number; other: number } {
  let green = 0;
  let red = 0;
  let other = 0;
  for (const e of evidence) {
    if (e.result === "pass") green += 1;
    else if (e.result === "fail" || e.result === "error") red += 1;
    else other += 1;
  }
  return { green, red, other };
}

export function minTrust(areaTrust?: Record<string, number>): number | undefined {
  const vals = Object.values(areaTrust ?? {});
  return vals.length ? Math.min(...vals) : undefined;
}

export const AB_SUFFIX = /__(A|B)$/;

export function isSampleChange(id: string): boolean {
  return id.startsWith("ju26-issue-");
}
