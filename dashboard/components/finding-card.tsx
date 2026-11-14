import type { CommandReceipt, Evidence, Finding, Mutant, TestRun } from "@/lib/types";
import { EvidenceCard } from "./evidence-card";
import { Mono, SeverityPill } from "./pills";
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
    <article className="space-y-3 rounded-md border-l-2 border-l-bad-foreground bg-card p-3 pl-4 shadow-xs" data-testid="finding">
      <div className="flex flex-wrap items-center gap-2">
        <SeverityPill severity={f.severity} />
        <span className="font-mono text-[11px] text-muted-foreground">{f.lens}</span>
        <span className="font-mono text-[11px] text-muted-foreground">{f.id}</span>
      </div>
      <p className="text-sm leading-6">{f.claim}</p>
      <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
        <span>
          at <Mono>{f.location}</Mono>
        </span>
        <span>
          receipt: {f.receipt_plan.kind} — {f.receipt_plan.detail}
        </span>
      </div>
      {evidence.length > 0 ? (
        <div className="space-y-2">
          {evidence.map((ev) => (
            <EvidenceCard key={ev.id} ev={ev} ctx={ctx} />
          ))}
        </div>
      ) : (
        <p className="text-xs text-warn-foreground">Marked verified but no evidence record found for {(f.evidence_ids ?? []).join(", ") || "(none)"}.</p>
      )}
    </article>
  );
}

export function HunchList({ hunches }: { hunches: Finding[] }) {
  if (hunches.length === 0) return <p className="text-sm text-muted-foreground">No unverified hunches withheld.</p>;
  return (
    <Collapsible>
      <CollapsibleTrigger
        className="group flex w-full items-center gap-2 rounded-md border border-dashed px-3 py-2 text-left text-sm text-muted-foreground hover:bg-muted/50"
        data-testid="hunches-toggle"
      >
        <ChevronRight className="size-3.5 transition-transform group-aria-expanded:rotate-90" />
        <span>
          <span className="font-mono tabular-nums text-foreground">{hunches.length}</span> unverified {hunches.length === 1 ? "hunch" : "hunches"} withheld — no receipt, not shown as findings
        </span>
      </CollapsibleTrigger>
      <CollapsibleContent>
        <ul className="mt-2 space-y-2" data-testid="hunches-list">
          {hunches.map((h) => (
            <li key={h.id} className="rounded-md border border-dashed bg-muted/30 p-3 text-sm text-muted-foreground" data-testid="hunch">
              <div className="mb-1 flex flex-wrap items-center gap-2 text-[11px] uppercase tracking-wide">
                <span className="rounded-sm border border-dashed px-1.5 py-0.5">unverified hunch — no receipt</span>
                <span className="font-mono normal-case tracking-normal">{h.lens}</span>
                <span className="font-mono normal-case tracking-normal">{h.id}</span>
              </div>
              <p className="italic">{h.claim}</p>
              <div className="mt-1 text-xs">
                at <Mono>{h.location}</Mono> · planned receipt: {h.receipt_plan.kind} — {h.receipt_plan.detail}
              </div>
            </li>
          ))}
        </ul>
      </CollapsibleContent>
    </Collapsible>
  );
}
