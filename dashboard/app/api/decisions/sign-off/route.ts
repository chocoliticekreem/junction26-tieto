import { NextResponse } from "next/server";
import { loadRun, readDecision, signOffRefusal, writeDecision } from "@/lib/data";
import { nowIso, requireHuman } from "@/lib/human";
import { readOnlyRefusal } from "@/lib/readonly";
import type { Decision } from "@/lib/types";

export async function POST(req: Request) {
  const readOnly = readOnlyRefusal();
  if (readOnly) return readOnly;
  const auth = await requireHuman();
  if ("error" in auth) return auth.error;
  const body = await req.json().catch(() => ({}));
  const changeId = typeof body?.change_id === "string" ? body.change_id : "";
  const run = loadRun(changeId);
  if (!run) return NextResponse.json({ error: `No run for change ${changeId}` }, { status: 404 });
  if (readDecision(changeId)) return NextResponse.json({ error: "A decision already exists for this change." }, { status: 409 });
  const refusal = signOffRefusal(run.receipt);
  if (refusal) return NextResponse.json({ error: refusal }, { status: 409 });
  const decision: Decision = {
    change_id: changeId,
    requirement_id: run.receipt.requirement_id,
    who: auth.who,
    choice: "sign-off",
    evidence_ids: run.evidence.map((e) => e.id),
    merge_sha: "",
    ts: nowIso(),
  };
  writeDecision(decision);
  return NextResponse.json({ decision });
}
