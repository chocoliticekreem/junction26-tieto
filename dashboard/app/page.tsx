import Link from "next/link";
import { AB_SUFFIX, isSampleChange, isSqlChange, loadLatestRuns, pendingQuestions, readDecision, receiptCounts } from "@/lib/data";
import { humanizeVerdict } from "@/lib/humanize";
import { impactQueueClause } from "@/lib/impact-summary";
import { decideSentence, wrongSentence } from "@/lib/plain-summary";
import type { Decision, RunBundle } from "@/lib/types";
import { Slip } from "@/components/mascot";
import { StaggerList } from "@/components/motion";
import { QueueCard, type QueueCardData } from "@/components/queue-card";

export const dynamic = "force-dynamic";

function firstSentence(s: string): string {
  const m = s.match(/^([^]*?[.!?])(\s+|$)/);
  return m ? m[1] : s;
}

function card(run: RunBundle, decision: Decision | undefined, pending: number): QueueCardData {
  const r = run.receipt;
  const rc = receiptCounts(run.evidence);
  const changeHref = `/change/${encodeURIComponent(run.changeId)}`;
  const decisionHref = `/decision/${encodeURIComponent(run.changeId)}`;
  const hasReadings = (r.interpretations?.length ?? 0) > 0;
  const impact = run.impact ? impactQueueClause(run.impact) : undefined;
  let status: string;
  let action: QueueCardData["action"];
  let secondary: QueueCardData["secondary"];
  if (decision) {
    status = decideSentence(r.verdict, { decision });
    action = { label: "See the record", href: changeHref, primary: false };
  } else if (r.verdict === "blocked") {
    status = [`${humanizeVerdict(r.verdict, r.verdict_reason, { findings: r.findings, evidence: run.evidence }).headline}.`, impact, "It has to be fixed before anyone can approve it."].filter(Boolean).join(" ");
    action = { label: "See why it's blocked", href: changeHref, primary: true };
    if (hasReadings) secondary = { label: "Two readings were built; see both", href: decisionHref };
  } else if (r.verdict === "decision-A-B") {
    status = decideSentence(r.verdict);
    action = { label: "Choose A or B", href: decisionHref, primary: true };
    secondary = { label: "Read the receipt first", href: changeHref };
  } else {
    status = [firstSentence(wrongSentence(r.findings, r.hunches, run.evidence)), impact, decideSentence(r.verdict)].filter(Boolean).join(" ");
    action = { label: "Review and sign off", href: `${changeHref}`, primary: true };
  }
  return {
    changeId: run.changeId,
    requirementId: r.requirement_id,
    title: r.requirement_title.replace(/[.\s]+$/, ""),
    verdict: r.verdict,
    status,
    action,
    secondary,
    pending,
    receipts: { pass: rc.green, partial: rc.other, fail: rc.red },
    decided: decision ? `${decision.choice === "sign-off" ? "signed off" : `merged ${decision.choice}`} by ${decision.who}` : undefined,
  };
}

function Group({ title, blurb, cards, testId }: { title: string; blurb: string; cards: QueueCardData[]; testId: string }) {
  if (cards.length === 0) return null;
  return (
    <section className="space-y-3" data-testid={testId}>
      <div>
        <h2 className="text-base font-semibold tracking-tight">
          {title} <span className="ml-1 font-mono text-xs font-normal text-muted-foreground tabular-nums">{cards.length}</span>
        </h2>
        <p className="text-sm text-muted-foreground">{blurb}</p>
      </div>
      <StaggerList as="ul" className="grid gap-3">
        {cards.map((c) => (
          <QueueCard key={c.changeId} c={c} />
        ))}
      </StaggerList>
    </section>
  );
}

export default function QueuePage() {
  const all = loadLatestRuns().filter((r) => !AB_SUFFIX.test(r.changeId));
  const latest = all.filter((r) => isSampleChange(r.changeId));
  const practice = all.filter((r) => !isSampleChange(r.changeId) && !isSqlChange(r.changeId)).length;
  const sql = all.filter((r) => isSqlChange(r.changeId)).length;
  const pending = pendingQuestions(latest);
  const pendingByChange = new Map<string, number>();
  for (const q of pending) pendingByChange.set(q.change_id, (pendingByChange.get(q.change_id) ?? 0) + 1);

  const cards = latest.map((run) => card(run, readDecision(run.changeId), pendingByChange.get(run.changeId) ?? 0));
  const done = cards.filter((c) => c.decided);
  const blocked = cards.filter((c) => !c.decided && c.verdict === "blocked");
  const needs = cards.filter((c) => !c.decided && c.verdict !== "blocked");
  const needYou = needs.length + blocked.filter((c) => c.pending > 0).length;

  return (
    <div className="space-y-10">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">Your queue</h1>
          <p className="mt-1 text-[15px] text-muted-foreground">AI-written changes, reviewed with proof. You make the call.</p>
        </div>
        {cards.length > 0 && (
          <div className="flex items-end gap-3">
            <Slip pose={needYou === 0 ? "empty" : "reviewing"} size={64} className="-mb-1" decorative />
            <div className="rounded-full border bg-card px-3 py-1 text-sm font-medium tabular-nums" data-testid="need-you">
              {needYou === 0 ? "Nothing needs you" : `${needYou} need${needYou === 1 ? "s" : ""} you`}
            </div>
          </div>
        )}
      </div>

      {cards.length === 0 && (
        <div className="flex items-center gap-4 rounded-lg border border-dashed bg-card/50 px-4 py-4 text-sm text-muted-foreground">
          <Slip pose="empty" size={72} decorative />
          <p>
            No changes reviewed yet. Run <code className="font-mono">uv run receipts review --change ju26-issue-2283 --req REQ-2283</code> and reload.
          </p>
        </div>
      )}

      <Group title="Needs your decision" blurb="Nothing is proven wrong. Read the receipt, then sign off or send it back." cards={needs} testId="group-needs" />
      <Group title="Blocked — waiting for a fix" blurb="A defect was proven. Nobody can approve these until it is fixed." cards={blocked} testId="group-blocked" />
      <Group title="Done" blurb="Decided by a named person. The record says what was checked and what was not." cards={done} testId="group-done" />

      {cards.length > 0 && (
        <p className="text-sm text-muted-foreground">
          Want to know how far to trust the reviewer?{" "}
          <Link href="/track-record" className="text-primary underline-offset-4 hover:underline">
            See how we know it works
          </Link>

          {practice > 0 && `: ${practice} practice runs with planted bugs we know the answers to`}
          {sql > 0 && `${practice > 0 ? ", and" : ":"} ${sql} database migrations`}.
        </p>
      )}
    </div>
  );
}
