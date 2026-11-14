import Link from "next/link";
import { AB_SUFFIX, isSampleChange, isSqlChange, loadLatestRuns, minTrust, pendingQuestions, readDecision, receiptCounts } from "@/lib/data";
import type { Decision, RunBundle } from "@/lib/types";
import { RiskPill, TrustLevel, VerdictPill } from "@/components/pills";
import { EmptyState, PageTitle } from "@/components/section";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { cn } from "@/lib/utils";

export const dynamic = "force-dynamic";

interface Row {
  run: RunBundle;
  href: string;
  green: number;
  red: number;
  pending: number;
  decision?: Decision;
  trust?: number;
}

function Title({ r }: { r: Row }) {
  return (
    <Link href={r.href} className="group block min-w-0">
      <div className="flex flex-wrap items-baseline gap-x-2">
        <span className="font-mono text-xs text-muted-foreground">{r.run.receipt.requirement_id}</span>
        <span className="font-medium group-hover:underline">{r.run.receipt.requirement_title}</span>
      </div>
      <div className="mt-0.5 flex flex-wrap gap-x-3 font-mono text-[11px] text-muted-foreground">
        <span>{r.run.changeId}</span>
        <span>{r.run.receipt.head_sha.slice(0, 8)}</span>
        <span>{r.run.runTs}</span>
        {r.decision && (
          <span className="text-ok-foreground">
            {r.decision.choice === "sign-off" ? "signed off" : `merged ${r.decision.choice}`} by {r.decision.who}
          </span>
        )}
      </div>
    </Link>
  );
}

function Receipts({ r }: { r: Row }) {
  return (
    <span className="font-mono text-xs tabular-nums" data-testid="receipt-counts">
      <span className={cn(r.green ? "text-ok-foreground" : "text-muted-foreground")}>{r.green} green</span>
      <span className="text-muted-foreground"> · </span>
      <span className={cn(r.red ? "text-bad-foreground" : "text-muted-foreground")}>{r.red} red</span>
    </span>
  );
}

function Pending({ r }: { r: Row }) {
  return r.pending > 0 ? (
    <Link href="/questions" className="font-mono text-xs text-warn-foreground tabular-nums underline-offset-4 hover:underline">
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
      <div className="hidden rounded-md border md:block">
        <Table>
          <TableHeader>
            <TableRow className="hover:bg-transparent">
              <TableHead className="h-8">Change</TableHead>
              <TableHead className="h-8 w-32">Verdict</TableHead>
              <TableHead className="h-8 w-16 text-right">Risk</TableHead>
              <TableHead className="h-8 w-28 text-right">Receipts</TableHead>
              <TableHead className="h-8 w-24 text-right">Questions</TableHead>
              <TableHead className="h-8 w-24 text-right">Trust</TableHead>
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
                <TableCell className="py-2 text-right">
                  <RiskPill risk={r.run.receipt.risk} />
                </TableCell>
                <TableCell className="py-2 text-right">
                  <Receipts r={r} />
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
      </div>
      <ul className="divide-y rounded-md border md:hidden">
        {rows.map((r) => (
          <li key={r.run.changeId} className="space-y-2 p-3" data-testid="inbox-card">
            <Title r={r} />
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <VerdictPill verdict={r.run.receipt.verdict} />
              <RiskPill risk={r.run.receipt.risk} />
              <Receipts r={r} />
              <Pending r={r} />
              <TrustLevel level={r.trust} />
            </div>
          </li>
        ))}
      </ul>
    </section>
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
        green: rc.green,
        red: rc.red,
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
      <PageTitle title="Inbox" eyebrow="latest run per change">
        <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
          One row per change. Receipts are executed checks (pass / fail). Trust is the lowest area trust level touched by the change.
        </p>
      </PageTitle>
      {rows.length === 0 && (
        <EmptyState>
          No runs found. Run <code className="font-mono">uv run receipts review --change ju26-issue-2283 --req REQ-2283</code> and reload.
        </EmptyState>
      )}
      <Group title="Tieto sample changes" rows={sample} />
      <Group title="Bench" rows={bench} />
      <Group title="SQL migrations (second sandbox adapter)" rows={sql} />
    </div>
  );
}
