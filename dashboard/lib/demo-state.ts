import { cookies } from "next/headers";
import type { NextResponse } from "next/server";
import { loadRun, readContextStatus, readDecision, readLedger } from "./data";
import type { ContextStatus, Decision, LedgerEntry } from "./types";

/**
 * Public demo on a serverless host: instances share no disk, so a visitor's clicks live in their own cookie and are
 * merged over the bundled data on every read. Outside ephemeral mode every accessor here is the plain file read.
 */
export function isEphemeral(): boolean {
  return process.env.RECEIPTS_EPHEMERAL === "1";
}

export const DEMO_COOKIE = "receipts_demo_state";
const MAX_COOKIE = 3500;
const MAX_ANSWER = 200;

/** [change_id, choice, who, ts, merge_sha, simulated 0|1] */
type DecisionRow = [string, Decision["choice"], string, string, string, 0 | 1];
/** [question_id, change_id, answer, who, ts] */
type LedgerRow = [string, string, string, string, string];
export interface DemoState {
  d: DecisionRow[];
  l: LedgerRow[];
  /** [who, ts] once the visitor confirmed the context. */
  c?: [string, string];
}

const str = (v: unknown): v is string => typeof v === "string";

function parse(raw: string | undefined): DemoState {
  const empty: DemoState = { d: [], l: [] };
  if (!raw) return empty;
  try {
    const v = JSON.parse(Buffer.from(raw, "base64url").toString("utf8"));
    const d = Array.isArray(v?.d)
      ? v.d.filter((r: unknown[]) => Array.isArray(r) && r.slice(0, 5).every(str) && ["A", "B", "sign-off"].includes(r[1] as string))
      : [];
    const l = Array.isArray(v?.l) ? v.l.filter((r: unknown[]) => Array.isArray(r) && r.length === 5 && r.every(str)) : [];
    const c = Array.isArray(v?.c) && v.c.length === 2 && v.c.every(str) ? (v.c as [string, string]) : undefined;
    return { d, l, c };
  } catch {
    return empty;
  }
}

function encode(s: DemoState): string {
  return Buffer.from(JSON.stringify(s), "utf8").toString("base64url");
}

export async function readDemoState(): Promise<DemoState> {
  if (!isEphemeral()) return { d: [], l: [] };
  return parse((await cookies()).get(DEMO_COOKIE)?.value);
}

/** Writes the state onto the response, dropping the oldest answers, then the oldest decisions, to stay under the cookie budget. */
export function saveDemoState(res: NextResponse, s: DemoState): void {
  const state: DemoState = { d: [...s.d], l: [...s.l], c: s.c };
  while (encode(state).length > MAX_COOKIE && (state.l.length || state.d.length)) {
    if (state.l.length) state.l.shift();
    else state.d.shift();
  }
  res.cookies.set(DEMO_COOKIE, encode(state), { path: "/", httpOnly: true, sameSite: "lax", maxAge: 60 * 60 * 24 * 7 });
}

export function clearDemoState(res: NextResponse): void {
  res.cookies.set(DEMO_COOKIE, "", { path: "/", maxAge: 0 });
}

export function addDecision(s: DemoState, d: Decision): DemoState {
  const row: DecisionRow = [d.change_id, d.choice, d.who, d.ts, d.merge_sha ?? "", d.simulated ? 1 : 0];
  return { ...s, d: [...s.d.filter((r) => r[0] !== d.change_id), row] };
}

export function addAnswer(s: DemoState, e: LedgerEntry): DemoState {
  const row: LedgerRow = [e.question_id ?? "", e.change_id, e.answer.slice(0, MAX_ANSWER), e.answered_by, e.ts];
  return { ...s, l: [...s.l, row] };
}

export function confirmContext(s: DemoState, who: string, ts: string): DemoState {
  return { ...s, c: [who, ts] };
}

/** Requirement and evidence ids come from the run data, as the write routes derive them, so the cookie holds only the click. */
function decisionFromRow([changeId, choice, who, ts, sha, sim]: DecisionRow): Decision | undefined {
  const run = loadRun(changeId);
  if (!run) return undefined;
  const evidenceRun = choice === "sign-off" ? run : loadRun(`${changeId}__${choice}`);
  return {
    change_id: changeId,
    requirement_id: run.receipt.requirement_id,
    who,
    choice,
    evidence_ids: (evidenceRun?.evidence ?? []).map((e) => e.id),
    merge_sha: sha,
    ...(sim ? { simulated: true } : {}),
    ts,
  };
}

function entryFromRow([questionId, changeId, answer, who, ts]: LedgerRow): LedgerEntry | undefined {
  const q = loadRun(changeId)?.questions.find((x) => x.id === questionId);
  if (!q) return undefined;
  return { question: q.text, answer, area: q.area, answered_by: who, ts, change_id: changeId, question_id: questionId };
}

export async function decisionFor(changeId: string): Promise<Decision | undefined> {
  if (!isEphemeral()) return readDecision(changeId);
  const row = (await readDemoState()).d.find((r) => r[0] === changeId);
  return (row && decisionFromRow(row)) ?? readDecision(changeId);
}

export async function ledgerEntries(): Promise<LedgerEntry[]> {
  if (!isEphemeral()) return readLedger();
  const mine = (await readDemoState()).l.flatMap((r) => entryFromRow(r) ?? []);
  return [...readLedger(), ...mine];
}

export async function contextStatus(): Promise<ContextStatus> {
  const bundled = readContextStatus();
  if (!isEphemeral() || bundled.status === "confirmed") return bundled;
  const c = (await readDemoState()).c;
  return c ? { status: "confirmed", confirmed_by: c[0], ts: c[1] } : bundled;
}
