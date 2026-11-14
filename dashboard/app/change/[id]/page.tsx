import Link from "next/link";
import { notFound } from "next/navigation";
import { loadRun, pendingQuestions, readDecision, signOffRefusal } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { receiptMarkdown } from "@/lib/receipt-markdown";
import { CopyReceiptButton } from "@/components/copy-receipt-button";
import { Disclosure } from "@/components/disclosure";
import { EvidenceCard } from "@/components/evidence-card";
import { FindingCard, HunchList } from "@/components/finding-card";
import { splitNote } from "@/components/metrics-table";
import { MutantTable } from "@/components/mutant-table";
import { ResultPill, RiskPill, TrustLevel, VerdictPill } from "@/components/pills";
import { Proven } from "@/components/proven";
import { provenLines } from "@/lib/proven";
import { RoutingTree } from "@/components/routing-tree";
import { EmptyState, PageTitle, Section, TightList } from "@/components/section";
import { SignOffButton } from "@/components/sign-off-button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import type { ResultKind } from "@/lib/types";

export const dynamic = "force-dynamic";

const RESULT_ORDER: ResultKind[] = ["pass", "partial", "skipped", "fail", "error"];

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
  const byResult = RESULT_ORDER.map((k) => [k, run.evidence.filter((e) => e.result === k).length] as const).filter(([, n]) => n > 0);
  const residual = splitNote(r.residual_risk);
  const routingTotal = Object.keys(r.routing).length;
  const routingCounts = (["deep", "light", "regeneration"] as const).map((d) => `${Object.values(r.routing).filter((v) => v === d).length} ${d}`).join(" · ");

  return (
    <div className="space-y-6">
      <div className="text-xs text-muted-foreground">
        <Link href="/" className="hover:underline">
          Inbox
        </Link>{" "}
        / <span className="font-mono">{changeId}</span>
      </div>

      <PageTitle
        eyebrow={r.requirement_id}
        title={r.requirement_title}
        right={
          <>
            <CopyReceiptButton markdown={markdown} apiHref={`/api/receipt-markdown?change=${encodeURIComponent(changeId)}`} />
            {r.verdict === "decision-A-B" ? (
              <Button render={<Link href={decisionHref} />} size="sm" data-testid="go-decision">
                Decide A or B
              </Button>
            ) : (
              <SignOffButton changeId={changeId} human={human} decision={decision} refusal={signOffRefusal(r)} />
            )}
          </>
        }
      >
        <div className="mt-2 flex flex-wrap items-start gap-x-2 gap-y-1 text-sm">
          <VerdictPill verdict={r.verdict} className="mt-0.5" />
          <p className="min-w-0 flex-1 leading-6" data-testid="verdict-reason">
            {r.verdict_reason}
          </p>
        </div>
        <div className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs" data-testid="receipt-strip">
          <span className="font-mono tabular-nums">{run.evidence.length} receipts</span>
          {byResult.map(([k, n]) => (
            <a key={k} href="#receipts">
              <ResultPill result={k}>
                {n} {k}
              </ResultPill>
            </a>
          ))}
        </div>
        <div className="mt-1.5 flex flex-wrap gap-x-3 gap-y-1 font-mono text-xs tabular-nums text-muted-foreground" data-testid="counts-row">
          <a href="#findings" className="hover:underline">
            <span className={r.findings.length ? "text-bad-foreground" : ""}>{r.findings.length}</span> findings
          </a>
          <span>{r.hunches.length} hunches withheld</span>
          <a href="#not-checked" className="hover:underline">
            {r.not_checked.length} not checked
          </a>
          <a href="#refused" className="hover:underline">
            {r.refused.length} refused
          </a>
          {pending.length ? (
            <Link href="/questions" className="text-warn-foreground hover:underline">
              {pending.length} questions pending
            </Link>
          ) : (
            <span>{(r.questions ?? []).length} questions</span>
          )}
        </div>
        <Proven lines={provenLines(run.evidence)} />
        <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 font-mono text-xs text-muted-foreground">
          <RiskPill risk={r.risk} />
          {r.areas.map((a) => (
            <span key={a} className="inline-flex items-center gap-1">
              {a} <TrustLevel level={r.area_trust?.[a]} />
            </span>
          ))}
          <span>{r.head_sha.slice(0, 10)}</span>
          <span>{r.run_ts}</span>
          {r.mock_llm && <span>lens output from recorded fixtures</span>}
        </div>
      </PageTitle>

      {decision && (
        <div className="rounded-md border border-ok-foreground/30 bg-ok px-3 py-2 text-sm text-ok-foreground" data-testid="decision-banner">
          {decision.choice === "sign-off" ? "Signed off" : `Merged ${decision.choice}`} by <span className="font-medium">{decision.who}</span>{" "}
          <span className="font-mono text-xs">{decision.ts}</span>
          {decision.merge_sha && <span className="font-mono text-xs"> · {decision.merge_sha.slice(0, 12)}</span>}
        </div>
      )}

      {r.verdict === "decision-A-B" && r.interpretations && r.interpretations.length > 0 && (
        <Section title="Two readings">
          <div className="grid gap-3 sm:grid-cols-2">
            {r.interpretations.map((i) => (
              <div key={i.label} className="flex gap-3 text-sm">
                <span className="flex size-6 shrink-0 items-center justify-center rounded-sm border font-mono text-xs font-semibold">{i.label}</span>
                <div className="min-w-0">
                  <p className="leading-5">{i.summary}</p>
                  {i.branch && <div className="mt-0.5 font-mono text-[11px] text-muted-foreground">{i.branch}</div>}
                </div>
              </div>
            ))}
          </div>
        </Section>
      )}

      <Section title="Findings" count={r.findings.length} id="findings">
        {r.findings.length === 0 ? (
          <p className="text-sm text-muted-foreground">No verified findings. Not approval: see Not checked.</p>
        ) : (
          <div className="space-y-3">
            {r.findings.map((f) => (
              <FindingCard key={f.id} f={f} ctx={ctx} />
            ))}
          </div>
        )}
        <HunchList hunches={r.hunches} />
      </Section>

      <div className="grid gap-6 lg:grid-cols-2">
        <Section title="Not checked" count={r.not_checked.length} id="not-checked">
          {r.not_checked.length === 0 ? (
            <p className="text-sm text-warn-foreground">The run declared no gaps. Treat with suspicion.</p>
          ) : (
            <TightList items={r.not_checked} testId="not-checked-list" />
          )}
        </Section>
        <Section title="Refused to decide" count={r.refused.length} id="refused">
          {r.refused.length === 0 ? <p className="text-sm text-muted-foreground">Nothing refused.</p> : <TightList items={r.refused} testId="refused-list" />}
        </Section>
      </div>

      <Section title="Residual risk">
        <div className="border-l-2 border-l-warn-foreground pl-3 text-sm leading-6" data-testid="residual-risk">
          <p>{residual.lead}</p>
          {residual.rest && (
            <Disclosure label="More" testId="residual-toggle">
              <p className="text-muted-foreground">{residual.rest}</p>
            </Disclosure>
          )}
        </div>
      </Section>

      <Section title="Receipts" count={run.evidence.length} id="receipts">
        <Disclosure label={`${r.checked.length} checks · ${otherEvidence.length} receipts not attached to a finding`} testId="receipts-toggle">
          <div className="space-y-3">
            {r.checked.length > 0 && (
              <div className="rounded-md border">
                <Table>
                  <TableHeader>
                    <TableRow className="hover:bg-transparent">
                      <TableHead className="h-8">What</TableHead>
                      <TableHead className="h-8 w-32">By</TableHead>
                      <TableHead className="h-8 w-20">Result</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {r.checked.map((c, i) => (
                      <TableRow key={i}>
                        <TableCell className="min-w-56 py-1.5 text-sm whitespace-normal [overflow-wrap:anywhere]">
                          {c.what}
                          {c.evidence_id && (
                            <a href={`#evidence-${c.evidence_id}`} className="ml-2 font-mono text-[11px] text-primary underline-offset-4 hover:underline">
                              {c.evidence_id}
                            </a>
                          )}
                        </TableCell>
                        <TableCell className="py-1.5 font-mono text-xs text-muted-foreground">{c.by}</TableCell>
                        <TableCell className="py-1.5">
                          <ResultPill result={c.result} />
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            )}
            {otherEvidence.length === 0 && r.checked.length === 0 && <EmptyState>Nothing recorded as checked.</EmptyState>}
            {otherEvidence.map((ev) => (
              <EvidenceCard key={ev.id} ev={ev} ctx={ctx} />
            ))}
          </div>
        </Disclosure>
        {run.mutants.length > 0 && (
          <Disclosure label={`${run.mutants.length} mutants on changed lines`} testId="mutants-toggle">
            <MutantTable mutants={run.mutants} />
          </Disclosure>
        )}
        {r.narrative && (
          <Disclosure label="Reviewer narrative" testId="narrative-toggle">
            <p className="text-sm leading-6 text-muted-foreground">{r.narrative}</p>
          </Disclosure>
        )}
      </Section>

      <Section title="Routing">
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
  );
}
