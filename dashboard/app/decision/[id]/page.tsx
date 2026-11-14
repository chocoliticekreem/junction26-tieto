import Link from "next/link";
import { notFound } from "next/navigation";
import { ChevronRight, CircleCheck, Info, Users } from "lucide-react";
import { loadRun } from "@/lib/data";
import { decisionFor } from "@/lib/demo-state";
import { currentHuman } from "@/lib/human";
import { isReadOnly } from "@/lib/readonly";
import { DemoNote } from "@/components/demo-note";
import { abMismatchMessage, firstSentence, humanizeTitle, quotedRequirement, readerVote } from "@/lib/humanize";
import { Disclosure } from "@/components/disclosure";
import { EvidenceCard } from "@/components/evidence-card";
import { Slip } from "@/components/mascot";
import { MergeButtons } from "@/components/merge-buttons";
import { ReceiptBar, ResultIcon } from "@/components/pills";
import { EmptyState, PageTitle, Panel, Section } from "@/components/section";
import { StepIndicator } from "@/components/step-indicator";
import { StoryStep } from "@/components/story-step";
import { TechDrawer } from "@/components/tech-drawer";
import { VerdictSummary } from "@/components/verdict-summary";
import type { Interpretation, RunBundle } from "@/lib/types";
import { cn } from "@/lib/utils";

export const dynamic = "force-dynamic";

const STEPS = [
  { id: "readings", label: "Two readings" },
  { id: "vote", label: "The vote" },
  { id: "choose", label: "Your choice" },
];

function diffSummary(run: RunBundle | undefined) {
  const files = run?.intake?.files ?? [];
  if (!files.length) return null;
  const add = files.reduce((n, f) => n + f.additions, 0);
  const del = files.reduce((n, f) => n + f.deletions, 0);
  return { files: files.length, add, del, list: files.filter((f) => f.kind !== "generated").slice(0, 12), generated: files.filter((f) => f.kind === "generated").length };
}

function Side({ interp, run, chosen }: { interp: Interpretation; run: RunBundle | undefined; chosen: boolean }) {
  const { lead, rest } = firstSentence(interp.summary);
  const counts = run ? { pass: run.evidence.filter((e) => e.result === "pass").length, partial: run.evidence.filter((e) => e.result === "partial" || e.result === "skipped").length, fail: run.evidence.filter((e) => e.result === "fail" || e.result === "error").length } : null;
  return (
    <Panel className={cn("min-w-0 space-y-3 p-4 sm:p-5", chosen && "border-ok-foreground/40 ring-2 ring-ok-foreground/15")} data-testid={`side-${interp.label}`}>
      <div className="flex items-start gap-3">
        <span className="flex size-9 shrink-0 items-center justify-center rounded-lg border bg-background font-mono text-lg font-semibold">{interp.label}</span>
        <div className="min-w-0 flex-1 space-y-1">
          <p className="text-[15px] leading-6">{lead}</p>
          {rest && (
            <Disclosure label="More" testId={`reading-more-${interp.label}`}>
              <p className="text-sm leading-6 text-muted-foreground">{rest}</p>
            </Disclosure>
          )}
          {interp.branch && <div className="font-mono text-[11px] text-muted-foreground [overflow-wrap:anywhere]">{interp.branch}</div>}
          {chosen && (
            <div className="inline-flex items-center gap-1 text-xs font-medium text-ok-foreground">
              <CircleCheck className="size-3.5" /> merged
            </div>
          )}
        </div>
      </div>

      {run ? (
        <>
          <VerdictSummary verdict={run.receipt.verdict} reason={run.receipt.verdict_reason} ctx={{ findings: run.receipt.findings, evidence: run.evidence }} compact hideRaw />
          {counts && (
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <ReceiptBar pass={counts.pass} partial={counts.partial} fail={counts.fail} />
              <span className="font-mono">{run.receipt.head_sha.slice(0, 8)}</span>
            </div>
          )}
          {run.receipt.findings.length > 0 && (
            <ul className="space-y-1.5 rounded-md border border-bad-foreground/20 bg-bad/40 px-3 py-2 text-sm dark:bg-bad/25">
              {run.receipt.findings.map((f) => (
                <li key={f.id} className="line-clamp-2 [overflow-wrap:anywhere]" title={f.claim}>
                  {f.claim}
                </li>
              ))}
            </ul>
          )}
          <div className="text-xs text-muted-foreground">
            <Link href={`/change/${encodeURIComponent(run.changeId)}`} className="underline-offset-4 hover:underline">
              Full receipt for {interp.label}
            </Link>
          </div>
        </>
      ) : (
        <EmptyState>No run found for this branch (expected runs/{"<change>"}__{interp.label}). Merge is still possible on the branch alone.</EmptyState>
      )}
    </Panel>
  );
}

function SideDetails({ interp, run }: { interp: Interpretation; run: RunBundle | undefined }) {
  if (!run) return null;
  const diff = diffSummary(run);
  const ctx = { changeId: run.changeId, runTs: run.runTs, commands: run.commands, mutants: run.mutants, tests: run.tests };
  return (
    <div className="min-w-0 space-y-3" data-testid={`details-${interp.label}`}>
      <h3 className="font-mono text-sm font-semibold">{interp.label}</h3>
      <Disclosure label={`${run.receipt.checked.length} checks · ${run.evidence.length} receipts`} testId={`receipts-toggle-${interp.label}`}>
        <div className="space-y-2">
          <ul className="space-y-1 text-xs">
            {run.receipt.checked.map((c, i) => {
              const h = humanizeTitle(c.what);
              return (
                <li key={i} className="flex items-start gap-2" title={c.what}>
                  <ResultIcon result={c.result} className="mt-0.5" />
                  <span className="min-w-0 [overflow-wrap:anywhere]">
                    {h.text}
                    {h.count && <span className="text-muted-foreground"> · {h.count}</span>}
                  </span>
                </li>
              );
            })}
          </ul>
          {run.evidence.length === 0 && <EmptyState>No receipts in this run.</EmptyState>}
          {run.evidence.map((ev) => (
            <EvidenceCard key={ev.id} ev={ev} ctx={ctx} />
          ))}
        </div>
      </Disclosure>
      {diff && (
        <Disclosure
          label={
            <>
              {diff.files} files <span className="font-mono text-ok-foreground">+{diff.add}</span> <span className="font-mono text-bad-foreground">-{diff.del}</span>
              {diff.generated > 0 && <> · {diff.generated} generated</>}
            </>
          }
          testId={`diff-toggle-${interp.label}`}
        >
          <ul className="space-y-0.5 font-mono text-[11px] text-muted-foreground">
            {diff.list.map((f) => (
              <li key={f.path} className="flex gap-2 [overflow-wrap:anywhere]">
                <span className="w-14 shrink-0 tabular-nums">
                  <span className="text-ok-foreground">+{f.additions}</span> <span className="text-bad-foreground">-{f.deletions}</span>
                </span>
                {f.path}
              </li>
            ))}
          </ul>
        </Disclosure>
      )}
    </div>
  );
}

export default async function DecisionPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const changeId = decodeURIComponent(id);
  const run = loadRun(changeId);
  if (!run) notFound();
  const r = run.receipt;
  const interps: Interpretation[] =
    r.interpretations && r.interpretations.length
      ? r.interpretations
      : [
          { label: "A", summary: "Interpretation A (not described by the run)", branch: `review/${changeId}/A` },
          { label: "B", summary: "Interpretation B (not described by the run)", branch: `review/${changeId}/B` },
        ];
  const runA = loadRun(`${changeId}__A`);
  const runB = loadRun(`${changeId}__B`);
  const human = await currentHuman();
  const readOnly = isReadOnly();
  const decision = await decisionFor(changeId);
  const blocked = Object.fromEntries(
    (
      [
        ["A", runA],
        ["B", runB],
      ] as const
    )
      .filter(([, side]) => side?.receipt.verdict === "blocked")
      .map(([label, side]) => [label, side!.receipt.verdict_reason]),
  );
  const bothBlocked = "A" in blocked && "B" in blocked;
  const vote = readerVote(r.refused);
  const refusal = r.refused[0];
  const quote = quotedRequirement(r.refused);

  return (
    <div className="space-y-5">
      <nav className="flex items-center gap-1 text-xs text-muted-foreground" aria-label="breadcrumb">
        <Link href="/" className="rounded-sm hover:text-foreground hover:underline">
          Your queue
        </Link>
        <ChevronRight className="size-3" aria-hidden />
        <Link href={`/change/${encodeURIComponent(changeId)}`} className="rounded-sm font-mono hover:text-foreground hover:underline">
          {changeId}
        </Link>
        <ChevronRight className="size-3" aria-hidden />
        <span>decision</span>
      </nav>
      <PageTitle eyebrow={r.requirement_id} title={r.requirement_title}>
        <VerdictSummary verdict={r.verdict} reason={r.verdict_reason} ctx={{ findings: r.findings, evidence: run.evidence }} className="mt-3" hideRaw />
        {r.verdict !== "decision-A-B" && r.verdict !== "blocked" && (
          <p className="mt-3 flex max-w-2xl items-start gap-2 rounded-lg border border-info-foreground/25 bg-info px-3 py-2 text-sm text-info-foreground" data-testid="ab-mismatch">
            <Info className="mt-0.5 size-4 shrink-0" aria-hidden />
            <span title={`Verdict is ${r.verdict}, not decision-A-B; A/B shown anyway.`}>{abMismatchMessage(r.verdict, bothBlocked)}</span>
          </p>
        )}
      </PageTitle>

      <StepIndicator steps={STEPS} />

      <div className="space-y-10">
        <StoryStep id="readings" n={1} title="Two readings of the ticket">
          <div className="grid gap-4 lg:grid-cols-2">
            {interps.map((i) => (
              <Side key={i.label} interp={i} run={i.label === "A" ? runA : runB} chosen={decision?.choice === i.label} />
            ))}
          </div>
        </StoryStep>

        <StoryStep id="vote" n={2} title="The vote">
          <Panel className="flex items-start gap-3 p-4 sm:p-5" data-testid="reader-vote">
            <Users className="mt-0.5 size-5 shrink-0 text-primary" aria-hidden />
            <div className="min-w-0 space-y-1">
              <p className="text-[15px] font-medium leading-6">
                {vote ? `${vote.saw} of ${vote.of} independent readers saw two readings.` : "Independent readers saw two readings, so the reviewer refuses to pick."}
              </p>
              {quote ? (
                <p className="text-sm text-muted-foreground">
                  The ticket says: <q className="italic">{quote}</q>
                </p>
              ) : (
                <p className="text-sm text-muted-foreground">{refusal && !vote ? firstSentence(refusal.why).lead : "Each reader saw only the requirement, never the code or another reader's answer."}</p>
              )}
            </div>
          </Panel>
        </StoryStep>

        <StoryStep id="choose" n={3} title="Your choice">
          <Panel className="space-y-3 p-5 sm:p-6" data-testid="choice-panel">
            <div className="flex items-start gap-4">
              <Slip pose={decision ? "stamp-signed" : bothBlocked ? "stamp-blocked" : "thinking"} size={72} className="-my-2 -ml-1" decorative />
              <p className="text-lg font-medium leading-7 tracking-tight">
                {decision ? `Decided: ${decision.who} picked reading ${decision.choice}.` : bothBlocked ? "Neither reading can be merged yet: both share a proven defect." : "Pick the reading that matches what you meant. The merge is recorded under your name."}
              </p>
            </div>
            <MergeButtons changeId={changeId} human={human} decision={decision} interpretations={interps} blocked={blocked} readOnly={readOnly} />
            {readOnly && !decision && <DemoNote />}
          </Panel>
        </StoryStep>

        <TechDrawer summary={`checks and receipts for A and B · changed files · ${r.requirement_id} full text`}>
          <div className="grid gap-6 lg:grid-cols-2">
            {interps.map((i) => (
              <SideDetails key={i.label} interp={i} run={i.label === "A" ? runA : runB} />
            ))}
          </div>
          <Section title="Requirement">
            <Disclosure label={`${r.requirement_id} full text`} testId="requirement-toggle">
              <p className="max-w-3xl text-sm leading-6 whitespace-pre-wrap [overflow-wrap:anywhere]">{r.requirement_text}</p>
            </Disclosure>
          </Section>
        </TechDrawer>
      </div>
    </div>
  );
}
