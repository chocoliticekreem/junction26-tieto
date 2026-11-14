import { Info } from "lucide-react";
import { humanizeVerdict, verdictHeadline, type HumanizeCtx } from "@/lib/humanize";
import type { VerdictKind } from "@/lib/types";
import { Disclosure } from "./disclosure";
import { VerdictPill, verdictLabel } from "./pills";
import { cn } from "@/lib/utils";

/** The pill already names the verdict, so the headline keeps only what it adds: nothing, or the proof. */
function beyondPill(verdict: VerdictKind, headline: string): string | null {
  if (headline === verdictHeadline[verdict]) return null;
  const prefix = `${verdictLabel[verdict]}: `;
  if (!headline.startsWith(prefix)) return headline;
  const rest = headline.slice(prefix.length);
  return rest[0].toUpperCase() + rest.slice(1);
}

/**
 * The verdict in plain language: the pill, what the headline adds beyond it, a short list of human
 * reasons, and the pipeline's raw reason one click away. Prose reasons (not a machine shape) are shown as-is.
 */
export function VerdictSummary({
  verdict,
  reason,
  ctx,
  compact,
  className,
  hideRaw,
}: {
  verdict: VerdictKind;
  reason: string;
  ctx?: HumanizeCtx;
  compact?: boolean;
  className?: string;
  /** Leave the pipeline's raw reason out; the page renders it elsewhere (the technical drawer). */
  hideRaw?: boolean;
}) {
  const h = humanizeVerdict(verdict, reason, ctx);
  const headline = beyondPill(verdict, h.headline);
  return (
    <div className={cn("min-w-0", className)} data-testid="verdict-summary">
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
        <VerdictPill verdict={verdict} />
        {headline && (
          <span className={cn("font-medium tracking-tight", compact ? "text-sm" : "text-base")} data-testid="verdict-headline">
            {headline}
          </span>
        )}
      </div>
      {h.mapped ? (
        h.reasons.length > 0 && (
          <ul className={cn("mt-1.5 flex flex-wrap gap-x-4 gap-y-1 text-muted-foreground", compact ? "text-xs" : "text-sm")} data-testid="verdict-reasons">
            {h.reasons.map((r, i) => (
              <li key={i} className="inline-flex items-center gap-1.5">
                <span className="size-1 shrink-0 rounded-full bg-current opacity-60" aria-hidden />
                {r}
              </li>
            ))}
          </ul>
        )
      ) : (
        <p className={cn("mt-1 max-w-3xl leading-6 text-muted-foreground", compact ? "text-xs leading-5" : "text-sm")} data-testid="verdict-reason">
          {h.reasons[0]}
        </p>
      )}
      {h.mapped && !hideRaw && (
        <Disclosure label={<span className="inline-flex items-center gap-1"><Info className="size-3" /> pipeline reason</span>} testId="verdict-raw-toggle" className="mt-1">
          <p className="font-mono text-[11px] leading-5 text-muted-foreground [overflow-wrap:anywhere]" data-testid="verdict-reason" title={h.raw}>
            {h.raw}
          </p>
        </Disclosure>
      )}
    </div>
  );
}
