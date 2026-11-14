import Link from "next/link";
import { notFound } from "next/navigation";
import { ChevronRight, FlaskConical, Info, MessageCircleQuestion, Split } from "lucide-react";
import { loadRun, pendingQuestions, readDecision, signOffRefusal } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { humanizeTitle, lensLabel } from "@/lib/humanize";
import { plainSummary } from "@/lib/plain-summary";
import { receiptMarkdown } from "@/lib/receipt-markdown";
import { CopyReceiptButton } from "@/components/copy-receipt-button";
import { Disclosure } from "@/components/disclosure";
import { EvidenceCard } from "@/components/evidence-card";
import { FindingCard, HunchList } from "@/components/finding-card";
import { ImpactStep } from "@/components/impact-step";
import { PlainSummary } from "@/components/plain-summary";
import { ProductContext } from "@/components/product-context";
import { Term } from "@/components/term";
import { splitNote } from "@/components/metrics-table";
import { MutantTable } from "@/components/mutant-table";
import { ResultIcon, TrustLevel } from "@/components/pills";
import { Proven } from "@/components/proven";
import { provenLines } from "@/lib/proven";
import { QuestionForm } from "@/components/question-form";
import { RoutingTree } from "@/components/routing-tree";
import { EmptyState, PageTitle, Panel, Section, TightList } from "@/components/section";
import { ShowMore } from "@/components/show-more";
import { SignOffButton } from "@/components/sign-off-button";
import { StepIndicator } from "@/components/step-indicator";
import { StoryStep } from "@/components/story-step";
import { TechDrawer } from "@/components/tech-drawer";
import { TrustReceipt } from "@/components/trust-receipt";
import { VerdictSummary } from "@/components/verdict-summary";
import { Button } from "@/components/ui/button";

export const dynamic = "force-dynamic";

const STEPS = [
  { id: "what-changed", label: "What changed" },
  { id: "impact", label: "Impact on other features" },
  { id: "proved", label: "What we proved" },
  { id: "not-checked", label: "What we didn't check" },
  { id: "decide", label: "Your decision" },
];

const MAX_FINDINGS = 3;

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
  const summary = plainSummary(run, { decision, pendingQuestions: pending.length });
  const story = summary.filter((s) => s.key !== "decide");
  const decide = summary.find((s) => s.key === "decide")!;
  const routingCounts = (["deep", "light", "regeneration"] as const).map((d) => `${Object.values(r.routing).filter((v) => v === d).length} ${d}`).join(" · ");
  const hasReadings = (r.interpretations?.length ?? 0) > 0;
  const proven = provenLines(run.evidence);
  const techSummary = [`${run.evidence.length} receipts`, `${r.hunches.length} hunches`, run.mutants.length ? `${run.mutants.length} mutants` : null, routingTotal ? `${routingTotal} files routed` : null, "full requirement", "copy as PR comment"]
    .filter(Boolean)
    .join(" · ");

  return (
    <div className="space-y-5">
      <nav className="flex items-center gap-1 text-xs text-muted-foreground" aria-label="breadcrumb">
        <Link href="/" className="rounded-sm hover:text-foreground hover:underline">
          Your queue
        </Link>
        <ChevronRight className="size-3" aria-hidden />
        <span className="font-mono">{changeId}</span>
      </nav>

      <PageTitle eyebrow={r.requirement_id} title={r.requirement_title}>
        <VerdictSummary verdict={r.verdict} reason={r.verdict_reason} ctx={{ findings: r.findings, evidence: run.evidence }} className="mt-3" hideRaw />
      </PageTitle>

      {decision && (
        <div className="rounded-lg border border-ok-foreground/25 bg-ok px-3 py-2 text-sm text-ok-foreground" data-testid="decision-banner">
          {decision.choice === "sign-off" ? "Signed off" : `Merged ${decision.choice}`} by <span className="font-medium">{decision.who}</span>{" "}
          <span className="font-mono text-xs">{decision.ts}</span>
          {decision.merge_sha && <span className="font-mono text-xs"> · {decision.merge_sha.slice(0, 12)}</span>}
        </div>
      )}

      <StepIndicator steps={STEPS} />

      <div className="grid gap-x-8 gap-y-10 lg:grid-cols-[minmax(0,1fr)_21rem] lg:items-start">
        <StoryStep id="what-changed" n={1} title="What changed" className="lg:col-start-1">
          <PlainSummary items={story} bare />
          <ProductContext context={run.context} areas={r.areas} />
        </StoryStep>

        <StoryStep id="impact" n={2} title="Impact on other features" className="lg:col-start-1">
          <ImpactStep impact={run.impact} />
        </StoryStep>

        <StoryStep id="proved" n={3} title="What we proved" className="lg:col-start-1">
          <Proven lines={proven} />
          <div className="space-y-2" id="findings">
            <h3 className="flex items-center gap-1.5 text-sm font-semibold tracking-tight">
              <Term k="finding">Findings</Term>
              <span className="font-mono text-xs font-normal tabular-nums text-muted-foreground">{r.findings.length}</span>
            </h3>
            {r.findings.length === 0 ? (
              <p className="text-sm text-muted-foreground">Nothing proven wrong. That is not approval: see what was not checked.</p>
            ) : (
              <ShowMore max={MAX_FINDINGS} noun="findings" testId="show-all-findings">
                {r.findings.map((f) => (
                  <FindingCard key={f.id} f={f} ctx={ctx} />
                ))}
              </ShowMore>
            )}
            {r.hunches.length > 0 && (
              <p className="text-xs text-muted-foreground">
                {r.hunches.length} {r.hunches.length === 1 ? "suspicion" : "suspicions"} without proof {r.hunches.length === 1 ? "is" : "are"} listed under{" "}
                <a href="#technical" className="text-primary underline-offset-4 hover:underline">
                  technical details
                </a>
                ; they do not count against the change.
              </p>
            )}
          </div>
        </StoryStep>

        <div className="min-w-0 self-start lg:sticky lg:top-24 lg:col-start-2 lg:row-start-1 lg:row-span-6">
          <TrustReceipt run={run} decision={decision} pending={pending.length} />
        </div>

        <StoryStep id="not-checked" n={4} title="What we didn't check" className="lg:col-start-1">
          {r.not_checked.length === 0 ? (
            <p className="text-sm text-warn-foreground">The run declared no gaps. Treat with suspicion.</p>
          ) : (
            <Panel className="px-3.5 py-1">
              <TightList items={r.not_checked} testId="not-checked-list" />
            </Panel>
          )}
          {r.refused.length > 0 && (
            <div className="space-y-1.5" id="refused">
              <h3 className="flex items-center gap-1.5 text-sm font-semibold tracking-tight">
                <Split className="size-3.5 text-muted-foreground" aria-hidden /> Refused to decide
                <span className="font-mono text-xs font-normal tabular-nums text-muted-foreground">{r.refused.length}</span>
              </h3>
              <Panel className="px-3.5 py-1">
                <TightList items={r.refused} testId="refused-list" />
              </Panel>
            </div>
          )}
        </StoryStep>

        <StoryStep id="decide" n={5} title="Your decision" className="lg:col-start-1">
          <Panel className="space-y-5 p-5 sm:p-6" data-testid="decision-panel">
            {pending.length > 0 && !decision && (
              <div className="space-y-3" id="questions" data-testid="pending-questions">
                <h3 className="flex items-center gap-1.5 text-sm font-semibold text-warn-foreground">
                  <MessageCircleQuestion className="size-4" aria-hidden />
                  Before you decide: {pending.length} {pending.length === 1 ? "question" : "questions"} for you
                </h3>
                {pending.map((q) => (
                  <div key={q.id} className="space-y-2 rounded-lg border border-warn-foreground/25 bg-warn/40 p-4 [overflow-wrap:anywhere] dark:bg-warn/25" data-testid="pending-question">
                    <p className="text-base font-medium leading-6 tracking-tight">{q.text}</p>
                    <p className="text-sm text-muted-foreground">{q.why_it_matters}</p>
                    <p className="font-mono text-[11px] text-muted-foreground">
                      {lensLabel[q.lens] ?? q.lens} lens · {q.area} · {q.id}
                    </p>
                    <QuestionForm q={q} human={human} />
                  </div>
                ))}
              </div>
            )}

            <p className="text-lg font-medium leading-7 tracking-tight" data-testid="plain-decide">
              <a href={decide.href} className="underline-offset-4 hover:underline">
                {decide.text}
              </a>
            </p>

            <div className="rounded-lg border border-warn-foreground/25 bg-warn/50 px-3.5 py-3 text-sm leading-6 dark:bg-warn/30" data-testid="residual-risk">
              <div className="text-xs font-semibold text-warn-foreground">{r.verdict === "blocked" ? "Risk that stays even after the fix" : decision ? "The risk accepted at sign-off" : "The risk you accept by signing"}</div>
              <p className="mt-0.5">{residual.lead}</p>
              {residual.rest && (
                <Disclosure label="More" testId="residual-toggle">
                  <p className="text-muted-foreground">{residual.rest}</p>
                </Disclosure>
              )}
            </div>

            <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
              {r.verdict === "decision-A-B" && !decision ? (
                <Button render={<Link href={decisionHref} />} nativeButton={false} size="lg" className="px-4 text-[15px]" data-testid="go-decision">
                  <Split data-icon="inline-start" /> Choose A or B
                </Button>
              ) : (
                <SignOffButton changeId={changeId} human={human} decision={decision} refusal={signOffRefusal(r)} verdict={r.verdict} />
              )}
              {hasReadings && r.verdict !== "decision-A-B" && (
                <Link href={decisionHref} className="text-sm text-primary underline-offset-4 hover:underline" data-testid="go-decision">
                  Two readings were built; see both
                </Link>
              )}
            </div>
            {!decision && r.verdict !== "blocked" && (
              <p className="text-xs text-muted-foreground">
                Signing records your name, the receipts you relied on, and the gaps above. <Term k="signoff">The AI never approves on its own.</Term>
              </p>
            )}
          </Panel>
        </StoryStep>

        <div className="lg:col-start-1">
          <TechDrawer summary={techSummary}>
            <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-xs text-muted-foreground">
              <CopyReceiptButton markdown={markdown} apiHref={`/api/receipt-markdown?change=${encodeURIComponent(changeId)}`} />
              <span className="inline-flex flex-wrap items-center gap-x-3 gap-y-1">
                <Term k="trust">Trust earned</Term>
                {r.areas.map((a) => (
                  <span key={a} className="inline-flex items-center gap-1.5">
                    <span className="font-mono">{a}</span> <TrustLevel level={r.area_trust?.[a]} />
                  </span>
                ))}
              </span>
            </div>

            <Section title="Pipeline verdict">
              <Disclosure label={<span className="inline-flex items-center gap-1"><Info className="size-3" /> pipeline reason</span>} testId="verdict-raw-toggle">
                <p className="font-mono text-[11px] leading-5 text-muted-foreground [overflow-wrap:anywhere]" data-testid="verdict-reason">
                  {r.verdict_reason}
                </p>
              </Disclosure>
            </Section>

            <Section title={<><Term k="hunch">Hunches</Term></>} count={r.hunches.length} id="hunches">
              {r.hunches.length === 0 ? <p className="text-sm text-muted-foreground">No unproven suspicions.</p> : <HunchList hunches={r.hunches} />}
            </Section>

            <Section title={<Term k="receipt">Receipts</Term>} count={run.evidence.length} id="receipts">
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
                <Disclosure label={<span className="inline-flex items-center gap-1"><FlaskConical className="size-3" /> {run.mutants.length} <Term k="mutant">mutants</Term> on changed lines</span>} testId="mutants-toggle">
                  <MutantTable mutants={run.mutants} />
                </Disclosure>
              )}
              {r.narrative && (
                <Disclosure label="Reviewer narrative" testId="narrative-toggle">
                  <p className="max-w-3xl text-sm leading-6 text-muted-foreground">{r.narrative}</p>
                </Disclosure>
              )}
            </Section>

            <Section title="Routing">
              {run.intake?.summary && <p className="text-sm">{run.intake.summary}</p>}
              <Disclosure label={routingTotal ? `${routingTotal} files: ${routingCounts}` : "No routing map in this run"} testId="routing-toggle">
                <RoutingTree routing={r.routing} />
              </Disclosure>
            </Section>

            <Section title="Requirement" id="requirement">
              <Disclosure label={`${r.requirement_id} full text`} testId="requirement-toggle">
                <p className="max-w-3xl text-sm leading-6 whitespace-pre-wrap [overflow-wrap:anywhere]" data-testid="requirement-text">
                  {r.requirement_text}
                </p>
              </Disclosure>
            </Section>
          </TechDrawer>
        </div>
      </div>
    </div>
  );
}
