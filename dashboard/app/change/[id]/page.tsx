import Link from "next/link";
import { notFound } from "next/navigation";
import { ChevronRight, EyeOff, FlaskConical, Route, ScrollText, ShieldAlert, Split } from "lucide-react";
import { loadRun, pendingQuestions, readDecision, signOffRefusal } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { humanizeTitle } from "@/lib/humanize";
import { receiptMarkdown } from "@/lib/receipt-markdown";
import { CopyReceiptButton } from "@/components/copy-receipt-button";
import { Disclosure } from "@/components/disclosure";
import { EvidenceCard } from "@/components/evidence-card";
import { FindingCard, HunchList } from "@/components/finding-card";
import { splitNote } from "@/components/metrics-table";
import { MutantTable } from "@/components/mutant-table";
import { ResultIcon, TrustLevel } from "@/components/pills";
import { Proven } from "@/components/proven";
import { provenLines } from "@/lib/proven";
import { RoutingTree } from "@/components/routing-tree";
import { EmptyState, PageTitle, Panel, Section, TightList } from "@/components/section";
import { SignOffButton } from "@/components/sign-off-button";
import { TrustReceipt } from "@/components/trust-receipt";
import { VerdictSummary } from "@/components/verdict-summary";
import { Button } from "@/components/ui/button";

export const dynamic = "force-dynamic";

export default async function ChangePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const changeId = decodeURIComponent(id);
  const run = loadRun(changeId);
  if (!run) notFound();
  const { receipt: r } = run;
  const human = await currentHuman();
  const decision = readDecision(changeId);
  const pending = pendingQuestions([run]).filter((q) => q.change_id === changeId);
  const ctx = { changeId, runTs: run.runTs, commands: run.commands, evidence: run.evidence, mutants: run.mutants, tests: run.tests };
  const findingEvidenceIds = new Set(r.findings.flatMap((f) => f.evidence_ids ?? []));
  const otherEvidence = run.evidence.filter((e) => !findingEvidenceIds.has(e.id));
  const decisionHref = `/decision/${encodeURIComponent(changeId)}`;
  const markdown = receiptMarkdown(run, decision);
  const residual = splitNote(r.residual_risk);
  const routingTotal = Object.keys(r.routing).length;
  const routingCounts = (["deep", "light", "regeneration"] as const).map((d) => `${Object.values(r.routing).filter((v) => v === d).length} ${d}`).join(" · ");

  return (
    <div className="space-y-6">
      <nav className="flex items-center gap-1 text-xs text-muted-foreground" aria-label="breadcrumb">
        <Link href="/" className="rounded-sm hover:text-foreground hover:underline">
          Inbox
        </Link>
        <ChevronRight className="size-3" aria-hidden />
        <span className="font-mono">{changeId}</span>
      </nav>

      <PageTitle
        eyebrow={r.requirement_id}
        title={r.requirement_title}
        right={
          <>
            <CopyReceiptButton markdown={markdown} apiHref={`/api/receipt-markdown?change=${encodeURIComponent(changeId)}`} />
            {r.verdict === "decision-A-B" ? (
              <Button render={<Link href={decisionHref} />} size="sm" data-testid="go-decision">
                <Split data-icon="inline-start" /> Decide A or B
              </Button>
            ) : (
              <SignOffButton changeId={changeId} human={human} decision={decision} refusal={signOffRefusal(r)} verdict={r.verdict} />
            )}
          </>
        }
      >
        <VerdictSummary verdict={r.verdict} reason={r.verdict_reason} ctx={{ findings: r.findings, evidence: run.evidence }} className="mt-3" />
        <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
          {r.areas.map((a) => (
            <span key={a} className="inline-flex items-center gap-1.5">
              <span className="font-mono">{a}</span> <TrustLevel level={r.area_trust?.[a]} />
            </span>
          ))}
          {pending.length > 0 && (
            <Link href="/questions" className="font-medium text-warn-foreground hover:underline">
              {pending.length} {pending.length === 1 ? "question" : "questions"} pending
            </Link>
          )}
        </div>
      </PageTitle>

      {decision && (
        <div className="rounded-lg border border-ok-foreground/25 bg-ok px-3 py-2 text-sm text-ok-foreground" data-testid="decision-banner">
          {decision.choice === "sign-off" ? "Signed off" : `Merged ${decision.choice}`} by <span className="font-medium">{decision.who}</span>{" "}
          <span className="font-mono text-xs">{decision.ts}</span>
          {decision.merge_sha && <span className="font-mono text-xs"> · {decision.merge_sha.slice(0, 12)}</span>}
        </div>
      )}

      <div className="grid gap-8 lg:grid-cols-[minmax(0,1fr)_21rem] lg:items-start">
        <div className="min-w-0 space-y-8">
          <Proven lines={provenLines(run.evidence)} />

          {r.verdict === "decision-A-B" && r.interpretations && r.interpretations.length > 0 && (
            <Section title="Two readings" icon={<Split />}>
              <div className="grid gap-3 sm:grid-cols-2">
                {r.interpretations.map((i) => (
                  <Panel key={i.label} className="flex gap-3 p-3 text-sm">
                    <span className="flex size-6 shrink-0 items-center justify-center rounded-md border bg-background font-mono text-xs font-semibold">{i.label}</span>
                    <div className="min-w-0">
                      <p className="leading-5">{i.summary}</p>
                      {i.branch && <div className="mt-1 font-mono text-[11px] text-muted-foreground">{i.branch}</div>}
                    </div>
                  </Panel>
                ))}
              </div>
            </Section>
          )}

          <Section title="Findings" count={r.findings.length} id="findings" icon={<ShieldAlert />}>
            {r.findings.length === 0 ? (
              <p className="text-sm text-muted-foreground">No verified findings. Not approval: see Not checked.</p>
            ) : (
              <div className="space-y-2">
                {r.findings.map((f) => (
                  <FindingCard key={f.id} f={f} ctx={ctx} />
                ))}
              </div>
            )}
            <HunchList hunches={r.hunches} />
          </Section>

          <div className="grid gap-6 md:grid-cols-2">
            <Section title="Not checked" count={r.not_checked.length} id="not-checked" icon={<EyeOff />}>
              {r.not_checked.length === 0 ? (
                <p className="text-sm text-warn-foreground">The run declared no gaps. Treat with suspicion.</p>
              ) : (
                <TightList items={r.not_checked} testId="not-checked-list" />
              )}
            </Section>
            <Section title="Refused to decide" count={r.refused.length} id="refused" icon={<Split />}>
              {r.refused.length === 0 ? <p className="text-sm text-muted-foreground">Nothing refused.</p> : <TightList items={r.refused} testId="refused-list" />}
            </Section>
          </div>

          <Section title="Residual risk">
            <div className="rounded-lg border border-warn-foreground/25 bg-warn/50 px-3.5 py-3 text-sm leading-6 dark:bg-warn/30" data-testid="residual-risk">
              <p>{residual.lead}</p>
              {residual.rest && (
                <Disclosure label="More" testId="residual-toggle">
                  <p className="text-muted-foreground">{residual.rest}</p>
                </Disclosure>
              )}
            </div>
          </Section>

          <Section title="Receipts" count={run.evidence.length} id="receipts" icon={<ScrollText />}>
            <Disclosure label={`${r.checked.length} checks · ${otherEvidence.length} receipts not attached to a finding`} testId="receipts-toggle">
              <div className="space-y-3">
                {r.checked.length > 0 && (
                  <Panel className="divide-y">
                    {r.checked.map((c, i) => {
                      const h = humanizeTitle(c.what);
                      return (
                        <div key={i} className="flex items-start gap-2.5 px-3 py-2 text-sm">
                          <ResultIcon result={c.result} className="mt-1" />
                          <div className="min-w-0 flex-1 [overflow-wrap:anywhere]">
                            <span title={c.what}>{h.text}</span>
                            {h.count && <span className="text-muted-foreground"> · {h.count}</span>}
                            <div className="mt-0.5 flex flex-wrap gap-x-2 font-mono text-[11px] text-muted-foreground">
                              <span>{c.by}</span>
                              {h.note && <span>{h.note}</span>}
                              {c.evidence_id && (
                                <a href={`#evidence-${c.evidence_id}`} className="text-primary underline-offset-4 hover:underline">
                                  {c.evidence_id}
                                </a>
                              )}
                            </div>
                          </div>
                        </div>
                      );
                    })}
                  </Panel>
                )}
                {otherEvidence.length === 0 && r.checked.length === 0 && <EmptyState>Nothing recorded as checked.</EmptyState>}
                {otherEvidence.map((ev) => (
                  <EvidenceCard key={ev.id} ev={ev} ctx={ctx} />
                ))}
              </div>
            </Disclosure>
            {run.mutants.length > 0 && (
              <Disclosure label={<span className="inline-flex items-center gap-1"><FlaskConical className="size-3" /> {run.mutants.length} mutants on changed lines</span>} testId="mutants-toggle">
                <MutantTable mutants={run.mutants} />
              </Disclosure>
            )}
            {r.narrative && (
              <Disclosure label="Reviewer narrative" testId="narrative-toggle">
                <p className="max-w-3xl text-sm leading-6 text-muted-foreground">{r.narrative}</p>
              </Disclosure>
            )}
          </Section>

          <Section title="Routing" icon={<Route />}>
            {run.intake?.summary && <p className="text-sm">{run.intake.summary}</p>}
            <Disclosure label={routingTotal ? `${routingTotal} files: ${routingCounts}` : "No routing map in this run"} testId="routing-toggle">
              <RoutingTree routing={r.routing} />
            </Disclosure>
          </Section>

          <Section title="Requirement">
            <Disclosure label={`${r.requirement_id} full text`} testId="requirement-toggle">
              <p className="max-w-3xl text-sm leading-6 whitespace-pre-wrap [overflow-wrap:anywhere]" data-testid="requirement-text">
                {r.requirement_text}
              </p>
            </Disclosure>
          </Section>
        </div>

        <TrustReceipt run={run} decision={decision} pending={pending.length} className="order-first lg:order-none lg:sticky lg:top-16" />
      </div>
    </div>
  );
}
