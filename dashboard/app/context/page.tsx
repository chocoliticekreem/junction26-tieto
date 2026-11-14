import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { readContextMarkdown, readContextStatus, readLedger } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { ConfirmContextButton } from "@/components/confirm-context-button";
import { Mono } from "@/components/pills";
import { EmptyState, PageTitle, Section } from "@/components/section";
import { cn } from "@/lib/utils";

export const dynamic = "force-dynamic";

export default async function ContextPage() {
  const md = readContextMarkdown();
  const status = readContextStatus();
  const ledger = readLedger().slice().reverse();
  const human = await currentHuman();
  const confirmed = status.status === "confirmed";

  return (
    <div className="space-y-8">
      <PageTitle
        title="Reviewer context"
        eyebrow="context/REVIEW_CONTEXT.md"
        right={<ConfirmContextButton human={human} status={status} />}
      >
        <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">
          <span
            className={cn(
              "inline-flex h-5 items-center rounded-sm border px-1.5 text-[11px] font-medium",
              confirmed ? "border-ok-foreground/30 bg-ok text-ok-foreground" : "border-warn-foreground/30 bg-warn text-warn-foreground",
            )}
            data-testid="context-status"
          >
            {confirmed ? "confirmed" : "draft"}
          </span>
          {confirmed ? (
            <span className="text-xs text-muted-foreground">
              by {status.confirmed_by} at <span className="font-mono">{status.ts}</span>
            </span>
          ) : (
            <span className="text-xs text-muted-foreground">Bootstrapped from the repo; a named human has not confirmed it yet. Lenses cite it, but with “draft” provenance.</span>
          )}
        </div>
      </PageTitle>

      <div className="grid gap-8 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <Section title="Context file">
          {md ? (
            <div className="prose-ctx rounded-md border p-4" data-testid="context-markdown">
              <Markdown remarkPlugins={[remarkGfm]}>{md}</Markdown>
            </div>
          ) : (
            <EmptyState>
              No context/REVIEW_CONTEXT.md yet. Run <code className="font-mono">uv run receipts bootstrap-context</code>.
            </EmptyState>
          )}
        </Section>

        <Section title="Ledger" count={ledger.length} hint="answers given by named humans">
          {ledger.length === 0 ? (
            <EmptyState>No ledger entries.</EmptyState>
          ) : (
            <ul className="divide-y rounded-md border" data-testid="context-ledger">
              {ledger.map((e, i) => (
                <li key={i} className="space-y-1 p-3 text-sm">
                  <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 font-mono text-[11px] text-muted-foreground">
                    <Mono>{e.area}</Mono>
                    <span>{e.change_id}</span>
                    <span className="ml-auto">
                      {e.answered_by} · {e.ts.slice(0, 10)}
                    </span>
                  </div>
                  <div className="text-muted-foreground">{e.question}</div>
                  <div className="font-medium">{e.answer}</div>
                </li>
              ))}
            </ul>
          )}
        </Section>
      </div>
    </div>
  );
}
