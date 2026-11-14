import { NextResponse } from "next/server";
import { loadRun, readDecision, writeDecision } from "@/lib/data";
import { mergeIntoMain } from "@/lib/git-merge";
import { nowIso, requireHuman } from "@/lib/human";
import { dirs } from "@/lib/paths";
import type { Decision } from "@/lib/types";

export async function POST(req: Request) {
  const auth = await requireHuman();
  if ("error" in auth) return auth.error;
  const body = await req.json().catch(() => ({}));
  const changeId = typeof body?.change_id === "string" ? body.change_id : "";
  const choice = body?.choice === "A" || body?.choice === "B" ? (body.choice as "A" | "B") : null;
  if (!choice) return NextResponse.json({ error: "choice must be A or B" }, { status: 400 });

  const run = loadRun(changeId);
  if (!run) return NextResponse.json({ error: `No run for change ${changeId}` }, { status: 404 });
  const existing = readDecision(changeId);
  if (existing) {
    return NextResponse.json({ error: `A decision already exists (${existing.choice} by ${existing.who}). Refusing a second merge.` }, { status: 409 });
  }

  const interp = run.receipt.interpretations?.find((i) => i.label === choice);
  const branch = interp?.branch || `review/${changeId}/${choice}`;
  const sideRun = loadRun(`${changeId}__${choice}`);
  if (sideRun?.receipt.verdict === "blocked") {
    return NextResponse.json(
      { error: `Reading ${choice} is blocked (${sideRun.receipt.verdict_reason}). Fix the blocker before merging.` },
      { status: 409 },
    );
  }
  const evidenceIds = (sideRun ?? run).evidence.map((e) => e.id);

  let mergeSha: string;
  try {
    const message = `Merge ${branch}: ${run.receipt.requirement_id} interpretation ${choice}\n\nDecided by ${auth.who} in Receipts.${interp?.summary ? `\n\n${interp.summary}` : ""}`;
    mergeSha = mergeIntoMain(dirs.mockRepo(), branch, auth.who, message);
  } catch (e) {
    return NextResponse.json({ error: e instanceof Error ? e.message : String(e) }, { status: 500 });
  }

  const decision: Decision = {
    change_id: changeId,
    requirement_id: run.receipt.requirement_id,
    who: auth.who,
    choice,
    evidence_ids: evidenceIds,
    merge_sha: mergeSha,
    ts: nowIso(),
  };
  try {
    writeDecision(decision);
  } catch {
    return NextResponse.json({ error: `Merged as ${mergeSha} but a decision record appeared concurrently; not overwriting it.` }, { status: 409 });
  }
  return NextResponse.json({ decision });
}
