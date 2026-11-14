import { readMetricsTable, readTrustLevels } from "@/lib/data";
import type { MetricsTable } from "@/lib/types";
import { BarChart, LineChart, type Series } from "@/components/charts";
import { TrustLevel } from "@/components/pills";
import { EmptyState, PageTitle, Section } from "@/components/section";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

export const dynamic = "force-dynamic";

function col(t: MetricsTable, name: string): number {
  return t.columns.findIndex((c) => c.toLowerCase() === name.toLowerCase());
}

function num(v: unknown): number | null {
  if (typeof v === "number") return v;
  if (typeof v === "string" && v.trim() !== "" && !Number.isNaN(Number(v))) return Number(v);
  return null;
}

function fmt(v: string | number | null, column: string): string {
  if (v === null || v === undefined) return "—";
  if (typeof v === "number") {
    if (/recall|precision|rate/i.test(column) && v >= 0 && v <= 1) return `${(v * 100).toFixed(0)}%`;
    if (/usd|cost/i.test(column)) return `$${v.toFixed(v < 1 ? 4 : 2)}`;
    return Number.isInteger(v) ? String(v) : v.toFixed(2);
  }
  return v;
}

function MetricsTableView({ t, testId }: { t: MetricsTable; testId: string }) {
  // Numeric columns right-align; a column is numeric when every non-null value is a number.
  const numeric = t.columns.map((_, ci) => t.rows.every((r) => r[ci] === null || typeof r[ci] === "number") && t.rows.some((r) => typeof r[ci] === "number"));
  return (
    <div className="space-y-2">
      <div className="rounded-md border">
        <Table data-testid={testId}>
          <TableHeader>
            <TableRow className="hover:bg-transparent">
              {t.columns.map((c, i) => (
                <TableHead key={c} className={`h-8 whitespace-nowrap ${numeric[i] ? "text-right" : ""}`}>
                  {c}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {t.rows.map((row, ri) => (
              <TableRow key={ri}>
                {row.map((v, ci) => (
                  <TableCell key={ci} className={`py-1.5 font-mono text-xs tabular-nums ${numeric[ci] ? "text-right" : ""}`}>
                    {fmt(v, t.columns[ci] ?? "")}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      {t.note && <p className="text-xs text-muted-foreground">{t.note}</p>}
    </div>
  );
}

function recallByLens(t1: MetricsTable | undefined) {
  if (!t1) return [];
  const li = col(t1, "lens");
  const ri = col(t1, "recall");
  if (li < 0 || ri < 0) return [];
  return t1.rows.flatMap((r) => {
    const v = num(r[ri]);
    return v === null ? [] : [{ label: String(r[li]), value: v }];
  });
}

function questionsOverSequence(t5: MetricsTable | undefined): Series[] {
  if (!t5) return [];
  const si = col(t5, "seq");
  const qi = col(t5, "questions");
  const mi = col(t5, "mode");
  if (si < 0 || qi < 0) return [];
  const byMode = new Map<string, { x: number; y: number }[]>();
  for (const r of t5.rows) {
    const x = num(r[si]);
    const y = num(r[qi]);
    if (x === null || y === null) continue;
    const mode = mi >= 0 ? String(r[mi]) : "all";
    if (!byMode.has(mode)) byMode.set(mode, []);
    byMode.get(mode)!.push({ x, y });
  }
  const order = ["cold", "warm"];
  return [...byMode.entries()]
    .sort(([a], [b]) => order.indexOf(a) - order.indexOf(b))
    .map(([name, points]) => ({ name, points, dashed: name === "warm" }));
}

export default function TrackRecordPage() {
  const t1 = readMetricsTable("T1");
  const t3 = readMetricsTable("T3");
  const t5 = readMetricsTable("T5");
  const trust = readTrustLevels();
  const recall = recallByLens(t1);
  const qSeries = questionsOverSequence(t5);
  const nothing = !t1 && !t3 && !t5 && !trust;

  return (
    <div className="space-y-10">
      <PageTitle title="Track record" eyebrow="metrics/ from the bench, cold and warm">
        <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
          Trust is earned per lens and per area from planted defects with known answers. Numbers here are what the pipeline measured, not what it claims.
        </p>
      </PageTitle>

      {nothing && (
        <EmptyState>
          No metrics yet. Run <code className="font-mono">uv run receipts bench run</code> then <code className="font-mono">uv run receipts metrics</code>. Expected files: metrics/T1.json, T3.json, T5.json, trust.json
          (shapes in dashboard/README.md).
        </EmptyState>
      )}

      <div className="grid gap-8 lg:grid-cols-2">
        <Section title="Recall by lens" hint="share of planted cases in scope each lens caught">
          {recall.length ? <BarChart data={recall} /> : <EmptyState>Needs T1 with “lens” and “recall” columns.</EmptyState>}
        </Section>
        <Section title="Questions per change over the sequence" hint="cold run vs warm run (ledger filled)">
          {qSeries.length ? <LineChart series={qSeries} yLabel="questions" /> : <EmptyState>Needs T5 with “seq”, “questions” and “mode” columns.</EmptyState>}
        </Section>
      </div>

      <Section title="Area trust levels" hint="0 forces human review; 3 may be auto-ready; any escape resets to 0">
        {trust && Object.keys(trust).length ? (
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3" data-testid="trust-levels">
            {Object.entries(trust)
              .sort(([a], [b]) => a.localeCompare(b))
              .map(([area, level]) => (
                <div key={area} className="flex items-center justify-between gap-3 rounded-md border px-3 py-2">
                  <span className="truncate font-mono text-xs">{area}</span>
                  <TrustLevel level={level} />
                </div>
              ))}
          </div>
        ) : (
          <EmptyState>No metrics/trust.json. Every area starts at level 1 until the bench runs.</EmptyState>
        )}
      </Section>

      <Section title="T1 · Per-lens detection">{t1 ? <MetricsTableView t={t1} testId="table-T1" /> : <EmptyState>metrics/T1.json missing.</EmptyState>}</Section>
      <Section title="T3 · Routing" hint="cheap router vs full review at equal recall">
        {t3 ? <MetricsTableView t={t3} testId="table-T3" /> : <EmptyState>metrics/T3.json missing.</EmptyState>}
      </Section>
      <Section title="T5 · Learning curve" hint="questions asked per change, cold vs warm">
        {t5 ? <MetricsTableView t={t5} testId="table-T5" /> : <EmptyState>metrics/T5.json missing.</EmptyState>}
      </Section>
    </div>
  );
}
