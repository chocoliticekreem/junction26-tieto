import { CircleCheck, CircleX, EyeOff, MessageSquareQuote, Target, UserCheck } from "lucide-react";
import type { PlainSentence } from "@/lib/plain-summary";
import { cn } from "@/lib/utils";

const icon: Record<PlainSentence["key"], React.ComponentType<{ className?: string }>> = {
  purpose: Target,
  proved: CircleCheck,
  wrong: CircleX,
  unchecked: EyeOff,
  decide: UserCheck,
};

const tone: Record<PlainSentence["key"], string> = {
  purpose: "text-muted-foreground",
  proved: "text-ok-foreground",
  wrong: "text-bad-foreground",
  unchecked: "text-muted-foreground",
  decide: "text-primary",
};

/** The receipt in five short sentences a non-engineer can read in 30 seconds; each one links to its evidence. */
export function PlainSummary({ items, className }: { items: PlainSentence[]; className?: string }) {
  return (
    <section className={cn("rounded-lg border bg-card px-4 py-3.5 shadow-[0_1px_0_oklch(0_0_0/3%)]", className)} data-testid="plain-summary" aria-label="in plain words">
      <div className="flex items-center gap-1.5 text-xs font-semibold tracking-tight">
        <MessageSquareQuote className="size-3.5 text-muted-foreground" aria-hidden />
        In plain words
      </div>
      <ol className="mt-2 space-y-1.5">
        {items.map((s) => {
          const Icon = icon[s.key];
          const t = s.key === "wrong" && s.text.startsWith("Nothing was proven wrong") ? "text-ok-foreground" : tone[s.key];
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
