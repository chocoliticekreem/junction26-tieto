import Link from "next/link";
import { notFound } from "next/navigation";
import { loadRun, readDecision } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { Disclosure } from "@/components/disclosure";
import { EvidenceCard } from "@/components/evidence-card";
import { MergeButtons } from "@/components/merge-buttons";
import { ResultPill, RiskPill, VerdictPill } from "@/components/pills";
import { EmptyState, PageTitle, Section } from "@/components/section";
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
    <div className={cn("min-w-0 space-y-3 rounded-md border p-4", chosen && "border-ok-foreground/40 bg-ok/30")} data-testid={`side-${interp.label}`}>
      <div className="flex items-start gap-3">
        <span className="flex size-7 shrink-0 items-center justify-center rounded-sm border font-mono text-sm font-semibold">{interp.label}</span>
        <div className="min-w-0 space-y-0.5">
          <p className="text-sm leading-5">{interp.summary}</p>
          {interp.branch && <div className="font-mono text-[11px] text-muted-foreground [overflow-wrap:anywhere]">{interp.branch}</div>}
          {chosen && <div className="text-xs font-medium text-ok-foreground">merged</div>}
        </div>
      </div>

      {run ? (
        <>
          <div className="flex flex-wrap items-start gap-x-2 gap-y-1 text-sm">
            <VerdictPill verdict={run.receipt.verdict} className="mt-0.5" />
            <p className="min-w-0 flex-1 leading-5 text-muted-foreground">{run.receipt.verdict_reason}</p>
          </div>
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
            <span className="font-mono tabular-nums">{run.evidence.length} receipts</span>
            {byResult.map(([k, n]) => (
              <ResultPill key={k} result={k}>
                {n} {k}
              </ResultPill>
            ))}
            <RiskPill risk={run.receipt.risk} />
            <span className="font-mono text-muted-foreground">{run.receipt.head_sha.slice(0, 8)}</span>
          </div>

          {run.receipt.findings.length > 0 && (
            <ul className="space-y-1 border-l-2 border-l-bad-foreground pl-3 text-sm">
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
                {run.receipt.checked.map((c, i) => (
                  <li key={i} className="flex items-center gap-2">
                    <ResultPill result={c.result} /> <span className="min-w-0 [overflow-wrap:anywhere]">{c.what}</span>
                  </li>
                ))}
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
  const decision = readDecision(changeId);

  return (
    <div className="space-y-6">
      <div className="text-xs text-muted-foreground">
        <Link href="/" className="hover:underline">
          Inbox
        </Link>{" "}
        /{" "}
        <Link href={`/change/${encodeURIComponent(changeId)}`} className="font-mono hover:underline">
          {changeId}
        </Link>{" "}
        / decision
      </div>
      <PageTitle eyebrow={r.requirement_id} title={r.requirement_title}>
        <div className="mt-2 flex flex-wrap items-start gap-x-2 gap-y-1 text-sm">
          <VerdictPill verdict={r.verdict} className="mt-0.5" />
          <p className="min-w-0 flex-1 leading-6">{r.verdict_reason}</p>
        </div>
        {r.verdict !== "decision-A-B" && <p className="mt-1 text-xs text-warn-foreground">Verdict is {r.verdict}, not decision-A-B; A/B shown anyway.</p>}
      </PageTitle>

      <MergeButtons
        changeId={changeId}
        human={human}
        decision={decision}
        interpretations={interps}
        blocked={Object.fromEntries(
          (
            [
              ["A", runA],
              ["B", runB],
            ] as const
          )
            .filter(([, run]) => run?.receipt.verdict === "blocked")
            .map(([label, run]) => [label, run!.receipt.verdict_reason]),
        )}
      />

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
