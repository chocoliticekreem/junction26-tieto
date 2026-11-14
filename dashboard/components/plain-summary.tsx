import { CircleCheck, CircleX, EyeOff, MessageSquareQuote, Radar, Target, UserCheck } from "lucide-react";
import type { PlainSentence } from "@/lib/plain-summary";
import { cn } from "@/lib/utils";

const icon: Record<PlainSentence["key"], React.ComponentType<{ className?: string }>> = {
  purpose: Target,
  impact: Radar,
  proved: CircleCheck,
  wrong: CircleX,
  unchecked: EyeOff,
  decide: UserCheck,
};

const tone: Record<PlainSentence["key"], string> = {
  purpose: "text-muted-foreground",
  impact: "text-muted-foreground",
  proved: "text-ok-foreground",
  wrong: "text-bad-foreground",
  unchecked: "text-muted-foreground",
  decide: "text-primary",
};

/** The receipt in five short sentences a non-engineer can read in 30 seconds; each one links to its evidence. */
export function PlainSummary({ items, className, bare }: { items: PlainSentence[]; className?: string; bare?: boolean }) {
  return (
    <section className={cn(!bare && "rounded-lg border bg-card px-4 py-3.5 shadow-[0_1px_0_oklch(0_0_0/3%)]", className)} data-testid="plain-summary" aria-label="in plain words">
      {!bare && (
        <div className="flex items-center gap-1.5 text-xs font-semibold tracking-tight">
          <MessageSquareQuote className="size-3.5 text-muted-foreground" aria-hidden />
          In plain words
        </div>
      )}
      <ol className={cn("space-y-1.5", !bare && "mt-2")}>
        {items.map((s) => {
          const Icon = icon[s.key];
          const t =
            (s.key === "wrong" && s.text.startsWith("Nothing was proven wrong")) || (s.key === "impact" && /can't break other features/.test(s.text))
              ? "text-ok-foreground"
              : s.key === "impact" && /changed for/.test(s.text)
                ? "text-bad-foreground"
                : tone[s.key];
          return (
            <li key={s.key} className="flex items-start gap-2.5" data-testid={`plain-${s.key}`}>
              <Icon className={cn("mt-1 size-4 shrink-0", t)} aria-hidden />
              <a href={s.href} className="min-w-0 text-[15px] leading-6 underline-offset-4 hover:underline [overflow-wrap:anywhere]">
                {s.text}
              </a>
            </li>
          );
        })}
      </ol>
    </section>
  );
}
