// Mirror of pipeline/receipts/models.py. Change both together.

export type FileKind = "source" | "test" | "docs" | "generated" | "template";
export type Category = "correctness" | "security" | "compatibility" | "tests" | "docs" | "generated";
export type LensName =
  | "requirement-tests"
  | "logic-edge"
  | "compatibility"
  | "security"
  | "product-context"
  | "test-quality";
export type Severity = "low" | "medium" | "high" | "critical";
export type VerdictKind = "auto-ready" | "needs-sign-off" | "decision-A-B" | "blocked";
export type Depth = "regeneration" | "light" | "deep";
export type ResultKind = "pass" | "fail" | "partial" | "error" | "skipped";

export interface Hunk {
  id: string;
  file: string;
  kind: FileKind;
  old_start: number;
  old_lines: number;
  new_start: number;
  new_lines: number;
  header?: string;
  text: string;
}

export interface FileChange {
  path: string;
  kind: FileKind;
  status: "added" | "modified" | "deleted" | "renamed";
  additions: number;
  deletions: number;
  area: string;
}

export interface IntakeReport {
  change_id: string;
  requirement_id: string;
  base: string;
  head: string;
  head_sha: string;
  files: FileChange[];
  hunks: Hunk[];
  counts: Record<string, number>;
  summary: string;
}

export interface RouteResult {
  hunk_id: string;
  risk: number;
  category: Category;
  confidence: number;
  router: string;
  cost_tokens?: number;
  ms?: number;
  floor_applied?: boolean;
  reason?: string;
}

export interface RoutingReport {
  router: string;
  routes: RouteResult[];
  depth_by_file: Record<string, Depth>;
  total_tokens?: number;
  total_ms?: number;
  cost_usd?: number;
}

export interface CommandReceipt {
  id: string;
  cmd: string[];
  cwd: string;
  exit_code: number;
  duration_s: number;
  log_path: string;
  started_at?: string;
}

export interface TestCase {
  name: string;
  outcome: "passed" | "failed" | "skipped" | "notexecuted";
  duration_s?: number;
  message?: string;
}

export interface TestRun {
  receipt_id: string;
  side: "base" | "head";
  filter?: string;
  compiled: boolean;
  total?: number;
  passed?: number;
  failed?: number;
  skipped?: number;
  cases?: TestCase[];
}

export interface ReceiptPlan {
  kind: "test" | "mutation" | "byte-diff" | "regeneration" | "ledger-citation" | "code-citation";
  detail: string;
}

export interface Finding {
  id: string;
  lens: LensName;
  claim: string;
  location: string;
  severity: Severity;
  receipt_plan: ReceiptPlan;
  verified?: boolean;
  evidence_ids?: string[];
  verdict_depends_on_intent?: boolean;
}

export type EvidenceKind =
  | "fail-before-pass-after"
  | "new-api"
  | "mutation"
  | "regeneration"
  | "byte-diff"
  | "existing-tests"
  | "build"
  | "ledger-citation"
  | "code-citation"
  | "migration-up-down"
  | "row-preservation"
  | "behaviour-preserved"
  | "behaviour-changed";

export interface Evidence {
  id: string;
  kind: EvidenceKind;
  title: string;
  result: ResultKind;
  command_ids?: string[];
  details?: Record<string, unknown>;
  excerpt?: string;
}

export interface Mutant {
  id: string;
  file: string;
  line: number;
  operator:
    | "logical-operator"
    | "negate-condition"
    | "remove-guard"
    | "linq-first"
    | "off-by-one"
    | "swap-boolean"
    | "delete-statement";
  original: string;
  mutated: string;
  status: "killed" | "killed-by-suite" | "survived" | "compile-error" | "timeout";
  killing_tests?: string[];
}

export interface Question {
  id: string;
  change_id: string;
  area: string;
  lens: LensName;
  text: string;
  options: string[];
  why_it_matters: string;
  status?: "pending" | "answered";
  asked_at?: string;
}

export interface LedgerEntry {
  question: string;
  answer: string;
  area: string;
  answered_by: string;
  ts: string;
  change_id: string;
  question_id?: string;
}

export interface CheckedItem {
  what: string;
  by: string;
  evidence_id?: string;
  result: ResultKind;
}

export interface NotChecked {
  what: string;
  why: string;
}

export interface Refusal {
  what: string;
  why: string;
}

export interface Interpretation {
  label: "A" | "B";
  summary: string;
  branch?: string;
}

export interface TrustReceipt {
  change_id: string;
  requirement_id: string;
  requirement_title: string;
  requirement_text: string;
  run_ts: string;
  head_sha: string;
  verdict: VerdictKind;
  verdict_reason: string;
  risk: number;
  areas: string[];
  area_trust?: Record<string, number>;
  checked: CheckedItem[];
  findings: Finding[];
  hunches: Finding[];
  refused: Refusal[];
  not_checked: NotChecked[];
  residual_risk: string;
  routing: Record<string, Depth>;
  counts: Record<string, number>;
  questions?: string[];
  interpretations?: Interpretation[];
  narrative?: string;
  mock_llm?: boolean;
}

export interface Decision {
  change_id: string;
  requirement_id: string;
  who: string;
  choice: "A" | "B" | "sign-off";
  evidence_ids: string[];
  merge_sha?: string;
  /** Dashboard-only: merge recorded on a host with no git repository (public demo); merge_sha stays "". */
  simulated?: boolean;
  ts: string;
}

// Dashboard-only shapes -------------------------------------------------------

export interface ContextStatus {
  status: "draft" | "confirmed";
  confirmed_by?: string;
  ts?: string;
}

/** metrics/T1.json … T7.json. See dashboard/README.md. */
export interface MetricsTable {
  columns: string[];
  rows: (string | number | null)[][];
  note?: string;
}

export type MetricsTableName = "T1" | "T2" | "T3" | "T4" | "T5" | "T6" | "T7";

/** metrics/trust.json: area -> level 0..3 */
export type TrustLevels = Record<string, number>;

/** metrics/trust-history.json: one event per area per bench case, in bench order. */
export interface TrustHistoryEvent {
  seq: number;
  change_id: string;
  area: string;
  level: number;
  event: string;
}

/** metrics/summary.json: the headline numbers. Only the keys the dashboard reads. */
export interface MetricsSummaryMode {
  cases: number;
  planted: number;
  caught: number;
  recall: number;
  control_bait_cases: number;
  false_alarm_cases: number;
  questions: number;
  questions_per_change: number;
  escapes: number;
  lens_cost_usd?: number;
  router_cost_usd?: number;
}

export interface MetricsSummary {
  oracle?: string;
  cold?: Partial<MetricsSummaryMode>;
  warm?: Partial<MetricsSummaryMode>;
  /** Cold numbers restricted to the cases the warm run covered, so the two compare like for like. */
  cold_on_warm_cases?: { cases: number; questions: number; questions_per_change: number };
  "ju26-issue-2297_routed_away_from_humans"?: { generated_files: number; files_total: number; share: number };
  excluded_incomplete_runs?: Record<string, string[]>;
}

/** context.json: what the reviewer was told about the product for this change, and which claims it changed. */
export interface RunContextEntry {
  text: string;
  area: string;
  source: string;
  kind: "context" | "ledger";
}

export interface RunContextCitation {
  lens: string;
  claim: string;
  entry: string;
  effect: "withdrawn" | "suppressed-unconfirmed" | "cited";
}

export interface RunContext {
  status: "draft" | "confirmed";
  confirmed_by: string | null;
  areas: string[];
  entries: RunContextEntry[];
  citations: RunContextCitation[];
  readers_used_ledger: number;
}

/** impact.json: which existing members the change touches, who calls them, and whether pinned behaviour held. */
export interface ImpactCaller {
  file: string;
  line: number;
  member: string;
  in_change: boolean;
  /** null when coverage was unavailable for this call site. */
  tested: boolean | null;
}

export interface ImpactMember {
  name: string;
  file: string;
  kind: "modified" | "new" | "deleted";
  signature_changed: boolean;
  callers: ImpactCaller[];
  callers_outside_change: number;
  untested_callers: number;
  ambiguous: boolean;
}

export interface Characterization {
  member: string;
  test_names: string[];
  base: "pass" | "fail" | "compile-error";
  head: "pass" | "fail" | "compile-error" | "not-run";
  result: "preserved" | "changed" | "invalid";
  evidence_id: string;
}

export interface ImpactSummary {
  modified_existing: number;
  new: number;
  deleted: number;
  callers_outside_change: number;
  untested_callers: number;
  behaviour_changes: number;
}

export interface ImpactReport {
  method: string;
  members: ImpactMember[];
  characterization: Characterization[];
  coverage_available: boolean;
  summary: ImpactSummary;
}

/** One run folder, fully loaded. Optional files may be absent. */
export interface RunBundle {
  changeId: string;
  runTs: string;
  receipt: TrustReceipt;
  intake?: IntakeReport;
  routing?: RoutingReport;
  commands: CommandReceipt[];
  tests: TestRun[];
  evidence: Evidence[];
  findings: Finding[];
  mutants: Mutant[];
  questions: Question[];
  /** Absent on runs that predate context recording. */
  context?: RunContext;
  /** Absent on runs that predate the impact check. */
  impact?: ImpactReport;
}
