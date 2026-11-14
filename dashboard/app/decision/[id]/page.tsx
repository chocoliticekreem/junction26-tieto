import Link from "next/link";
import { notFound } from "next/navigation";
import { ChevronRight, CircleCheck, Info } from "lucide-react";
import { loadRun, readDecision } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { abMismatchMessage, humanizeTitle, humanizeVerdict } from "@/lib/humanize";
import { Disclosure } from "@/components/disclosure";
import { EvidenceCard } from "@/components/evidence-card";
import { MergeButtons } from "@/components/merge-buttons";
import { ResultIcon, ResultPill, RiskMeter } from "@/components/pills";
import { EmptyState, PageTitle, Panel, Section } from "@/components/section";
import { VerdictSummary } from "@/components/verdict-summary";
import type { Interpretation, ResultKind, RunBundle } from "@/lib/types";
import { cn } from "@/lib/utils";

export const dynamic = "force-dynamic";

const RESULT_ORDER: ResultKind[] = ["pass", "partial", "skipped", "fail", "error"];

function diffSummary(run: RunBundle | undefined) {
  const files = run?.intake?.files ?? [];
  if (!files.length) return null;
  const add = files.reduce((n, f) => n + f.additions, 0);
  const del = files.reduce((n, f) => n + f.deletions, 0);
  return { files: files.length, add, del, list: files.filter((f) => f.kind !== "generated").slice(0, 12), generated: files.filter((f) => f.kind === "generated").length };
}

function Side({ interp, run, chosen }: { interp: Interpretation; run: RunBundle | undefined; chosen: boolean }) {
  const diff = diffSummary(run);
  const ctx = run ? { changeId: run.changeId, runTs: run.runTs, commands: run.commands, mutants: run.mutants, tests: run.tests } : null;
  const byResult = run ? RESULT_ORDER.map((k) => [k, run.evidence.filter((e) => e.result === k).length] as const).filter(([, n]) => n > 0) : [];
  return (
    <Panel className={cn("min-w-0 space-y-4 p-4", chosen && "border-ok-foreground/40 ring-2 ring-ok-foreground/15")} data-testid={`side-${interp.label}`}>
      <div className="flex items-start gap-3">
        <span className="flex size-8 shrink-0 items-center justify-center rounded-md border bg-background font-mono text-base font-semibold">{interp.label}</span>
        <div className="min-w-0 space-y-1">
          <p className="text-sm leading-5">{interp.summary}</p>
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
          <VerdictSummary verdict={run.receipt.verdict} reason={run.receipt.verdict_reason} ctx={{ findings: run.receipt.findings, evidence: run.evidence }} compact />
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
            <span className="font-mono tabular-nums">{run.evidence.length} receipts</span>
            {byResult.map(([k, n]) => (
              <ResultPill key={k} result={k}>
                {n} {k}
              </ResultPill>
            ))}
            <RiskMeter risk={run.receipt.risk} />
            <span className="font-mono text-muted-foreground">{run.receipt.head_sha.slice(0, 8)}</span>
          </div>

          {run.receipt.findings.length > 0 && (
            <ul className="space-y-1.5 rounded-md border border-bad-foreground/20 bg-bad/40 px-3 py-2 text-sm dark:bg-bad/25">
              {run.receipt.findings.map((f) => (
                <li key={f.id} className="line-clamp-2 [overflow-wrap:anywhere]" title={f.claim}>
                  {f.claim}
                </li>
              ))}
            </ul>
          )}

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
              {ctx && run.evidence.map((ev) => <EvidenceCard key={ev.id} ev={ev} ctx={ctx} />)}
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

          <div className="text-xs text-muted-foreground">
            <Link href={`/change/${encodeURIComponent(run.changeId)}`} className="underline-offset-4 hover:underline">
              Full receipt
            </Link>
          </div>
        </>
      ) : (
        <EmptyState>No run found for this branch (expected runs/{"<change>"}__{interp.label}). Merge is still possible on the branch alone.</EmptyState>
      )}
    </Panel>
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
  const decision = readDecision(changeId);
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
  const blockedHuman = Object.fromEntries(
    Object.entries(blocked).map(([label, reason]) => {
      const side = label === "A" ? runA : runB;
      return [label, humanizeVerdict("blocked", reason, side ? { findings: side.receipt.findings, evidence: side.evidence } : undefined).headline];
    }),
  );

  return (
    <div className="space-y-6">
      <nav className="flex items-center gap-1 text-xs text-muted-foreground" aria-label="breadcrumb">
        <Link href="/" className="rounded-sm hover:text-foreground hover:underline">
          Inbox
        </Link>
        <ChevronRight className="size-3" aria-hidden />
        <Link href={`/change/${encodeURIComponent(changeId)}`} className="rounded-sm font-mono hover:text-foreground hover:underline">
          {changeId}
        </Link>
        <ChevronRight className="size-3" aria-hidden />
        <span>decision</span>
      </nav>
      <PageTitle eyebrow={r.requirement_id} title={r.requirement_title}>
        <VerdictSummary verdict={r.verdict} reason={r.verdict_reason} ctx={{ findings: r.findings, evidence: run.evidence }} className="mt-3" />
        {r.verdict !== "decision-A-B" && (
          <p className="mt-3 flex max-w-2xl items-start gap-2 rounded-lg border border-info-foreground/25 bg-info px-3 py-2 text-sm text-info-foreground" data-testid="ab-mismatch">
            <Info className="mt-0.5 size-4 shrink-0" aria-hidden />
            <span title={`Verdict is ${r.verdict}, not decision-A-B; A/B shown anyway.`}>{abMismatchMessage(r.verdict, bothBlocked)}</span>
          </p>
        )}
      </PageTitle>

      <MergeButtons changeId={changeId} human={human} decision={decision} interpretations={interps} blocked={blocked} blockedHuman={blockedHuman} />

      <div className="grid gap-4 lg:grid-cols-2">
        {interps.map((i) => (
          <Side key={i.label} interp={i} run={i.label === "A" ? runA : runB} chosen={decision?.choice === i.label} />
        ))}
      </div>

      <Section title="Requirement">
        <Disclosure label={`${r.requirement_id} full text`} testId="requirement-toggle">
          <p className="max-w-3xl text-sm leading-6 whitespace-pre-wrap [overflow-wrap:anywhere]">{r.requirement_text}</p>
        </Disclosure>
      </Section>
    </div>
  );
}
