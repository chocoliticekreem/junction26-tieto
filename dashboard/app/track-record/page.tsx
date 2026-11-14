import { readMetricsSummary, readMetricsTable, readTrustHistory, readTrustLevels } from "@/lib/data";
import type { MetricsSummary, MetricsTable, TrustHistoryEvent } from "@/lib/types";
import { BarChart, LineChart, TrustStepChart, type BarDatum, type Series, type StepPoint } from "@/components/charts";
import { Disclosure } from "@/components/disclosure";
import { MetricsTableView, StatTile, fmtCell } from "@/components/metrics-table";
import { TrustLevel } from "@/components/pills";
import { EmptyState, PageTitle, Section } from "@/components/section";

export const dynamic = "force-dynamic";

function col(t: MetricsTable, name: string): number {
  return t.columns.findIndex((c) => c.toLowerCase() === name.toLowerCase());
}

function num(v: unknown): number | null {
  if (typeof v === "number") return v;
  if (typeof v === "string" && v.trim() !== "" && !Number.isNaN(Number(v))) return Number(v);
  return null;
}

function pct(v: number): string {
  return `${Math.round(v * 100)}%`;
}

// Headline ---------------------------------------------------------------------

function headlineTiles(s: MetricsSummary | undefined) {
  if (!s?.cold) return [];
  const c = s.cold;
  const w = s.warm;
  const tiles: { label: string; value: string; unit?: string; definition: string }[] = [];
  if (c.caught !== undefined && c.planted !== undefined) {
    tiles.push({
      label: "Planted defects caught",
      value: `${c.caught}/${c.planted}`,
      unit: c.recall !== undefined ? pct(c.recall) : undefined,
      definition: "Cold run, empty ledger. Caught = blocked, or a verified finding at the planted lines.",
    });
  }
  if (c.escapes !== undefined) {
    tiles.push({
      label: "Escapes",
      value: String(c.escapes),
      definition: "Planted defects the pipeline marked auto-ready. Any escape resets that area's trust to 0.",
    });
  }
  if (c.false_alarm_cases !== undefined && c.control_bait_cases !== undefined) {
    tiles.push({
      label: "False alarms",
      value: `${c.false_alarm_cases}/${c.control_bait_cases}`,
      unit: "cases",
      definition: "Control or bait cases with a verified finding at the planted lines. Mostly equivalent mutants (test-quality).",
    });
  }
  if (c.questions_per_change !== undefined) {
    tiles.push({
      label: "Questions per change",
      value: w?.questions_per_change !== undefined ? `${c.questions_per_change.toFixed(2)} → ${w.questions_per_change.toFixed(2)}` : c.questions_per_change.toFixed(2),
      unit: w ? "cold → warm" : "cold",
      definition: `Questions to the human per change. Warm = same cases with the ledger filled${w?.cases ? ` (${w.cases} cases complete)` : ""}.`,
    });
  }
  const r = s["ju26-issue-2297_routed_away_from_humans"];
  if (r) {
    tiles.push({
      label: "2297 routed away from humans",
      value: pct(r.share),
      unit: `${r.generated_files}/${r.files_total} files`,
      definition: "Generated files verified as a block by regenerating them, so no human reads them.",
    });
  }
  if (c.lens_cost_usd !== undefined && c.cases) {
    tiles.push({
      label: "Model cost per change",
      value: `$${(c.lens_cost_usd / c.cases).toFixed(2)}`,
      unit: "cold",
      definition: `Lens spend ÷ ${c.cases} cases. Router adds $${((c.router_cost_usd ?? 0) / c.cases).toFixed(3)} per change.`,
    });
  }
  return tiles;
}

// T1 ---------------------------------------------------------------------------

function recallByLens(t1: MetricsTable | undefined): BarDatum[] {
  if (!t1) return [];
  const li = col(t1, "lens");
  const ri = col(t1, "recall");
  const ci = col(t1, "caught");
  const ni = col(t1, "planted_in_scope");
  const ki = col(t1, "kind");
  if (li < 0 || ri < 0) return [];
  return t1.rows.map((r) => {
    const caught = ci >= 0 ? num(r[ci]) : null;
    const n = ni >= 0 ? num(r[ni]) : null;
    return {
      label: String(r[li]),
      value: num(r[ri]),
      detail: caught !== null && n !== null ? `${caught}/${n}` : undefined,
      sublabel: ki >= 0 && r[ki] && r[ki] !== "lens" ? String(r[ki]) : undefined,
    };
  });
}

// T5 ---------------------------------------------------------------------------

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

const T5_SUMS = ["questions", "bait_false_alarms", "ledger_citations", "repeat_questions"] as const;

function t5Summary(t5: MetricsTable | undefined): { modes: string[]; rows: { metric: string; values: (number | null)[] }[] } | null {
  if (!t5) return null;
  const mi = col(t5, "mode");
  if (mi < 0) return null;
  const modes = [...new Set(t5.rows.map((r) => String(r[mi])))].sort((a, b) => ["cold", "warm"].indexOf(a) - ["cold", "warm"].indexOf(b));
  const rows = [
    { metric: "cases", values: modes.map((m) => t5.rows.filter((r) => String(r[mi]) === m).length) },
    ...T5_SUMS.map((name) => {
      const ci = col(t5, name);
      return {
        metric: name.replace(/_/g, " "),
        values: modes.map((m) => (ci < 0 ? null : t5.rows.filter((r) => String(r[mi]) === m).reduce((acc, r) => acc + (num(r[ci]) ?? 0), 0))),
      };
    }),
  ];
  return { modes, rows };
}

// Trust history ----------------------------------------------------------------

function trustSeries(history: TrustHistoryEvent[]): { moved: { area: string; points: StepPoint[] }[]; still: { area: string; level: number }[]; xMax: number } {
  const byArea = new Map<string, TrustHistoryEvent[]>();
  for (const e of history) {
    if (!byArea.has(e.area)) byArea.set(e.area, []);
    byArea.get(e.area)!.push(e);
  }
  const xMax = Math.max(1, ...history.map((e) => e.seq));
  const moved: { area: string; points: StepPoint[] }[] = [];
  const still: { area: string; level: number }[] = [];
  for (const [area, events] of [...byArea.entries()].sort(([a], [b]) => a.localeCompare(b))) {
    const sorted = events.slice().sort((a, b) => a.seq - b.seq);
    const points: StepPoint[] = sorted.map((e) => ({ x: e.seq, level: e.level, reset: /reset|escape/i.test(e.event) }));
    const levels = new Set(sorted.map((e) => e.level));
    if (levels.size > 1) moved.push({ area, points });
    else still.push({ area, level: sorted[0].level });
  }
  return { moved, still, xMax };
}

// Page -------------------------------------------------------------------------

export default function TrackRecordPage() {
  const summary = readMetricsSummary();
  const t1 = readMetricsTable("T1");
  const t2 = readMetricsTable("T2");
  const t3 = readMetricsTable("T3");
  const t4 = readMetricsTable("T4");
  const t5 = readMetricsTable("T5");
  const t6 = readMetricsTable("T6");
  const t7 = readMetricsTable("T7");
  const trust = readTrustLevels();
  const history = readTrustHistory();

  const tiles = headlineTiles(summary);
  const recall = recallByLens(t1);
  const qSeries = questionsOverSequence(t5);
  const q = t5Summary(t5);
  const trustHist = trustSeries(history);
  const excluded = Object.keys(summary?.excluded_incomplete_runs ?? {}).length;
  const nothing = !t1 && !t3 && !t5 && !trust && !summary;

  const t4ChangeCol = t4 ? col(t4, "change_id") : -1;
  const t4Sample = t4 && t4ChangeCol >= 0 ? t4.rows.filter((r) => String(r[t4ChangeCol]).startsWith("ju26-issue-")) : [];
  const t4Bench = t4 && t4ChangeCol >= 0 ? t4.rows.filter((r) => !String(r[t4ChangeCol]).startsWith("ju26-issue-")) : [];

  const t6All = t6 ? t6.rows.find((r) => r[col(t6, "lens")] === "all") : undefined;

  return (
    <div className="space-y-8">
      <PageTitle title="Track record">
        <p className="mt-1 text-sm text-muted-foreground">How the reviewer scores on a bench with known answers, cold (empty ledger) and warm.</p>
      </PageTitle>

      {nothing && (
        <EmptyState>
          No metrics yet. Run <code className="font-mono">uv run receipts bench run</code> then <code className="font-mono">uv run receipts metrics</code>. Expected files: metrics/summary.json, T1–T7.json,
          trust.json, trust-history.json (shapes in dashboard/README.md).
        </EmptyState>
      )}

      {tiles.length > 0 && (
        <section className="space-y-2" data-testid="headline">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {tiles.map((t) => (
              <StatTile key={t.label} {...t} />
            ))}
          </div>
          <div className="text-xs text-muted-foreground" data-testid="provenance">
            <p>
              Bench of {summary?.cold?.cases ?? "—"} cases, pipeline as of PR #11, 14 Nov 2026. Warm answers from <span className="font-mono">bench-oracle</span> (simulated human), not a
              person.
            </p>
            <Disclosure label="Limits" testId="provenance-toggle">
              <p className="max-w-3xl leading-5">
                The warm run is incomplete: the API usage limit hit, so {excluded || 11} cases ran without lenses and are excluded, not counted as misses. Planted defects are a proxy for
                reverts and incidents. Tile definitions are in each tile’s tooltip. Full limits: <span className="font-mono">metrics/README.md</span>.
              </p>
            </Disclosure>
          </div>
        </section>
      )}

      <div className="grid gap-8 lg:grid-cols-2">
        <Section title="Recall by lens">
          {recall.length ? <BarChart data={recall} /> : <EmptyState>Needs T1 with “lens” and “recall” columns.</EmptyState>}
          {recall.some((d) => d.value === null) && <p className="text-xs text-muted-foreground">— = nothing planted in scope.</p>}
        </Section>
        <Section title="Questions per change">
          {qSeries.length ? <LineChart series={qSeries} yLabel="questions" /> : <EmptyState>Needs T5 with “seq”, “questions” and “mode” columns.</EmptyState>}
          {q && (
            <div className="overflow-x-auto rounded-lg border bg-card" data-testid="t5-summary">
              <table className="w-full text-xs">
                <thead>
                  <tr className="border-b bg-muted/40 text-left text-muted-foreground">
                    <th className="px-3 py-1.5 font-medium">total</th>
                    {q.modes.map((m) => (
                      <th key={m} className="px-3 py-1.5 text-right font-medium">
                        {m}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {q.rows.map((r) => (
                    <tr key={r.metric} className="border-b last:border-0">
                      <td className="px-3 py-1">{r.metric}</td>
                      {r.values.map((v, i) => (
                        <td key={i} className="px-3 py-1 text-right font-mono tabular-nums">
                          {v === null ? "—" : v}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Section>
      </div>

      <Section title="Area trust">
        {trustHist.moved.length === 0 && trustHist.still.length === 0 ? (
          <EmptyState>No metrics/trust-history.json yet.</EmptyState>
        ) : (
          <div className="space-y-2" data-testid="trust-history">
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {trustHist.moved.map((m) => (
                <TrustStepChart key={m.area} title={m.area} points={m.points} xMax={trustHist.xMax} />
              ))}
            </div>
            {trustHist.still.length > 0 && (
              <p className="text-xs text-muted-foreground">
                Stayed at L{trustHist.still[0].level} for the whole bench: {trustHist.still.map((s) => s.area).join(", ")}.
              </p>
            )}
          </div>
        )}
        {trust && Object.keys(trust).length > 0 && (
          <div className="flex flex-wrap gap-x-4 gap-y-1" data-testid="trust-levels">
            {Object.entries(trust)
              .sort(([a], [b]) => a.localeCompare(b))
              .map(([area, level]) => (
                <span key={area} className="inline-flex items-center gap-1.5 font-mono text-xs">
                  {area} <TrustLevel level={level} />
                </span>
              ))}
          </div>
        )}
        {!trust && <EmptyState>No metrics/trust.json. Every area starts at level 1 until the bench runs.</EmptyState>}
      </Section>

      <Section title="Human load">
        {t4 ? (
          <div className="space-y-2">
            {t4Sample.length > 0 && (
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4" data-testid="t4-sample">
                {t4Sample.map((r) => {
                  const g = (name: string) => r[col(t4, name)];
                  const toHuman = num(g("files_to_human"));
                  const total = num(g("files_total"));
                  return (
                    <div key={String(r[t4ChangeCol])} className="rounded-lg border bg-card px-3.5 py-3 shadow-[0_1px_0_oklch(0_0_0/3%)]">
                      <div className="font-mono text-xs text-muted-foreground">{String(r[t4ChangeCol])}</div>
                      <div className="mt-1 font-mono text-xl font-semibold tabular-nums">
                        {toHuman ?? "—"}
                        <span className="text-sm font-normal text-muted-foreground"> of {total ?? "—"} files to a human</span>
                      </div>
                      <dl className="mt-1.5 grid grid-cols-[max-content_1fr] gap-x-3 gap-y-0.5 text-xs text-muted-foreground">
                        <dt>generated, verified</dt>
                        <dd className="text-right font-mono tabular-nums text-foreground">
                          {fmtCell(g("generated_verified_by_regeneration"), "")}/{fmtCell(g("generated"), "")}
                        </dd>
                        <dt>deep / light</dt>
                        <dd className="text-right font-mono tabular-nums text-foreground">
                          {fmtCell(g("deep"), "")} / {fmtCell(g("light"), "")}
                        </dd>
                        <dt>minutes saved</dt>
                        <dd className="text-right font-mono tabular-nums text-foreground">
                          {fmtCell(g("minutes_saved"), "")} <span className="text-muted-foreground">of {fmtCell(g("minutes_read_all"), "")}</span>
                        </dd>
                      </dl>
                    </div>
                  );
                })}
              </div>
            )}
            {t4Bench.length > 0 ? (
              <Disclosure label={`T4 · ${t4Bench.length} bench rows`} testId="t4-bench-toggle">
                <MetricsTableView t={t4} rows={t4Bench} testId="table-T4" />
              </Disclosure>
            ) : (
              <MetricsTableView t={t4} testId="table-T4" />
            )}
          </div>
        ) : (
          <EmptyState>metrics/T4.json missing.</EmptyState>
        )}
      </Section>

      <Section title="T1 · Per-lens detection">{t1 ? <MetricsTableView t={t1} testId="table-T1" /> : <EmptyState>metrics/T1.json missing.</EmptyState>}</Section>

      <Section title="Tables">
        {t6 && t6All && (
          <p className="text-sm" data-testid="t6-headline">
            <span className="font-semibold tabular-nums">{fmtCell(t6All[col(t6, "with_executed_receipt")], "")}</span> of{" "}
            <span className="font-semibold tabular-nums">{fmtCell(t6All[col(t6, "findings_shown")], "")}</span> findings shown have an executed receipt;{" "}
            <span className="font-semibold tabular-nums">{fmtCell(t6All[col(t6, "hunches_withheld")], "")}</span> hunches withheld
            {col(t6, "ledger_citations") >= 0 && <>; {fmtCell(t6All[col(t6, "ledger_citations")], "")} findings withdrawn as deliberate per the ledger</>}.
          </p>
        )}
        <div className="space-y-2">
          <Disclosure label="T2 · Per planted category" testId="t2-toggle">
            {t2 ? <MetricsTableView t={t2} testId="table-T2" /> : <EmptyState>metrics/T2.json missing.</EmptyState>}
          </Disclosure>
          <Disclosure label="T3 · Routing" testId="t3-toggle">
            {t3 ? <MetricsTableView t={t3} testId="table-T3" /> : <EmptyState>metrics/T3.json missing.</EmptyState>}
          </Disclosure>
          <Disclosure label={`T5 · Learning curve${t5 ? ` (${t5.rows.length} rows)` : ""}`} testId="t5-toggle">
            {t5 ? <MetricsTableView t={t5} testId="table-T5" /> : <EmptyState>metrics/T5.json missing.</EmptyState>}
          </Disclosure>
          <Disclosure label="T6 · Receipts per lens" testId="t6-toggle">
            {t6 ? <MetricsTableView t={t6} testId="table-T6" /> : <EmptyState>metrics/T6.json missing.</EmptyState>}
          </Disclosure>
          <Disclosure label="T7 · Upstream comparison" testId="t7-toggle">
            {t7 ? <MetricsTableView t={t7} testId="table-T7" wrap /> : <EmptyState>metrics/T7.json missing.</EmptyState>}
          </Disclosure>
        </div>
      </Section>
    </div>
  );
}
