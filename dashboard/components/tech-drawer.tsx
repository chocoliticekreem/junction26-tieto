import { ChevronRight, Wrench } from "lucide-react";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";

/** The one place for everything an engineer might want and a decision-maker does not: closed by default. */
export function TechDrawer({ children, summary }: { children: React.ReactNode; summary: string }) {
  return (
    <Collapsible id="technical" className="scroll-mt-28 rounded-lg border border-dashed bg-card/60" data-testid="tech-drawer">
      <CollapsibleTrigger className="group flex w-full items-center gap-2.5 rounded-lg px-4 py-3 text-left transition-colors duration-150 hover:bg-muted/50" data-testid="tech-toggle">
        <Wrench className="size-4 shrink-0 text-muted-foreground" aria-hidden />
        <span className="min-w-0 flex-1">
          <span className="block text-sm font-semibold tracking-tight">Technical details</span>
          <span className="block text-xs text-muted-foreground">{summary}</span>
        </span>
        <ChevronRight className="size-4 shrink-0 text-muted-foreground transition-transform duration-150 group-aria-expanded:rotate-90" aria-hidden />
      </CollapsibleTrigger>
      <CollapsibleContent>
        <div className="space-y-8 border-t border-dashed px-4 py-4">{children}</div>
      </CollapsibleContent>
    </Collapsible>
  );
}
