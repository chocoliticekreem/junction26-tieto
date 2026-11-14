// Inline SVG charts: no client JS, no library. Quiet by design.

export function BarChart({ data, max = 1, format = (v: number) => `${Math.round(v * 100)}%` }: { data: { label: string; value: number }[]; max?: number; format?: (v: number) => string }) {
  const rowH = 22;
  const labelW = 130;
  const width = 480;
  const barW = width - labelW - 56;
  const height = data.length * rowH + 8;
  return (
    <svg viewBox={`0 0 ${width} ${height}`} className="w-full max-w-xl" role="img" aria-label="bar chart">
      {data.map((d, i) => {
        const y = i * rowH + 4;
        const w = Math.max(0, Math.min(1, d.value / max)) * barW;
        return (
          <g key={d.label}>
            <text x={labelW - 8} y={y + 14} textAnchor="end" className="fill-muted-foreground font-mono text-[11px]">
              {d.label}
            </text>
            <rect x={labelW} y={y + 3} width={barW} height={rowH - 8} className="fill-muted" rx={2} />
            <rect x={labelW} y={y + 3} width={w} height={rowH - 8} className="fill-primary" rx={2} />
            <text x={labelW + barW + 8} y={y + 14} className="fill-foreground font-mono text-[11px] tabular-nums">
              {format(d.value)}
            </text>
          </g>
        );
      })}
    </svg>
  );
}

export interface Series {
  name: string;
  points: { x: number; y: number }[];
  dashed?: boolean;
}

export function LineChart({ series, yLabel }: { series: Series[]; yLabel?: string }) {
  const width = 480;
  const height = 180;
  const pad = { l: 36, r: 12, t: 18, b: 26 };
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
            <text key={v} x={sx(v)} y={height - 8} textAnchor="middle" className="fill-muted-foreground font-mono text-[10px] tabular-nums">
              {v}
            </text>
          ))}
        {yLabel && (
          <text x={width - pad.r} y={pad.t - 6} textAnchor="end" className="fill-muted-foreground text-[10px]">
            {yLabel}
          </text>
        )}
        {series.map((s, si) => {
          const pts = s.points.slice().sort((a, b) => a.x - b.x);
          const d = pts.map((p, i) => `${i === 0 ? "M" : "L"}${sx(p.x).toFixed(1)},${sy(p.y).toFixed(1)}`).join(" ");
          const cls = si === 0 ? "stroke-primary" : "stroke-muted-foreground";
          return (
            <g key={s.name}>
              <path d={d} fill="none" className={cls} strokeWidth={1.5} strokeDasharray={s.dashed ? "4 3" : undefined} />
              {pts.map((p) => (
                <circle key={p.x} cx={sx(p.x)} cy={sy(p.y)} r={2.5} className={si === 0 ? "fill-primary" : "fill-muted-foreground"} />
              ))}
            </g>
          );
        })}
      </svg>
      <div className="flex flex-wrap gap-4 text-xs text-muted-foreground">
        {series.map((s, si) => (
          <span key={s.name} className="inline-flex items-center gap-1.5">
            <svg width="18" height="6">
              <line x1="0" x2="18" y1="3" y2="3" className={si === 0 ? "stroke-primary" : "stroke-muted-foreground"} strokeWidth={1.5} strokeDasharray={s.dashed ? "4 3" : undefined} />
            </svg>
            {s.name}
          </span>
        ))}
      </div>
    </div>
  );
}
