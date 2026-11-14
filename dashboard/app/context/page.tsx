import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { BookOpenText, CircleCheck, PencilLine } from "lucide-react";
import { readContextMarkdown, readContextStatus, readLedger } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { stripHtmlComments } from "@/lib/humanize";
import { ConfirmContextButton } from "@/components/confirm-context-button";
import { Disclosure } from "@/components/disclosure";
import { EmptyState, PageTitle, Panel, Section } from "@/components/section";
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
      <PageTitle title="Reviewer context" right={<ConfirmContextButton human={human} status={status} />}>
        <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">
          <span
            className={cn(
              "inline-flex h-5 items-center gap-1 rounded-full border px-1.5 text-[11px] font-medium",
              confirmed ? "border-ok-foreground/25 bg-ok text-ok-foreground" : "border-warn-foreground/25 bg-warn text-warn-foreground",
            )}
            data-testid="context-status"
          >
            {confirmed ? <CircleCheck className="size-3" aria-hidden /> : <PencilLine className="size-3" aria-hidden />}
            {confirmed ? "confirmed" : "draft"}
          </span>
          {confirmed && (
            <span className="text-xs text-muted-foreground">
              by {status.confirmed_by} <span className="font-mono">{status.ts}</span>
            </span>
          )}
          <span className="text-xs text-muted-foreground">
            What the reviewer knows about this codebase: deliberate conventions and answered questions. Source: <span className="font-mono">context/REVIEW_CONTEXT.md</span>
          </span>
        </div>
      </PageTitle>

      <Section title="Ledger" count={ledger.length}>
        {ledger.length === 0 ? (
          <p className="text-sm text-muted-foreground">No answers yet.</p>
        ) : (
          <Disclosure label={`${ledger.length} answers by named humans`} testId="ledger-toggle">
            <Panel className="divide-y" data-testid="context-ledger">
              {ledger.map((e, i) => (
                <div key={i} className="space-y-1 p-3 text-sm">
                  <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 font-mono text-[11px] text-muted-foreground">
                    <span>{e.area}</span>
                    <span>{e.change_id}</span>
                    <span className="ml-auto">
                      {e.answered_by} · {e.ts.slice(0, 10)}
                    </span>
                  </div>
                  <div className="text-muted-foreground">{e.question}</div>
                  <div className="font-medium">{e.answer}</div>
                </div>
              ))}
            </Panel>
          </Disclosure>
        )}
      </Section>

      <Section title="Entries" icon={<BookOpenText />}>
        {md ? (
          <Panel className="px-5 py-4">
            <div className="prose-ctx max-w-3xl" data-testid="context-markdown">
              <Markdown remarkPlugins={[remarkGfm]}>{stripHtmlComments(md)}</Markdown>
            </div>
          </Panel>
        ) : (
          <EmptyState>
            No context/REVIEW_CONTEXT.md yet. Run <code className="font-mono">uv run receipts bootstrap-context</code>.
          </EmptyState>
        )}
      </Section>
    </div>
  );
}
