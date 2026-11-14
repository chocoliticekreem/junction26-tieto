"use client";

import { ChevronRight, Radar } from "lucide-react";
import { motion, useReducedMotion } from "motion/react";
import { impactFootnote, impactHeadline, pinState, type PinState } from "@/lib/impact-summary";
import type { ImpactCaller, ImpactMember, ImpactReport } from "@/lib/types";
import { ShowMore } from "./show-more";
import { Term } from "./term";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { cn } from "@/lib/utils";

const MAX_ROWS = 3;
const DOT_STEP = 0.045;

const pinPill: Record<PinState, { label: string; className: string }> = {
  preserved: { label: "old behaviour kept", className: "border-ok-foreground/25 bg-ok text-ok-foreground" },
  changed: { label: "old behaviour changed", className: "border-bad-foreground/25 bg-bad text-bad-foreground" },
  invalid: { label: "pin did not run", className: "border-warn-foreground/25 bg-warn text-warn-foreground" },
  none: { label: "not pinned", className: "border-border bg-muted text-muted-foreground" },
};

function plural(n: number, one: string, many = `${one}s`): string {
  return `${n} ${n === 1 ? one : many}`;
}

/** One caller as a dot: filled = tested, hollow = untested, grey = unknown. Dots fill in one after another on first view. */
function Dot({ caller, index, reduce }: { caller: ImpactCaller; index: number; reduce: boolean }) {
  const tone = caller.tested === true ? "border-foreground/70 bg-foreground/70" : caller.tested === false ? "border-bad-foreground bg-transparent" : "border-muted-foreground/40 bg-muted-foreground/25";
  const word = caller.tested === true ? "tested" : caller.tested === false ? "untested" : "coverage unknown";
  return (
    <motion.span
      className={cn("size-2.5 rounded-full border-[1.5px]", tone)}
      title={`${caller.member} · ${word}`}
      data-testid="impact-dot"
      data-tested={caller.tested === null ? "unknown" : String(caller.tested)}
      initial={reduce ? false : { scale: 0, opacity: 0 }}
      whileInView={{ scale: 1, opacity: 1 }}
      viewport={{ once: true, margin: "0px 0px -10% 0px" }}
      transition={{ type: "spring", stiffness: 520, damping: 26, delay: index * DOT_STEP }}
    />
  );
}

function CallerRow({ c }: { c: ImpactCaller }) {
  const word = c.tested === true ? "tested" : c.tested === false ? "untested" : "coverage unknown";
  const tone = c.tested === true ? "text-ok-foreground" : c.tested === false ? "text-bad-foreground" : "text-muted-foreground";
  return (
    <li className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5 py-1.5 text-xs [overflow-wrap:anywhere]" data-testid="impact-caller">
      <span className="font-mono">{c.member}</span>
      <span className="font-mono text-[11px] text-muted-foreground">
        {c.file.split("/").pop()}:{c.line}
      </span>
      {c.in_change && <span className="rounded-full border border-dashed px-1.5 py-px text-[10px] text-muted-foreground">part of this change</span>}
      <span className={cn("ml-auto font-medium", tone)}>{word}</span>
    </li>
  );
}

function MemberRow({ impact, m, dotOffset, reduce }: { impact: ImpactReport; m: ImpactMember; dotOffset: number; reduce: boolean }) {
  const pin = pinState(impact, m);
  const outside = m.callers.filter((c) => !c.in_change);
  const pill = pinPill[pin.state];
  const places = m.callers_outside_change;
  return (
    <Collapsible className="rounded-lg border bg-card shadow-[0_1px_0_oklch(0_0_0/3%)]" data-testid="impact-member" data-kind={m.kind} data-pin={pin.state}>
      <CollapsibleTrigger className="group flex w-full flex-wrap items-center gap-x-3 gap-y-1.5 rounded-lg px-3 py-2.5 text-left transition-colors duration-150 hover:bg-muted/50" data-testid="impact-member-toggle">
        <ChevronRight className="size-3.5 shrink-0 text-muted-foreground transition-transform duration-150 group-aria-expanded:rotate-90" aria-hidden />
        <span className="min-w-0 flex-1 basis-40 space-y-0.5">
          <span className="block font-mono text-sm [overflow-wrap:anywhere]" title={m.file}>
            {m.name}
          </span>
          <span className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-muted-foreground">
            <span>{m.kind === "deleted" ? "deleted" : "modified"}</span>
            <span>·</span>
            <span data-testid="impact-places">{places === 0 ? "nothing else uses it" : `used in ${plural(places, "other place")}`}</span>
            {m.signature_changed && <span className="rounded-full border border-warn-foreground/25 bg-warn px-1.5 py-px text-[10px] font-medium text-warn-foreground">signature changed</span>}
            {m.ambiguous && (
              <span className="rounded-full border border-dashed px-1.5 py-px text-[10px]" title="Another member shares this name, so some call sites may belong to it instead.">
                name shared
              </span>
            )}
          </span>
        </span>
        <span className="flex items-center gap-1" aria-label={`${outside.length} callers: ${outside.filter((c) => c.tested === true).length} tested, ${outside.filter((c) => c.tested === false).length} untested`} data-testid="impact-dots">
          {outside.slice(0, 24).map((c, i) => (
            <Dot key={`${c.file}:${c.line}`} caller={c} index={dotOffset + i} reduce={reduce} />
          ))}
          {outside.length > 24 && <span className="font-mono text-[10px] text-muted-foreground">+{outside.length - 24}</span>}
        </span>
        <span
          className={cn("inline-flex h-5 shrink-0 items-center whitespace-nowrap rounded-full border px-1.5 text-[11px] font-medium leading-none", pill.className)}
          title={pin.state === "none" ? "No characterization test was written for this function." : pin.evidenceId ? `receipt ${pin.evidenceId}` : undefined}
          data-testid="impact-pin"
        >
          {pill.label}
          {pin.tests > 0 && <span className="ml-1 font-mono tabular-nums opacity-80">{pin.tests}</span>}
        </span>
      </CollapsibleTrigger>
      <CollapsibleContent>
        <div className="border-t px-3 py-1.5">
          {m.callers.length === 0 ? (
            <p className="py-1.5 text-xs text-muted-foreground">No call sites found.</p>
          ) : (
            <ul className="divide-y" data-testid="impact-callers">
              {m.callers.map((c) => (
                <CallerRow key={`${c.file}:${c.line}:${c.member}`} c={c} />
              ))}
            </ul>
          )}
          {pin.evidenceId && (
            <a href={`#evidence-${pin.evidenceId}`} className="inline-block py-1.5 font-mono text-[11px] text-primary underline-offset-4 hover:underline">
              {pin.evidenceId}
            </a>
          )}
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
}

/** Step 2 of the change story: whether a change to shared code can break features it was not about. */
export function ImpactStep({ impact }: { impact?: ImpactReport }) {
  const reduce = useReducedMotion() ?? false;
  if (!impact) {
    return (
      <p className="text-sm text-muted-foreground" data-testid="impact-predates">
        <Radar className="mr-1.5 inline size-3.5 align-[-2px]" aria-hidden />
        This run predates the impact check, so we cannot say what else in the product this change touches.
      </p>
    );
  }
  const touched = impact.members.filter((m) => m.kind !== "new").sort((a, b) => b.callers_outside_change - a.callers_outside_change);
  const fresh = impact.members.filter((m) => m.kind === "new");
  const headline = impactHeadline(impact);
  const changed = impact.characterization.some((c) => c.result === "changed");
  const safe = touched.length === 0;
  let dotOffset = 0;

  return (
    <div className="space-y-3" id="impact-body">
      <p className={cn("text-[15px] leading-6 [overflow-wrap:anywhere]", changed ? "text-bad-foreground" : safe ? "text-ok-foreground" : "text-foreground")} data-testid="impact-headline">
        {headline}
      </p>

      {touched.length > 0 && (
        <div className="space-y-2" data-testid="blast-radius">
          <h3 className="flex items-center gap-1.5 text-sm font-semibold tracking-tight">
            <Term k="blast">Blast radius</Term>
            <span className="font-mono text-xs font-normal tabular-nums text-muted-foreground">{touched.length}</span>
          </h3>
          <ShowMore max={MAX_ROWS} noun="functions" testId="show-all-members">
            {touched.map((m) => {
              const row = <MemberRow key={m.name} impact={impact} m={m} dotOffset={dotOffset} reduce={reduce} />;
              dotOffset += Math.min(24, m.callers.filter((c) => !c.in_change).length);
              return row;
            })}
          </ShowMore>
          <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] text-muted-foreground">
            <span className="inline-flex items-center gap-1">
              <span className="size-2 rounded-full border-[1.5px] border-foreground/70 bg-foreground/70" aria-hidden /> tested <Term k="caller">caller</Term>
            </span>
            <span className="inline-flex items-center gap-1">
              <span className="size-2 rounded-full border-[1.5px] border-bad-foreground" aria-hidden /> untested
            </span>
            <span className="inline-flex items-center gap-1">
              <span className="size-2 rounded-full border-[1.5px] border-muted-foreground/40 bg-muted-foreground/25" aria-hidden /> unknown
            </span>
            <span>
              · <Term k="pinned">pinned</Term> = old behaviour captured in a test before the change
            </span>
          </p>
        </div>
      )}

      {fresh.length > 0 && (
        <p className="text-sm text-muted-foreground [overflow-wrap:anywhere]" data-testid="impact-new">
          <span className="font-medium text-foreground">New:</span> {fresh.map((m) => (
            <span key={m.name}>
              <span className="font-mono text-[13px]">{m.name}</span>
              {m !== fresh[fresh.length - 1] && ", "}
            </span>
          ))}{" "}
          (only this change uses {fresh.length === 1 ? "it" : "them"})
        </p>
      )}

      <p className="text-xs text-muted-foreground" data-testid="impact-footnote">
        {impactFootnote(impact)}
      </p>
    </div>
  );
}
