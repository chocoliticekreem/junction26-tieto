import Link from "next/link";
import { BookOpenText, CircleCheck, MessageCircleQuestion, PencilLine } from "lucide-react";
import { splitAreas } from "@/lib/context-doc";
import { lensLabel } from "@/lib/humanize";
import type { RunContext, RunContextCitation } from "@/lib/types";
import { ContextNote } from "./context-doc";
import { Disclosure } from "./disclosure";
import { Panel, Section } from "./section";
import { cn } from "@/lib/utils";

function citationText(c: RunContextCitation): React.ReactNode {
  const entry = <q className="font-medium">{c.entry}</q>;
  const claim = <span className="text-muted-foreground">{c.claim}</span>;
  switch (c.effect) {
    case "withdrawn":
      return (
        <>
          Because your context says {entry}, the reviewer did not flag: {claim}
        </>
      );
    case "suppressed-unconfirmed":
      return (
        <>
          Your context says {entry}, but it is not confirmed yet, so this stays a hunch instead of being dropped: {claim}
        </>
      );
    default:
      return (
        <>
          The reviewer leaned on your context {entry} for: {claim}
        </>
      );
  }
}

function plural(n: number, one: string, many = `${one}s`): string {
  return `${n} ${n === 1 ? one : many}`;
}

/** The one line a decision-maker sees: "10 notes used · 1 held back". */
export function contextOneLiner(context: RunContext): string {
  const held = context.citations.filter((c) => c.effect === "suppressed-unconfirmed").length;
  const dropped = context.citations.filter((c) => c.effect === "withdrawn").length;
  const parts = [`${plural(context.entries.length, "note")} used`];
  if (held) parts.push(`${held} held back`);
  if (dropped) parts.push(`${dropped} withdrawn`);
  if (!held && !dropped) parts.push(context.citations.length ? `${plural(context.citations.length, "finding")} shaped by it` : "nothing changed by it");
  return parts.join(" · ");
}

/** What the reviewer was told about the product for this change, collapsed to one line until opened. */
export function ProductContext({ context, areas }: { context?: RunContext; areas: string[] }) {
  const title = "What it knew about your product";
  if (!context) {
    return (
      <div id="product-context" className="scroll-mt-28 text-sm text-muted-foreground" data-testid="product-context">
        <BookOpenText className="mr-1.5 inline size-3.5 align-[-2px]" aria-hidden />
        {title}: <span data-testid="context-predates">This run predates context recording.</span>
      </div>
    );
  }
  return (
    <Disclosure
      label={
        <span className="text-sm text-foreground">
          <BookOpenText className="mr-1.5 inline size-3.5 align-[-2px] text-muted-foreground" aria-hidden />
          <span className="font-medium">{title}:</span> <span className="text-muted-foreground">{contextOneLiner(context)}</span>
        </span>
      }
      testId="product-context-toggle"
      className="scroll-mt-28"
    >
      <ProductContextBody context={context} areas={areas} />
    </Disclosure>
  );
}

function ProductContextBody({ context, areas }: { context: RunContext; areas: string[] }) {
  const confirmed = context.status === "confirmed";
  const shownAreas = context.areas.length ? context.areas : areas;
  return (
    <Section title={null} id="product-context" className="rounded-lg border bg-card/60 px-3.5 py-3">
      <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <span
          className={cn(
            "inline-flex h-5 items-center gap-1 rounded-full border px-1.5 text-[11px] font-medium",
            confirmed ? "border-ok-foreground/25 bg-ok text-ok-foreground" : "border-warn-foreground/25 bg-warn text-warn-foreground",
          )}
          data-testid="run-context-status"
        >
          {confirmed ? <CircleCheck className="size-3" aria-hidden /> : <PencilLine className="size-3" aria-hidden />}
          {confirmed ? `confirmed by ${context.confirmed_by ?? "a named human"}` : "draft"}
        </span>
        {!confirmed && (
          <span>
            The reviewer used these notes as hints, not facts.{" "}
            <Link href="/context" className="font-medium text-primary underline-offset-4 hover:underline" data-testid="run-context-nudge">
              Confirm it on the Context page
            </Link>{" "}
            and it will drop the matching findings outright next time.
          </span>
        )}
        {context.readers_used_ledger > 0 && (
          <span>
            {context.readers_used_ledger} of the independent readers used your earlier answers.
          </span>
        )}
      </div>

      {context.citations.length === 0 ? (
        <p className="text-sm text-muted-foreground" data-testid="context-no-citations">
          None of your product context changed a finding in this review.
        </p>
      ) : (
        <Panel className="divide-y">
          {context.citations.map((c, i) => (
            <div key={i} className="px-3 py-2.5 text-sm leading-6 [overflow-wrap:anywhere]" data-testid="context-citation" data-effect={c.effect}>
              {citationText(c)}
              <div className="mt-0.5 font-mono text-[11px] text-muted-foreground">{lensLabel[c.lens] ?? c.lens} lens</div>
            </div>
          ))}
        </Panel>
      )}

      {context.entries.length > 0 ? (
        <Disclosure label={`${context.entries.length} ${context.entries.length === 1 ? "note" : "notes"} the reviewer was given for ${shownAreas.join(", ") || "this change"}`} testId="context-entries-toggle">
          <ul className="space-y-1" data-testid="context-entries">
            {context.entries.map((e, i) => (
              <ContextNote key={i} areas={splitAreas(e.area)} source={e.source} testId="context-entry">
                {e.text}
                {e.kind === "ledger" && (
                  <span className="ml-1.5 inline-flex items-center gap-0.5 rounded-full border border-info-foreground/25 bg-info px-1.5 py-px align-[2px] text-[10px] font-medium leading-none text-info-foreground" data-testid="context-entry-ledger">
                    <MessageCircleQuestion className="size-3" aria-hidden /> from an answered question
                  </span>
                )}
              </ContextNote>
            ))}
          </ul>
        </Disclosure>
      ) : (
        <p className="text-xs text-muted-foreground">No product notes matched {shownAreas.join(", ") || "this change"}.</p>
      )}
    </Section>
  );
}
