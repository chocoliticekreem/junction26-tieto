import { Info } from "lucide-react";
import { humanizeVerdict, type HumanizeCtx } from "@/lib/humanize";
import type { VerdictKind } from "@/lib/types";
import { Disclosure } from "./disclosure";
import { VerdictPill } from "./pills";
import { cn } from "@/lib/utils";

/**
 * The verdict in plain language: a headline, a short list of human reasons, and the pipeline's raw
 * reason one click away. Prose reasons (not a machine shape) are shown as-is.
 */
export function VerdictSummary({
  verdict,
  reason,
  ctx,
  compact,
  className,
}: {
  verdict: VerdictKind;
  reason: string;
  ctx?: HumanizeCtx;
  compact?: boolean;
  className?: string;
}) {
  const h = humanizeVerdict(verdict, reason, ctx);
  return (
    <div className={cn("min-w-0", className)} data-testid="verdict-summary">
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
        <VerdictPill verdict={verdict} />
        <span className={cn("font-medium tracking-tight", compact ? "text-sm" : "text-base")} data-testid="verdict-headline">
          {h.headline}
        </span>
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
      {h.mapped && (
        <Disclosure label={<span className="inline-flex items-center gap-1"><Info className="size-3" /> pipeline reason</span>} testId="verdict-raw-toggle" className="mt-1">
          <p className="font-mono text-[11px] leading-5 text-muted-foreground [overflow-wrap:anywhere]" data-testid="verdict-reason" title={h.raw}>
            {h.raw}
          </p>
        </Disclosure>
      )}
    </div>
  );
}
