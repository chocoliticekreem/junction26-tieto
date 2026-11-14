import { cn } from "@/lib/utils";
import { Disclosure } from "@/components/disclosure";
import { splitNote } from "@/components/metrics-table";

export function Section({
  title,
  count,
  children,
  className,
  id,
  icon,
}: {
  title: string;
  count?: number;
  children: React.ReactNode;
  className?: string;
  id?: string;
  icon?: React.ReactNode;
}) {
  return (
    <section id={id} className={cn("min-w-0 space-y-2.5 scroll-mt-16", className)}>
      <h2 className="flex items-center gap-1.5 text-sm font-semibold tracking-tight">
        {icon && <span className="text-muted-foreground [&>svg]:size-3.5">{icon}</span>}
        {title}
        {count !== undefined && <span className="font-mono text-xs font-normal tabular-nums text-muted-foreground">{count}</span>}
      </h2>
      {children}
    </section>
  );
}

export function Panel({ children, className, ...props }: React.ComponentProps<"div">) {
  return (
    <div className={cn("rounded-lg border bg-card shadow-[0_1px_0_oklch(0_0_0/3%)]", className)} {...props}>
      {children}
    </div>
  );
}

export function EmptyState({ children }: { children: React.ReactNode }) {
  return <p className="rounded-lg border border-dashed bg-card/50 px-3 py-3 text-sm text-muted-foreground">{children}</p>;
}

export function PageTitle({ eyebrow, title, right, children }: { eyebrow?: string; title: string; right?: React.ReactNode; children?: React.ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0 flex-1">
        <h1 className="text-xl font-semibold tracking-tight sm:text-2xl">
          {title}
          {eyebrow && <span className="ml-2 align-middle font-mono text-xs font-normal text-muted-foreground">{eyebrow}</span>}
        </h1>
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
          <li key={i} className="py-2 [overflow-wrap:anywhere]">
            <span className="font-medium">{x.what}</span> <span className="text-muted-foreground">— {lead}</span>
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
