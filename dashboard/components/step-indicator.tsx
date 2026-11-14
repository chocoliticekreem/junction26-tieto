"use client";

import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

export interface Step {
  id: string;
  label: string;
}

/** Sticky 1-2-3-4 strip for the change story: the active step follows the scroll, a click scrolls smoothly. */
export function StepIndicator({ steps, className }: { steps: Step[]; className?: string }) {
  const [active, setActive] = useState(steps[0]?.id);

  useEffect(() => {
    const els = steps.map((s) => document.getElementById(s.id)).filter((e): e is HTMLElement => !!e);
    if (els.length === 0) return;
    let raf = 0;
    // The active step is the last one whose top has passed the reading line (a third of the viewport);
    // at the very bottom of the page the last step wins even if it never reaches that line.
    const compute = () => {
      raf = 0;
      const line = window.innerHeight * 0.34;
      const atBottom = window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 2;
      let current = els[0].id;
      for (const el of els) if (el.getBoundingClientRect().top <= line) current = el.id;
      if (atBottom) current = els[els.length - 1].id;
      setActive(current);
    };
    const schedule = () => {
      if (!raf) raf = requestAnimationFrame(compute);
    };
    // IntersectionObserver wakes the computation as steps enter or leave; scroll keeps it current in between.
    const io = new IntersectionObserver(schedule, { threshold: [0, 0.25, 0.5, 0.75, 1] });
    els.forEach((el) => io.observe(el));
    window.addEventListener("scroll", schedule, { passive: true });
    window.addEventListener("resize", schedule);
    compute();
    return () => {
      io.disconnect();
      window.removeEventListener("scroll", schedule);
      window.removeEventListener("resize", schedule);
      if (raf) cancelAnimationFrame(raf);
    };
  }, [steps]);

  function go(id: string) {
    const el = document.getElementById(id);
    if (!el) return;
    const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    el.scrollIntoView({ behavior: reduce ? "auto" : "smooth", block: "start" });
    history.replaceState(null, "", `#${id}`);
  }

  return (
    <nav
      className={cn("sticky top-[3.3rem] z-10 -mx-4 border-b bg-background/85 px-4 py-2 backdrop-blur supports-[backdrop-filter]:bg-background/70 sm:-mx-6 sm:px-6", className)}
      aria-label="Steps"
      data-testid="step-indicator"
    >
      <ol className="flex gap-1 overflow-x-auto text-xs sm:gap-2 sm:text-sm">
        {steps.map((s, i) => {
          const on = s.id === active;
          return (
            <li key={s.id} className="shrink-0">
              <button
                type="button"
                onClick={() => go(s.id)}
                aria-current={on ? "step" : undefined}
                data-testid={`step-${s.id}`}
                className={cn(
                  "inline-flex items-center gap-1.5 rounded-md px-2 py-1 whitespace-nowrap transition-colors duration-200 hover:bg-muted hover:text-foreground",
                  on ? "font-medium text-foreground" : "text-muted-foreground",
                )}
              >
                <span
                  className={cn(
                    "flex size-5 items-center justify-center rounded-full border font-mono text-[11px] tabular-nums transition-colors duration-200",
                    on ? "border-primary bg-primary text-primary-foreground" : "border-border bg-card",
                  )}
                  aria-hidden
                >
                  {i + 1}
                </span>
                {s.label}
              </button>
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
