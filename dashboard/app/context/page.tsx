import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { BookOpenText, CircleCheck, MessageCircleQuestion, PencilLine } from "lucide-react";
import { readContextMarkdown, readContextStatus, readLedger } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { stripHtmlComments } from "@/lib/humanize";
import { ConfirmContextButton } from "@/components/confirm-context-button";
import { EmptyState, Panel, Section } from "@/components/section";
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
      <div>
        <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">Product context</h1>
        <p className="mt-1 max-w-3xl text-[15px] text-muted-foreground">
          What the reviewer knows about your product: the conventions it must respect, and the answers you gave it. Until you confirm this, it treats these notes as hints; once confirmed, as facts.
        </p>
      </div>

      <Panel className="flex flex-wrap items-center justify-between gap-3 p-4 sm:p-5" data-testid="context-status-bar">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span
            className={cn(
              "inline-flex h-6 items-center gap-1 rounded-full border px-2 text-xs font-medium",
              confirmed ? "border-ok-foreground/25 bg-ok text-ok-foreground" : "border-warn-foreground/25 bg-warn text-warn-foreground",
            )}
            data-testid="context-status"
          >
            {confirmed ? <CircleCheck className="size-3" aria-hidden /> : <PencilLine className="size-3" aria-hidden />}
            {confirmed ? "confirmed" : "draft"}
          </span>
          {confirmed ? (
            <span className="text-muted-foreground">
              Confirmed by <span className="font-medium text-foreground">{status.confirmed_by}</span> <span className="font-mono text-xs">{status.ts}</span>. The reviewer drops findings that contradict these notes.
            </span>
          ) : (
            <span className="text-muted-foreground">Not confirmed yet: the reviewer keeps matching findings as hunches instead of dropping them.</span>
          )}
        </div>
        <ConfirmContextButton human={human} status={status} />
      </Panel>

      <Section title="Answers you gave" count={ledger.length} icon={<MessageCircleQuestion />}>
        {ledger.length === 0 ? (
          <p className="text-sm text-muted-foreground">No answers yet. When a verdict depends on intent, the reviewer asks you once and records the answer here.</p>
        ) : (
          <Panel className="divide-y" data-testid="context-ledger">
            {ledger.map((e, i) => (
              <div key={i} className="space-y-1 p-3.5 text-sm">
                <div className="text-muted-foreground">{e.question}</div>
                <div className="font-medium">{e.answer}</div>
                <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 font-mono text-[11px] text-muted-foreground">
                  <span>{e.area}</span>
                  <span>{e.change_id}</span>
                  <span className="ml-auto">
                    {e.answered_by} · {e.ts.slice(0, 10)}
                  </span>
                </div>
              </div>
            ))}
          </Panel>
        )}
      </Section>

      <Section title="What the reviewer was told" icon={<BookOpenText />}>
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
