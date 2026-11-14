import { Children, type ReactNode } from "react";
import Markdown, { type Components } from "react-markdown";
import remarkGfm from "remark-gfm";
import { AREA_TAG, SOURCE_TAG, splitAreas } from "@/lib/context-doc";
import { cn } from "@/lib/utils";

/** The code areas a note applies to, as small tags. */
export function AreaChips({ areas, className }: { areas: string[]; className?: string }) {
  if (areas.length === 0) return null;
  return (
    <span className={cn("inline-flex flex-wrap items-center gap-1 align-[2px]", className)}>
      {areas.map((a) => (
        <span key={a} className="inline-flex h-[18px] items-center rounded-full border bg-muted px-1.5 font-mono text-[10px] leading-none text-muted-foreground" data-testid="area-chip">
          {a}
        </span>
      ))}
    </span>
  );
}

/** Where a note came from (a file path, a ledger id, "git log"): mono, muted, at the end of the line. */
export function SourceCite({ source, className }: { source: string; className?: string }) {
  return (
    <span className={cn("font-mono text-[11px] text-muted-foreground [overflow-wrap:anywhere]", className)} data-testid="source-cite">
      {source}
    </span>
  );
}

/** One note: tag chips, the text (inline markdown kept), the source citation. */
export function ContextNote({ areas, source, children, testId }: { areas: string[]; source?: string; children: ReactNode; testId?: string }) {
  return (
    <li className="flex items-start gap-2.5 text-sm leading-6 [overflow-wrap:anywhere]" data-testid={testId}>
      <span className="mt-[11px] size-1 shrink-0 rounded-full bg-muted-foreground/60" aria-hidden />
      <span className="min-w-0">
        <AreaChips areas={areas} className="mr-1.5" />
        {children}
        {source && <SourceCite source={source} className="ml-1.5" />}
      </span>
    </li>
  );
}

/** A markdown bullet whose leading "[area: ...]" and trailing "(source: ...)" become chips and a citation. */
function Bullet({ children }: { children?: ReactNode }) {
  const kids = Children.toArray(children);
  let areas: string[] = [];
  let source: string | undefined;
  const first = kids[0];
  if (typeof first === "string") {
    const m = first.match(AREA_TAG);
    if (m) {
      areas = splitAreas(m[1]);
      kids[0] = first.slice(m[0].length);
    }
  }
  const lastIndex = kids.length - 1;
  const last = kids[lastIndex];
  if (typeof last === "string") {
    const m = last.match(SOURCE_TAG);
    if (m) {
      source = m[1].trim();
      kids[lastIndex] = last.slice(0, m.index);
    }
  }
  return (
    <ContextNote areas={areas} source={source} testId="context-note">
      {kids}
    </ContextNote>
  );
}

function textOf(node: ReactNode): string {
  return Children.toArray(node)
    .map((c) => (typeof c === "string" || typeof c === "number" ? String(c) : typeof c === "object" && c && "props" in c ? textOf((c.props as { children?: ReactNode }).children) : ""))
    .join("");
}

const components: Components = {
  h1: () => null,
  li: ({ children }) => <Bullet>{children}</Bullet>,
  ul: ({ children }) => <ul className="my-2 space-y-1">{children}</ul>,
  p: ({ children }) => {
    if (/^\s*No decisions/i.test(textOf(children))) {
      return (
        <p className="my-2 text-sm text-muted-foreground" data-testid="context-no-decisions">
          No decisions yet.
        </p>
      );
    }
    return <p className="my-2 text-sm leading-6">{children}</p>;
  },
};

/** REVIEW_CONTEXT.md body as a document: headings, tagged notes, tables, inline code. */
export function ContextDoc({ markdown, className }: { markdown: string; className?: string }) {
  return (
    <div className={cn("prose-ctx max-w-3xl", className)} data-testid="context-markdown">
      <Markdown remarkPlugins={[remarkGfm]} components={components}>
        {markdown}
      </Markdown>
    </div>
  );
}
