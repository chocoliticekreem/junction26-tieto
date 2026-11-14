"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";

/** Renders the first `max` children; the rest appear behind one "Show all" button. */
export function ShowMore({ children, max, noun, testId }: { children: React.ReactNode[]; max: number; noun: string; testId?: string }) {
  const [all, setAll] = useState(false);
  const hidden = children.length - max;
  const shown = all || hidden <= 0 ? children : children.slice(0, max);
  return (
    <div className="space-y-2">
      {shown}
      {!all && hidden > 0 && (
        <Button variant="outline" size="sm" onClick={() => setAll(true)} data-testid={testId ?? "show-all"}>
          Show all {children.length} {noun}
        </Button>
      )}
    </div>
  );
}
