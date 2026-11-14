import type { CommandReceipt, Evidence, Finding, Mutant, TestRun } from "@/lib/types";
import { EvidenceCard } from "./evidence-card";
import { Mono, ResultPill, SeverityPill } from "./pills";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { ChevronRight } from "lucide-react";

interface Ctx {
  changeId: string;
  runTs: string;
  commands: CommandReceipt[];
  evidence: Evidence[];
  mutants: Mutant[];
  tests: TestRun[];
}

export function FindingCard({ f, ctx }: { f: Finding; ctx: Ctx }) {
  const evidence = (f.evidence_ids ?? []).map((id) => ctx.evidence.find((e) => e.id === id)).filter((e): e is Evidence => !!e);
  return (
    <Collapsible className="border-l-2 border-l-bad-foreground pl-3" data-testid="finding">
      <CollapsibleTrigger className="group flex w-full items-start gap-2 text-left" data-testid="finding-toggle">
        <ChevronRight className="mt-1 size-3 shrink-0 text-muted-foreground transition-transform group-aria-expanded:rotate-90" />
        <span className="min-w-0 flex-1 space-y-1">
          <span className="block text-sm leading-5 [overflow-wrap:anywhere] group-aria-[expanded=false]:line-clamp-1">{f.claim}</span>
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground [overflow-wrap:anywhere]">
            <SeverityPill severity={f.severity} />
            <Mono>{f.location}</Mono>
            {evidence.map((ev) => (
              <ResultPill key={ev.id} result={ev.result} className="font-mono" />
            ))}
          </span>
        </span>
      </CollapsibleTrigger>
      <CollapsibleContent>
        <div className="mt-2 space-y-2 pl-5">
          <div className="text-xs text-muted-foreground [overflow-wrap:anywhere]">
            <span className="font-mono">{f.lens}</span> · <span className="font-mono">{f.id}</span> · {f.receipt_plan.kind}: {f.receipt_plan.detail}
          </div>
          {evidence.length > 0 ? (
            evidence.map((ev) => <EvidenceCard key={ev.id} ev={ev} ctx={ctx} defaultOpen />)
          ) : (
            <p className="text-xs text-warn-foreground">Marked verified but no evidence record found for {(f.evidence_ids ?? []).join(", ") || "(none)"}.</p>
          )}
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
}

export function HunchList({ hunches }: { hunches: Finding[] }) {
  if (hunches.length === 0) return null;
  return (
    <Collapsible>
      <CollapsibleTrigger className="group inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground" data-testid="hunches-toggle">
        <ChevronRight className="size-3 transition-transform group-aria-expanded:rotate-90" />
        <span>
          <span className="font-mono tabular-nums">{hunches.length}</span> unverified {hunches.length === 1 ? "hunch" : "hunches"} withheld
        </span>
      </CollapsibleTrigger>
      <CollapsibleContent>
        <ul className="mt-2 space-y-2 border-l-2 border-dashed pl-3" data-testid="hunches-list">
          {hunches.map((h) => (
            <li key={h.id} className="text-sm text-muted-foreground" data-testid="hunch">
              <p className="italic [overflow-wrap:anywhere]">{h.claim}</p>
              <div className="mt-0.5 text-xs [overflow-wrap:anywhere]">
                <span className="rounded-sm border border-dashed px-1 py-px text-[10px] uppercase tracking-wide">unverified hunch — no receipt</span> <Mono>{h.location}</Mono>{" "}
                · <span className="font-mono">{h.lens}</span> · <span className="font-mono">{h.id}</span> · planned receipt: {h.receipt_plan.kind} — {h.receipt_plan.detail}
              </div>
            </li>
          ))}
        </ul>
      </CollapsibleContent>
    </Collapsible>
  );
}
