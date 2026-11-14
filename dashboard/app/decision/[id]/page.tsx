import Link from "next/link";
import { notFound } from "next/navigation";
import { loadRun, readDecision, receiptCounts } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { EvidenceCard } from "@/components/evidence-card";
import { MergeButtons } from "@/components/merge-buttons";
import { Mono, ResultPill, RiskPill, VerdictPill } from "@/components/pills";
import { EmptyState, PageTitle, Section } from "@/components/section";
import type { Interpretation, RunBundle } from "@/lib/types";
import { cn } from "@/lib/utils";

export const dynamic = "force-dynamic";

function diffSummary(run: RunBundle | undefined) {
  const files = run?.intake?.files ?? [];
  if (!files.length) return null;
  const add = files.reduce((n, f) => n + f.additions, 0);
  const del = files.reduce((n, f) => n + f.deletions, 0);
  return { files: files.length, add, del, list: files.filter((f) => f.kind !== "generated").slice(0, 12), generated: files.filter((f) => f.kind === "generated").length };
}

function Side({ interp, run, chosen }: { interp: Interpretation; run: RunBundle | undefined; chosen: boolean }) {
  const diff = diffSummary(run);
  const rc = run ? receiptCounts(run.evidence) : null;
  const ctx = run ? { changeId: run.changeId, runTs: run.runTs, commands: run.commands, mutants: run.mutants, tests: run.tests } : null;
  return (
    <div className={cn("min-w-0 space-y-4 rounded-md border p-4", chosen && "border-ok-foreground/40 bg-ok/30")} data-testid={`side-${interp.label}`}>
      <div className="flex items-start gap-3">
        <span className="flex size-8 shrink-0 items-center justify-center rounded-md border bg-muted font-mono text-base font-semibold">{interp.label}</span>
        <div className="min-w-0 space-y-1">
          <p className="text-sm leading-6">{interp.summary}</p>
          {interp.branch && <Mono className="break-all">{interp.branch}</Mono>}
          {chosen && <div className="text-xs font-medium text-ok-foreground">merged</div>}
        </div>
      </div>

      {run ? (
        <>
          <div className="flex flex-wrap items-center gap-3 text-xs">
            <VerdictPill verdict={run.receipt.verdict} />
            <RiskPill risk={run.receipt.risk} />
            {rc && (
              <span className="font-mono tabular-nums">
                <span className={rc.green ? "text-ok-foreground" : "text-muted-foreground"}>{rc.green} green</span> ·{" "}
                <span className={rc.red ? "text-bad-foreground" : "text-muted-foreground"}>{rc.red} red</span>
              </span>
            )}
            <span className="font-mono text-muted-foreground">{run.receipt.head_sha.slice(0, 8)}</span>
          </div>
          <p className="text-sm text-muted-foreground">{run.receipt.verdict_reason}</p>

          {diff && (
            <div className="space-y-1 text-xs">
              <div className="font-medium">
                Diff: {diff.files} files, <span className="font-mono text-ok-foreground">+{diff.add}</span> <span className="font-mono text-bad-foreground">-{diff.del}</span>
                {diff.generated > 0 && <span className="text-muted-foreground"> · {diff.generated} generated</span>}
              </div>
              <ul className="space-y-0.5 font-mono text-[11px] text-muted-foreground">
                {diff.list.map((f) => (
                  <li key={f.path} className="flex gap-2 break-all">
                    <span className="w-14 shrink-0 tabular-nums">
                      <span className="text-ok-foreground">+{f.additions}</span> <span className="text-bad-foreground">-{f.deletions}</span>
                    </span>
                    {f.path}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <div className="space-y-2">
            <div className="text-xs font-medium">Checked</div>
            <ul className="space-y-1 text-xs">
              {run.receipt.checked.map((c, i) => (
                <li key={i} className="flex items-center gap-2">
                  <ResultPill result={c.result} /> <span>{c.what}</span>
                </li>
              ))}
            </ul>
          </div>

          {run.receipt.findings.length > 0 && (
            <div className="space-y-1 text-xs">
              <div className="font-medium text-bad-foreground">{run.receipt.findings.length} verified finding(s)</div>
              <ul className="list-disc pl-4">
                {run.receipt.findings.map((f) => (
                  <li key={f.id}>{f.claim}</li>
                ))}
              </ul>
            </div>
          )}

          <div className="space-y-2">
            <div className="text-xs font-medium">Receipts</div>
            {run.evidence.length === 0 && <EmptyState>No receipts in this run.</EmptyState>}
            {ctx && run.evidence.map((ev) => <EvidenceCard key={ev.id} ev={ev} ctx={ctx} />)}
          </div>

          <div className="text-xs text-muted-foreground">
            <Link href={`/change/${encodeURIComponent(run.changeId)}`} className="underline-offset-4 hover:underline">
              Full receipt for {run.changeId}
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
    <div className="space-y-8">
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
      <PageTitle eyebrow={r.requirement_id} title={`Decide: ${r.requirement_title}`}>
        <p className="mt-1 max-w-2xl text-sm text-muted-foreground">{r.verdict_reason}</p>
        {r.verdict !== "decision-A-B" && (
          <p className="mt-1 text-xs text-warn-foreground">The verdict of this run is {r.verdict}, not decision-A-B. The A/B view is shown anyway.</p>
        )}
      </PageTitle>

      <Section title="Requirement">
        <p className="text-sm leading-6 whitespace-pre-wrap [overflow-wrap:anywhere]">{r.requirement_text}</p>
      </Section>

      <MergeButtons changeId={changeId} human={human} decision={decision} interpretations={interps} />

      <div className="grid gap-4 lg:grid-cols-2">
        {interps.map((i) => (
          <Side key={i.label} interp={i} run={i.label === "A" ? runA : runB} chosen={decision?.choice === i.label} />
        ))}
      </div>
    </div>
  );
}
