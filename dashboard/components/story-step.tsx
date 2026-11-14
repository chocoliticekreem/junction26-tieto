import { cn } from "@/lib/utils";

/** One numbered step of the change story. The id is what the step indicator observes and scrolls to. */
export function StoryStep({ id, n, title, children, className, aside }: { id: string; n: number; title: string; children: React.ReactNode; className?: string; aside?: React.ReactNode }) {
  return (
    <section id={id} className={cn("min-w-0 scroll-mt-28 space-y-3", className)} data-testid={`story-${id}`} aria-labelledby={`${id}-title`}>
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 id={`${id}-title`} className="flex items-center gap-2.5 text-lg font-semibold tracking-tight">
          <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-primary font-mono text-xs text-primary-foreground tabular-nums" aria-hidden>
            {n}
          </span>
          {title}
        </h2>
        {aside}
      </div>
      {children}
    </section>
  );
}
