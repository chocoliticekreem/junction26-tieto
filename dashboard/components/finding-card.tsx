import type { CommandReceipt, Evidence, Finding, Mutant, TestRun } from "@/lib/types";
import { lensLabel } from "@/lib/humanize";
import { EvidenceCard } from "./evidence-card";
import { ResultIcon, SeverityPill } from "./pills";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { ChevronRight, ShieldAlert } from "lucide-react";

interface Ctx {
  changeId: string;
  runTs: string;
  commands: CommandReceipt[];
  evidence: Evidence[];
  mutants: Mutant[];
  tests: TestRun[];
}

const planLabel: Record<Finding["receipt_plan"]["kind"], string> = {
  test: "a test",
  mutation: "a mutant",
  "byte-diff": "a byte diff",
  regeneration: "regeneration",
  "ledger-citation": "a ledger entry",
  "code-citation": "a code citation",
};

export function FindingCard({ f, ctx }: { f: Finding; ctx: Ctx }) {
  const evidence = (f.evidence_ids ?? []).map((id) => ctx.evidence.find((e) => e.id === id)).filter((e): e is Evidence => !!e);
  return (
    <Collapsible className="rounded-lg border bg-card shadow-[0_1px_0_oklch(0_0_0/3%)]" data-testid="finding">
      <CollapsibleTrigger className="group flex w-full items-start gap-2.5 rounded-lg px-3 py-2.5 text-left transition-colors duration-150 hover:bg-muted/50" data-testid="finding-toggle">
        <ShieldAlert className="mt-0.5 size-4 shrink-0 text-bad-foreground" aria-hidden />
        <span className="min-w-0 flex-1 space-y-1">
          <span className="block text-sm leading-5 [overflow-wrap:anywhere] group-aria-[expanded=false]:line-clamp-2">{f.claim}</span>
          <span className="flex flex-wrap items-center gap-x-2.5 gap-y-1 text-xs text-muted-foreground [overflow-wrap:anywhere]">
            <SeverityPill severity={f.severity} />
            <span className="font-mono text-[11px]">{f.location}</span>
            {evidence.map((ev) => (
              <span key={ev.id} className="inline-flex items-center gap-1">
                <ResultIcon result={ev.result} />
                <span className="font-mono text-[11px]">{ev.id}</span>
              </span>
            ))}
          </span>
        </span>
        <ChevronRight className="mt-1 size-3.5 shrink-0 text-muted-foreground transition-transform duration-150 group-aria-expanded:rotate-90" />
      </CollapsibleTrigger>
      <CollapsibleContent>
        <div className="space-y-2 border-t px-3 py-2.5">
          <div className="text-xs text-muted-foreground [overflow-wrap:anywhere]">
            {lensLabel[f.lens] ?? f.lens} lens · proved by {planLabel[f.receipt_plan.kind] ?? f.receipt_plan.kind}: {f.receipt_plan.detail}
            <span className="ml-2 font-mono text-[11px]">{f.id}</span>
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
      <CollapsibleTrigger className="group inline-flex items-center gap-1 rounded-sm text-xs text-muted-foreground transition-colors duration-150 hover:text-foreground" data-testid="hunches-toggle">
        <ChevronRight className="size-3 transition-transform duration-150 group-aria-expanded:rotate-90" />
        <span>
          <span className="font-mono tabular-nums">{hunches.length}</span> unverified {hunches.length === 1 ? "hunch" : "hunches"} withheld
        </span>
      </CollapsibleTrigger>
      <CollapsibleContent>
        <ul className="mt-2 space-y-2 rounded-lg border border-dashed px-3 py-2" data-testid="hunches-list">
          {hunches.map((h) => (
            <li key={h.id} className="text-sm text-muted-foreground" data-testid="hunch">
              <p className="italic [overflow-wrap:anywhere]">{h.claim}</p>
              <div className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs [overflow-wrap:anywhere]">
                <span className="rounded-full border border-dashed px-1.5 py-px text-[10px] tracking-wide">unverified hunch — no receipt</span>
                <span className="font-mono text-[11px]">{h.location}</span>
                <span>
                  {lensLabel[h.lens] ?? h.lens} · would need {planLabel[h.receipt_plan.kind] ?? h.receipt_plan.kind}: {h.receipt_plan.detail}
                </span>
              </div>
            </li>
          ))}
        </ul>
      </CollapsibleContent>
    </Collapsible>
  );
}
