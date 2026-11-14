import Link from "next/link";
import { AB_SUFFIX, isSampleChange, isSqlChange, loadLatestRuns, minTrust, pendingQuestions, readDecision, receiptCounts } from "@/lib/data";
import type { Decision, RunBundle } from "@/lib/types";
import { ReceiptBar, RiskMeter, TrustLevel, VerdictPill } from "@/components/pills";
import { EmptyState, PageTitle, Panel } from "@/components/section";
import { Term } from "@/components/term";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

export const dynamic = "force-dynamic";

interface Row {
  run: RunBundle;
  href: string;
  pass: number;
  partial: number;
  fail: number;
  pending: number;
  decision?: Decision;
  trust?: number;
}

function Title({ r }: { r: Row }) {
  const meta = `${r.run.receipt.head_sha.slice(0, 8)} · ${r.run.runTs}`;
  return (
    <Link href={r.href} className="group flex min-w-0 flex-col gap-0.5 rounded-sm md:flex-row md:items-baseline md:gap-x-2" title={meta}>
      <span className="shrink-0 font-mono text-[11px] text-muted-foreground">{r.run.changeId}</span>
      <span className="min-w-0 text-sm group-hover:text-primary md:flex-1 md:truncate">{r.run.receipt.requirement_title}</span>
      {r.decision && (
        <span className="shrink-0 text-xs text-ok-foreground">
          {r.decision.choice === "sign-off" ? "signed off" : `merged ${r.decision.choice}`} by {r.decision.who}
        </span>
      )}
    </Link>
  );
}

function Pending({ r }: { r: Row }) {
  return r.pending > 0 ? (
    <Link href="/questions" className="font-mono text-xs font-medium text-warn-foreground tabular-nums underline-offset-4 hover:underline">
      {r.pending} pending
    </Link>
  ) : (
    <span className="font-mono text-xs text-muted-foreground tabular-nums">0</span>
  );
}

function Group({ title, rows }: { title: string; rows: Row[] }) {
  if (rows.length === 0) return null;
  return (
    <section className="space-y-2">
      <h2 className="text-sm font-semibold tracking-tight">
        {title} <span className="ml-1 font-mono text-xs font-normal text-muted-foreground tabular-nums">{rows.length}</span>
      </h2>
      <Panel className="hidden overflow-hidden md:block">
        <Table className="table-fixed">
          <TableHeader>
            <TableRow className="bg-muted/40 hover:bg-muted/40">
              <TableHead className="h-8 text-xs text-muted-foreground">Change</TableHead>
              <TableHead className="h-8 w-36 text-xs text-muted-foreground">Verdict</TableHead>
              <TableHead className="h-8 w-28 text-xs text-muted-foreground">Risk</TableHead>
              <TableHead className="h-8 w-32 text-xs text-muted-foreground">Receipts</TableHead>
              <TableHead className="h-8 w-24 text-right text-xs text-muted-foreground">Questions</TableHead>
              <TableHead className="h-8 w-16 text-right text-xs text-muted-foreground">Trust</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((r) => (
              <TableRow key={r.run.changeId} data-testid="inbox-row">
                <TableCell className="py-2 whitespace-normal">
                  <Title r={r} />
                </TableCell>
                <TableCell className="py-2">
                  <VerdictPill verdict={r.run.receipt.verdict} />
                </TableCell>
                <TableCell className="py-2">
                  <RiskMeter risk={r.run.receipt.risk} />
                </TableCell>
                <TableCell className="py-2">
                  <ReceiptBar pass={r.pass} partial={r.partial} fail={r.fail} />
                </TableCell>
                <TableCell className="py-2 text-right">
                  <Pending r={r} />
                </TableCell>
                <TableCell className="py-2 text-right">
                  <TrustLevel level={r.trust} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Panel>
      <Panel className="divide-y md:hidden">
        {rows.map((r) => (
          <div key={r.run.changeId} className="space-y-2 px-3 py-2.5" data-testid="inbox-card">
            <Title r={r} />
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
              <VerdictPill verdict={r.run.receipt.verdict} />
              <RiskMeter risk={r.run.receipt.risk} />
              <ReceiptBar pass={r.pass} partial={r.partial} fail={r.fail} />
              <Pending r={r} />
              <TrustLevel level={r.trust} />
            </div>
          </div>
        ))}
      </Panel>
    </section>
  );
}

function VerdictLegend() {
  return (
    <div className="flex flex-wrap items-center gap-x-5 gap-y-1.5 rounded-lg border bg-card/60 px-3 py-2 text-xs text-muted-foreground" data-testid="verdict-legend">
      <span className="inline-flex items-center gap-1.5">
        <VerdictPill verdict="blocked" /> a defect is proven; fix it first
      </span>
      <span className="inline-flex items-center gap-1.5">
        <VerdictPill verdict="needs-sign-off" /> nothing proven wrong; a person <Term k="signoff">decides</Term>
      </span>
      <span className="inline-flex items-center gap-1.5">
        <VerdictPill verdict="decision-A-B" /> <Term k="ab">two readings</Term>; a person picks one
      </span>
    </div>
  );
}

export default function InboxPage() {
  const latest = loadLatestRuns();
  const pending = pendingQuestions(latest);
  const pendingByChange = new Map<string, number>();
  for (const q of pending) pendingByChange.set(q.change_id, (pendingByChange.get(q.change_id) ?? 0) + 1);

  const rows: Row[] = latest
    .filter((r) => !AB_SUFFIX.test(r.changeId))
    .map((run) => {
      const rc = receiptCounts(run.evidence);
      return {
        run,
        href: `/change/${encodeURIComponent(run.changeId)}`,
        pass: rc.green,
        fail: rc.red,
        partial: rc.other,
        pending: pendingByChange.get(run.changeId) ?? 0,
        decision: readDecision(run.changeId),
        trust: minTrust(run.receipt.area_trust),
      };
    });
  const sample = rows.filter((r) => isSampleChange(r.run.changeId));
  const sql = rows.filter((r) => isSqlChange(r.run.changeId));
  const bench = rows.filter((r) => !isSampleChange(r.run.changeId) && !isSqlChange(r.run.changeId));

  return (
    <div className="space-y-8">
      <PageTitle title="Inbox">
        <p className="mt-1 text-sm text-muted-foreground">Latest run per change. Every finding has a receipt; a named human decides.</p>
      </PageTitle>
      {rows.length === 0 && (
        <EmptyState>
          No runs found. Run <code className="font-mono">uv run receipts review --change ju26-issue-2283 --req REQ-2283</code> and reload.
        </EmptyState>
      )}
      {rows.length > 0 && <VerdictLegend />}
      <Group title="Tieto sample changes" rows={sample} />
      <Group title="Bench" rows={bench} />
      <Group title="SQL migrations (second sandbox adapter)" rows={sql} />
    </div>
  );
}
