import { Info } from "lucide-react";
import { READONLY_NOTE } from "@/lib/readonly";

export function DemoNote() {
  return (
    <p className="flex items-center gap-1.5 text-xs text-muted-foreground" data-testid="readonly-note">
      <Info className="size-3.5 shrink-0" aria-hidden />
      {READONLY_NOTE}
    </p>
  );
}
