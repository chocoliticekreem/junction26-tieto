import { cn } from "@/lib/utils";

export function Section({
  title,
  count,
  hint,
  children,
  className,
  id,
}: {
  title: string;
  count?: number;
  hint?: string;
  children: React.ReactNode;
  className?: string;
  id?: string;
}) {
  return (
    <section id={id} className={cn("min-w-0 space-y-3", className)}>
      <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
        <h2 className="text-sm font-semibold tracking-tight">{title}</h2>
        {count !== undefined && <span className="font-mono text-xs tabular-nums text-muted-foreground">{count}</span>}
        {hint && <span className="text-xs text-muted-foreground">{hint}</span>}
      </div>
      {children}
    </section>
  );
}

export function EmptyState({ children }: { children: React.ReactNode }) {
  return <p className="rounded-md border border-dashed px-3 py-4 text-sm text-muted-foreground">{children}</p>;
}

export function PageTitle({ eyebrow, title, right, children }: { eyebrow?: string; title: string; right?: React.ReactNode; children?: React.ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0">
        {eyebrow && <div className="font-mono text-xs text-muted-foreground">{eyebrow}</div>}
        <h1 className="text-lg font-semibold tracking-tight sm:text-xl">{title}</h1>
        {children}
      </div>
      {right && <div className="flex shrink-0 items-center gap-2">{right}</div>}
    </div>
  );
}

export function KV({ items }: { items: { k: string; v: React.ReactNode }[] }) {
  return (
    <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1 text-sm">
      {items.map((it) => (
        <div key={it.k} className="contents">
          <dt className="text-muted-foreground">{it.k}</dt>
          <dd className="min-w-0 break-words">{it.v}</dd>
        </div>
      ))}
    </dl>
  );
}
