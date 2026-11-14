import { ChevronRight } from "lucide-react";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { cn } from "@/lib/utils";

/** The one disclosure pattern: a chevron + label row, detail below when open. */
export function Disclosure({
  label,
  children,
  testId,
  defaultOpen,
  className,
  triggerClassName,
}: {
  label: React.ReactNode;
  children: React.ReactNode;
  testId?: string;
  defaultOpen?: boolean;
  className?: string;
  triggerClassName?: string;
}) {
  return (
    <Collapsible defaultOpen={defaultOpen} className={className}>
      <CollapsibleTrigger
        className={cn("group inline-flex max-w-full items-center gap-1 text-left text-xs text-muted-foreground hover:text-foreground", triggerClassName)}
        data-testid={testId}
      >
        <ChevronRight className="size-3 shrink-0 transition-transform group-aria-expanded:rotate-90" />
        <span className="min-w-0">{label}</span>
      </CollapsibleTrigger>
      <CollapsibleContent>
        <div className="mt-2 min-w-0">{children}</div>
      </CollapsibleContent>
    </Collapsible>
  );
}
