import Link from "next/link";
import { notFound } from "next/navigation";
import { loadRun, pendingQuestions, readDecision, signOffRefusal } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { EvidenceCard } from "@/components/evidence-card";
import { FindingCard, HunchList } from "@/components/finding-card";
import { MutantTable } from "@/components/mutant-table";
import { Mono, ResultPill, RiskPill, TrustLevel, VerdictPill } from "@/components/pills";
import { RoutingTree } from "@/components/routing-tree";
import { EmptyState, KV, PageTitle, Section } from "@/components/section";
import { SignOffButton } from "@/components/sign-off-button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
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

  return (
    <div className="space-y-8">
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
            {r.verdict === "decision-A-B" && (
              <Button render={<Link href={decisionHref} />} variant="outline" size="sm" data-testid="go-decision">
                Open A/B decision
              </Button>
            )}
            {r.verdict !== "decision-A-B" && (
              <SignOffButton changeId={changeId} human={human} decision={decision} refusal={signOffRefusal(r)} />
            )}
          </>
        }
      >
        <div className="mt-2 flex flex-wrap items-center gap-3 text-sm">
          <VerdictPill verdict={r.verdict} />
          <RiskPill risk={r.risk} />
          <span className="font-mono text-xs text-muted-foreground">{r.head_sha.slice(0, 10)}</span>
          <span className="font-mono text-xs text-muted-foreground">{r.run_ts}</span>
          {r.mock_llm && <span className="rounded-sm border px-1.5 text-[11px] text-muted-foreground">lens output from recorded fixtures</span>}
        </div>
      </PageTitle>

      {decision && (
        <div className="rounded-md border border-ok-foreground/30 bg-ok px-3 py-2 text-sm text-ok-foreground" data-testid="decision-banner">
          {decision.choice === "sign-off" ? "Signed off" : `Merged ${decision.choice}`} by <span className="font-medium">{decision.who}</span> at{" "}
          <span className="font-mono text-xs">{decision.ts}</span>
          {decision.merge_sha && (
            <>
              {" "}
              · merge <span className="font-mono text-xs">{decision.merge_sha.slice(0, 12)}</span>
            </>
          )}
        </div>
      )}

      <div className="grid gap-8 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <Section title="Requirement">
          <p className="text-sm leading-6 whitespace-pre-wrap [overflow-wrap:anywhere]">{r.requirement_text}</p>
        </Section>
        <Section title="Verdict">
          <p className="text-sm leading-6" data-testid="verdict-reason">
            {r.verdict_reason}
          </p>
          {r.narrative && <p className="text-sm leading-6 text-muted-foreground">{r.narrative}</p>}
          <KV
            items={[
              { k: "Areas", v: r.areas.length ? r.areas.map((a) => <Mono key={a} className="mr-1">{a}</Mono>) : "—" },
              {
                k: "Area trust",
                v: Object.keys(r.area_trust ?? {}).length ? (
                  <span className="flex flex-wrap gap-x-3 gap-y-1">
                    {Object.entries(r.area_trust ?? {}).map(([a, l]) => (
                      <span key={a} className="inline-flex items-center gap-1.5">
                        <span className="font-mono text-xs">{a}</span>
                        <TrustLevel level={l} />
                      </span>
                    ))}
                  </span>
                ) : (
                  "—"
                ),
              },
              {
                k: "Questions",
                v: pending.length ? (
                  <Link href="/questions" className="text-warn-foreground underline-offset-4 hover:underline">
                    {pending.length} pending
                  </Link>
                ) : (
                  `${(r.questions ?? []).length} asked, none pending`
                ),
              },
            ]}
          />
        </Section>
      </div>

      {r.verdict === "decision-A-B" && r.interpretations && r.interpretations.length > 0 && (
        <Section title="Two plausible readings" hint="the reviewer refused to pick; a named human decides">
          <div className="grid gap-3 sm:grid-cols-2">
            {r.interpretations.map((i) => (
              <div key={i.label} className="rounded-md border p-3 text-sm">
                <div className="mb-1 font-mono text-xs text-muted-foreground">
                  {i.label} {i.branch && <>· {i.branch}</>}
                </div>
                {i.summary}
              </div>
            ))}
          </div>
          <Button render={<Link href={decisionHref} />} size="sm">
            Compare A and B with receipts
          </Button>
        </Section>
      )}

      <Section title="Routing map" hint="depth follows risk; generated files verified as a block by regeneration">
        {run.intake?.summary && <p className="text-sm text-muted-foreground">{run.intake.summary}</p>}
        <RoutingTree routing={r.routing} />
      </Section>

      <Section title="Checked" count={r.checked.length}>
        {r.checked.length === 0 ? (
          <EmptyState>Nothing recorded as checked.</EmptyState>
        ) : (
          <div className="rounded-md border">
            <Table>
              <TableHeader>
                <TableRow className="hover:bg-transparent">
                  <TableHead className="h-8">What</TableHead>
                  <TableHead className="h-8 w-40">By</TableHead>
                  <TableHead className="h-8 w-24">Result</TableHead>
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
      </Section>

      <Section title="Verified findings" count={r.findings.length} hint="each one is backed by an executed receipt">
        {r.findings.length === 0 ? (
          <EmptyState>No verified findings. This is not approval: see “Not checked” below.</EmptyState>
        ) : (
          <div className="space-y-3">
            {r.findings.map((f) => (
              <FindingCard key={f.id} f={f} ctx={ctx} />
            ))}
          </div>
        )}
      </Section>

      <Section title="Unverified hunches" hint="suspicions without a receipt; shown apart, never as findings">
        <HunchList hunches={r.hunches} />
      </Section>

      {otherEvidence.length > 0 && (
        <Section title="Other receipts" count={otherEvidence.length} hint="executed checks not attached to a finding">
          <div className="space-y-2">
            {otherEvidence.map((ev) => (
              <EvidenceCard key={ev.id} ev={ev} ctx={ctx} />
            ))}
          </div>
        </Section>
      )}

      {run.mutants.length > 0 && (
        <Section title="Mutants on changed lines" count={run.mutants.length}>
          <MutantTable mutants={run.mutants} />
        </Section>
      )}

      <div className="grid gap-8 lg:grid-cols-2">
        <Section title="Refused to decide" count={r.refused.length}>
          {r.refused.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nothing refused.</p>
          ) : (
            <ul className="space-y-2" data-testid="refused-list">
              {r.refused.map((x, i) => (
                <li key={i} className="rounded-md border border-info-foreground/30 bg-info/40 p-3 text-sm">
                  <div className="font-medium">{x.what}</div>
                  <div className="text-muted-foreground">{x.why}</div>
                </li>
              ))}
            </ul>
          )}
        </Section>
        <Section title="Not checked" count={r.not_checked.length} hint="silence is never approval">
          {r.not_checked.length === 0 ? (
            <p className="text-sm text-warn-foreground">The run declared no gaps. Treat with suspicion.</p>
          ) : (
            <ul className="space-y-2" data-testid="not-checked-list">
              {r.not_checked.map((x, i) => (
                <li key={i} className="rounded-md border p-3 text-sm">
                  <div className="font-medium">{x.what}</div>
                  <div className="text-muted-foreground">{x.why}</div>
                </li>
              ))}
            </ul>
          )}
        </Section>
      </div>

      <Section title="Residual risk the signer accepts">
        <p className="rounded-md border-l-2 border-l-warn-foreground bg-warn/40 p-3 text-sm leading-6" data-testid="residual-risk">
          {r.residual_risk}
        </p>
      </Section>
    </div>
  );
}
