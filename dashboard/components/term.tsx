"use client";

import { useId, useState } from "react";
import { glossary, type TermKey } from "@/lib/glossary";
import { cn } from "@/lib/utils";

/**
 * A jargon word with a dotted underline; hover, focus or tap shows its one-sentence definition.
 * A span, not a button: it also sits inside receipt-line links and disclosure triggers.
 */
export function Term({ k, children, className }: { k: TermKey; children: React.ReactNode; className?: string }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  return (
    <span className={cn("relative inline", className)}>
      <span
        role="button"
        tabIndex={0}
        className="cursor-help rounded-sm underline decoration-dotted decoration-muted-foreground/70 underline-offset-[3px] hover:decoration-foreground"
        aria-describedby={open ? id : undefined}
        aria-expanded={open}
        data-testid={`term-${k}`}
        onClick={() => setOpen((v) => !v)}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            e.stopPropagation();
            setOpen((v) => !v);
          }
        }}
        onMouseEnter={() => setOpen(true)}
        onMouseLeave={() => setOpen(false)}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
      >
        {children}
      </span>
      {open && (
        <span
          id={id}
          role="tooltip"
          data-testid="term-tip"
          className="pointer-events-none absolute top-full left-0 z-30 mt-1.5 w-64 max-w-[80vw] rounded-md border bg-popover px-2.5 py-2 text-left font-sans text-xs font-normal leading-5 tracking-normal normal-case text-popover-foreground shadow-md"
        >
          {glossary[k]}
        </span>
      )}
    </span>
  );
}
