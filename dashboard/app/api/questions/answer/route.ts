import { NextResponse } from "next/server";
import { appendLedger, loadRun } from "@/lib/data";
import { addAnswer, isEphemeral, ledgerEntries, readDemoState, saveDemoState } from "@/lib/demo-state";
import { nowIso, requireHuman } from "@/lib/human";
import { readOnlyRefusal } from "@/lib/readonly";
import type { LedgerEntry } from "@/lib/types";

export async function POST(req: Request) {
  const readOnly = readOnlyRefusal();
  if (readOnly) return readOnly;
  const auth = await requireHuman();
  if ("error" in auth) return auth.error;
  const body = await req.json().catch(() => ({}));
  const questionId = typeof body?.question_id === "string" ? body.question_id : "";
  const changeId = typeof body?.change_id === "string" ? body.change_id : "";
  const answer = typeof body?.answer === "string" ? body.answer.trim() : "";
  if (!questionId || !changeId || !answer) {
    return NextResponse.json({ error: "question_id, change_id and a non-empty answer are required" }, { status: 400 });
  }
  const run = loadRun(changeId);
  const q = run?.questions.find((x) => x.id === questionId);
  if (!q) return NextResponse.json({ error: `Question ${questionId} not found in the latest run of ${changeId}` }, { status: 404 });
  if ((await ledgerEntries()).some((e) => e.question_id === questionId)) {
    return NextResponse.json({ error: "Already answered." }, { status: 409 });
  }
  const entry: LedgerEntry = {
    question: q.text,
    answer,
    area: q.area,
    answered_by: auth.who,
    ts: nowIso(),
    change_id: changeId,
    question_id: questionId,
  };
  if (isEphemeral()) {
    const res = NextResponse.json({ entry });
    saveDemoState(res, addAnswer(await readDemoState(), entry));
    return res;
  }
  appendLedger(entry);
  return NextResponse.json({ entry });
}
