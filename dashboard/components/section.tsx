import { cn } from "@/lib/utils";
import { Disclosure } from "@/components/disclosure";
import { splitNote } from "@/components/metrics-table";

export function Section({
  title,
  count,
  children,
  className,
  id,
}: {
  title: string;
  count?: number;
  children: React.ReactNode;
  className?: string;
  id?: string;
}) {
  return (
    <section id={id} className={cn("min-w-0 space-y-2", className)}>
      <h2 className="text-sm font-semibold tracking-tight">
        {title}
        {count !== undefined && <span className="ml-1.5 font-mono text-xs font-normal tabular-nums text-muted-foreground">{count}</span>}
      </h2>
      {children}
    </section>
  );
}

export function EmptyState({ children }: { children: React.ReactNode }) {
  return <p className="rounded-md border border-dashed px-3 py-3 text-sm text-muted-foreground">{children}</p>;
}

export function PageTitle({ eyebrow, title, right, children }: { eyebrow?: string; title: string; right?: React.ReactNode; children?: React.ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0 flex-1">
        {eyebrow && <div className="font-mono text-xs text-muted-foreground">{eyebrow}</div>}
        <h1 className="text-lg font-semibold tracking-tight sm:text-xl">{title}</h1>
        {children}
      </div>
      {right && <div className="flex min-w-0 max-w-full flex-wrap items-center gap-2">{right}</div>}
    </div>
  );
}

/** what — first sentence of why; the rest of a long why sits behind "More". */
export function TightList({ items, testId }: { items: { what: string; why: string }[]; testId?: string }) {
  return (
    <ul className="divide-y text-sm" data-testid={testId}>
      {items.map((x, i) => {
        const { lead, rest } = splitNote(x.why);
        return (
          <li key={i} className="py-1.5 [overflow-wrap:anywhere]">
            {x.what} <span className="text-muted-foreground">— {lead}</span>
            {rest && (
              <Disclosure label="More">
                <p className="text-muted-foreground">{rest}</p>
              </Disclosure>
            )}
          </li>
        );
      })}
    </ul>
  );
}
