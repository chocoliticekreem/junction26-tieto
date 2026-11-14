"use client";

import { motion, useReducedMotion } from "motion/react";
import { cn } from "@/lib/utils";
import type { VerdictKind } from "@/lib/types";

const tone: Record<VerdictKind | "signed", string> = {
  "auto-ready": "text-ok-foreground",
  "needs-sign-off": "text-warn-foreground",
  "decision-A-B": "text-info-foreground",
  blocked: "text-bad-foreground",
  signed: "text-ok-foreground",
};

/** A rubber stamp that lands on the receipt: scale-down + fade, skipped under prefers-reduced-motion. */
export function Stamp({ kind, children, className, testId, delay = 0.15 }: { kind: VerdictKind | "signed"; children: React.ReactNode; className?: string; testId?: string; delay?: number }) {
  const reduce = useReducedMotion();
  return (
    <motion.span
      className={cn("stamp text-sm sm:text-base", tone[kind], className)}
      initial={reduce ? false : { opacity: 0, scale: 1.6, rotate: -6 }}
      animate={{ opacity: 1, scale: 1, rotate: -6 }}
      transition={{ type: "spring", stiffness: 420, damping: 22, mass: 0.6, delay }}
      data-testid={testId ?? "stamp"}
      data-kind={kind}
    >
      {children}
    </motion.span>
  );
}

/** A short, professional celebration behind a freshly signed stamp: one ring expanding out and fading. */
export function SignedRing() {
  const reduce = useReducedMotion();
  if (reduce) return null;
  return (
    <motion.span
      aria-hidden
      className="pointer-events-none absolute inset-0 rounded-full border-2 border-ok-foreground"
      initial={{ opacity: 0.6, scale: 0.6 }}
      animate={{ opacity: 0, scale: 1.9 }}
      transition={{ duration: 0.7, ease: "easeOut", delay: 0.25 }}
    />
  );
}
