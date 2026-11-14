import { NextResponse } from "next/server";
import { loadRun, readDecision, writeDecision } from "@/lib/data";
import { branchSha, mergeIntoMain } from "@/lib/git-merge";
import { nowIso, requireHuman } from "@/lib/human";
import { readOnlyRefusal } from "@/lib/readonly";
import { dirs } from "@/lib/paths";
import type { Decision } from "@/lib/types";

export async function POST(req: Request) {
  const readOnly = readOnlyRefusal();
  if (readOnly) return readOnly;
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

  const refuse = (error: string) => NextResponse.json({ error }, { status: 409 });
  if (run.receipt.verdict !== "decision-A-B") {
    return refuse(`Merge refused: verdict is ${run.receipt.verdict}, not decision-A-B; only an A/B decision merges a reading.`);
  }
  const interp = run.receipt.interpretations?.find((i) => i.label === choice);
  const branch = interp?.branch;
  if (!interp || !branch) return refuse(`Merge refused: the receipt records no branch for reading ${choice}.`);
  const sideRun = loadRun(`${changeId}__${choice}`);
  if (!sideRun) return refuse(`Merge refused: reading ${choice} has no review run of its own (${changeId}__${choice}), so it has no receipts.`);
  if (sideRun.receipt.verdict === "blocked") {
    return refuse(`Reading ${choice} is blocked (${sideRun.receipt.verdict_reason}). Fix the blocker before merging.`);
  }
  const reviewedSha = sideRun.receipt.head_sha;
  const currentSha = branchSha(dirs.mockRepo(), branch);
  if (!currentSha) return refuse(`Merge refused: branch ${branch} does not exist in the mock repo.`);
  if (currentSha !== reviewedSha) {
    return refuse(
      `Merge refused: ${branch} moved since it was reviewed (reviewed ${reviewedSha.slice(0, 12)}, now ${currentSha.slice(0, 12)}). Re-run the review.`,
    );
  }
  const evidenceIds = sideRun.evidence.map((e) => e.id);

  let mergeSha: string;
  try {
    const message = `Merge ${branch}: ${run.receipt.requirement_id} interpretation ${choice}\n\nDecided by ${auth.who} in Receipts.${interp.summary ? `\n\n${interp.summary}` : ""}`;
    mergeSha = mergeIntoMain(dirs.mockRepo(), branch, reviewedSha, auth.who, message);
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
