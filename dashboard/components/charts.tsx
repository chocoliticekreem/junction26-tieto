// Inline SVG / CSS charts: no client JS, no library. One accent, ink for the second series, status red
// only for resets. Hover detail comes from native <title> tooltips on every mark.

export interface BarDatum {
  label: string;
  /** null = nothing in scope; renders a dash and no bar. */
  value: number | null;
  /** Shown before the percentage, e.g. "5/8". */
  detail?: string;
  /** Second, smaller line under the label (e.g. "check"). */
  sublabel?: string;
}

export function BarChart({ data, max = 1, format = (v: number) => `${Math.round(v * 100)}%` }: { data: BarDatum[]; max?: number; format?: (v: number) => string }) {
  return (
    <div className="grid max-w-2xl grid-cols-[minmax(7rem,max-content)_1fr_max-content] items-center gap-x-3 gap-y-2" role="img" aria-label="bar chart">
      {data.map((d) => {
        const w = d.value === null ? 0 : Math.max(0, Math.min(1, d.value / max)) * 100;
        const tip = d.value === null ? `${d.label}: nothing planted in scope` : `${d.label}: ${d.detail ? `${d.detail} caught, ` : ""}${format(d.value)} recall`;
        return (
          <div key={d.label} className="group contents">
            <div className="text-right leading-tight" title={tip}>
              <div className="font-mono text-[11px] [overflow-wrap:anywhere]">{d.label}</div>
              {d.sublabel && <div className="text-[9px] text-muted-foreground">{d.sublabel}</div>}
            </div>
            <div className="h-3 overflow-hidden rounded-[3px] bg-muted" title={tip}>
              {d.value !== null && <div className="h-full rounded-[3px] bg-primary transition-opacity duration-150 group-hover:opacity-80" style={{ width: `${w}%` }} />}
            </div>
            <div className="font-mono text-[11px] tabular-nums" title={tip}>
              {d.value === null ? "—" : (
                <>
                  {d.detail && <span className="text-muted-foreground">{d.detail} · </span>}
                  {format(d.value)}
                </>
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
}

export interface Series {
  name: string;
  points: { x: number; y: number }[];
  dashed?: boolean;
}

export function LineChart({ series, yLabel, xLabel = "practice run" }: { series: Series[]; yLabel?: string; xLabel?: string }) {
  const width = 480;
  const height = 190;
  const pad = { l: 34, r: 12, t: 16, b: 30 };
  const xs = series.flatMap((s) => s.points.map((p) => p.x));
  const ys = series.flatMap((s) => s.points.map((p) => p.y));
  if (!xs.length) return null;
  const xMin = Math.min(...xs);
  const xMax = Math.max(...xs);
  const yMax = Math.max(1, ...ys);
  const sx = (x: number) => pad.l + ((x - xMin) / Math.max(1, xMax - xMin)) * (width - pad.l - pad.r);
  const sy = (y: number) => pad.t + (1 - y / yMax) * (height - pad.t - pad.b);
  const yTicks = Array.from({ length: yMax + 1 }, (_, i) => i).filter((v) => yMax <= 6 || v % Math.ceil(yMax / 5) === 0);
  const xTicks = Array.from(new Set(xs)).sort((a, b) => a - b);
  const xTickStep = Math.max(1, Math.ceil(xTicks.length / 12));
  const stroke = (i: number) => (i === 0 ? "stroke-primary" : "stroke-foreground/60");
  const fill = (i: number) => (i === 0 ? "fill-primary" : "fill-foreground/60");

  return (
    <div className="space-y-1.5">
      <svg viewBox={`0 0 ${width} ${height}`} className="w-full max-w-xl" role="img" aria-label="line chart">
        {yTicks.map((v) => (
          <g key={v}>
            <line x1={pad.l} x2={width - pad.r} y1={sy(v)} y2={sy(v)} className="stroke-border" strokeWidth={1} />
            <text x={pad.l - 6} y={sy(v) + 3} textAnchor="end" className="fill-muted-foreground font-mono text-[10px] tabular-nums">
              {v}
            </text>
          </g>
        ))}
        {xTicks
          .filter((_, i) => i % xTickStep === 0)
          .map((v) => (
            <text key={v} x={sx(v)} y={height - 14} textAnchor="middle" className="fill-muted-foreground font-mono text-[10px] tabular-nums">
              {v}
            </text>
          ))}
        <text x={width - pad.r} y={height - 3} textAnchor="end" className="fill-muted-foreground text-[9px]">
          {xLabel}
        </text>
        {yLabel && (
          <text x={pad.l} y={pad.t - 6} textAnchor="start" className="fill-muted-foreground text-[9px]">
            {yLabel}
          </text>
        )}
        {series.map((s, si) => {
          const pts = s.points.slice().sort((a, b) => a.x - b.x);
          const d = pts.map((p, i) => `${i === 0 ? "M" : "L"}${sx(p.x).toFixed(1)},${sy(p.y).toFixed(1)}`).join(" ");
          return (
            <g key={s.name}>
              <path d={d} fill="none" className={stroke(si)} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" strokeDasharray={s.dashed ? "5 4" : undefined} />
              {pts.map((p) => (
                <g key={p.x} className="group">
                  <circle cx={sx(p.x)} cy={sy(p.y)} r={9} className="fill-transparent" />
                  <circle cx={sx(p.x)} cy={sy(p.y)} r={p.y > 0 ? 3.5 : 2} className={`${fill(si)} stroke-background transition-[r] duration-150 group-hover:r-5`} strokeWidth={1.5} />
                  <title>{`${s.name} · case ${p.x}: ${p.y} ${p.y === 1 ? "question" : "questions"}`}</title>
                </g>
              ))}
            </g>
          );
        })}
      </svg>
      <div className="flex flex-wrap gap-4 text-xs text-muted-foreground">
        {series.map((s, si) => (
          <span key={s.name} className="inline-flex items-center gap-1.5">
            <svg width="18" height="6" aria-hidden>
              <line x1="0" x2="18" y1="3" y2="3" className={stroke(si)} strokeWidth={2} strokeDasharray={s.dashed ? "5 4" : undefined} />
            </svg>
            {s.name}
          </span>
        ))}
      </div>
    </div>
  );
}

export interface StepPoint {
  x: number;
  level: number;
  /** A reset or escape: drawn as a hollow marker in the status colour. */
  reset?: boolean;
}

/** One small step chart of a trust level (0-3) over the practice-run sequence. */
export function TrustStepChart({ title, points, xMax }: { title: string; points: StepPoint[]; xMax: number }) {
  const width = 300;
  const height = 96;
  const pad = { l: 22, r: 8, t: 8, b: 18 };
  const pts = points.slice().sort((a, b) => a.x - b.x);
  if (!pts.length) return null;
  const sx = (x: number) => pad.l + ((x - 1) / Math.max(1, xMax - 1)) * (width - pad.l - pad.r);
  const sy = (y: number) => pad.t + (1 - y / 3) * (height - pad.t - pad.b);
  let d = `M${sx(pts[0].x).toFixed(1)},${sy(pts[0].level).toFixed(1)}`;
  for (let i = 1; i < pts.length; i++) {
    d += ` H${sx(pts[i].x).toFixed(1)} V${sy(pts[i].level).toFixed(1)}`;
  }
  const resets = pts.filter((p) => p.reset);
  const xTicks = [1, Math.round(xMax / 2), xMax];
  return (
    <figure className="min-w-0 space-y-1 rounded-lg border bg-card p-3">
      <figcaption className="flex items-baseline justify-between gap-2 text-xs">
        <span className="truncate font-mono">{title}</span>
        <span className="shrink-0 text-muted-foreground">
          L{pts[0].level} → L{pts[pts.length - 1].level}
          {resets.length > 0 && ` · ${resets.length} reset${resets.length === 1 ? "" : "s"}`}
        </span>
      </figcaption>
      <svg viewBox={`0 0 ${width} ${height}`} className="w-full" role="img" aria-label={`trust level over the practice runs for ${title}`}>
        {[0, 1, 2, 3].map((v) => (
          <g key={v}>
            <line x1={pad.l} x2={width - pad.r} y1={sy(v)} y2={sy(v)} className="stroke-border" strokeWidth={1} />
            <text x={pad.l - 5} y={sy(v) + 3} textAnchor="end" className="fill-muted-foreground font-mono text-[9px] tabular-nums">
              {v}
            </text>
          </g>
        ))}
        {xTicks.map((v) => (
          <text key={v} x={sx(v)} y={height - 5} textAnchor="middle" className="fill-muted-foreground font-mono text-[9px] tabular-nums">
            {v}
          </text>
        ))}
        <path d={d} fill="none" className="stroke-primary" strokeWidth={2} strokeLinejoin="round" />
        {pts.map((p) => (
          <g key={p.x}>
            <circle cx={sx(p.x)} cy={sy(p.level)} r={7} className="fill-transparent" />
            <title>{`case ${p.x}: L${p.level}${p.reset ? " (reset)" : ""}`}</title>
          </g>
        ))}
        {resets.map((p) => (
          <circle key={p.x} cx={sx(p.x)} cy={sy(p.level)} r={3.5} className="fill-background stroke-bad-foreground" strokeWidth={1.5} />
        ))}
      </svg>
    </figure>
  );
}
