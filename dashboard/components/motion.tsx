"use client";

import { motion, useReducedMotion } from "motion/react";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";

const EASE = [0.22, 1, 0.36, 1] as const;

/** Page content fades and slides up once per navigation; a router.refresh keeps the same key, so it never re-plays. */
export function PageEnter({ children, className }: { children: React.ReactNode; className?: string }) {
  const reduce = useReducedMotion();
  const pathname = usePathname();
  return (
    <motion.div
      key={pathname}
      className={cn("min-w-0", className)}
      initial={reduce ? false : { opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.26, ease: EASE }}
    >
      {children}
    </motion.div>
  );
}

/** Children stagger in on mount (queue cards). */
export function StaggerList({ children, className, as: Tag = "div" }: { children: React.ReactNode; className?: string; as?: "div" | "ul" | "ol" }) {
  const reduce = useReducedMotion();
  const M = Tag === "ul" ? motion.ul : Tag === "ol" ? motion.ol : motion.div;
  return (
    <M
      className={className}
      initial={reduce ? false : "hidden"}
      animate="show"
      variants={{ hidden: {}, show: { transition: { staggerChildren: 0.06, delayChildren: 0.05 } } }}
    >
      {children}
    </M>
  );
}

export function StaggerItem({ children, className, as: Tag = "div", ...rest }: { children: React.ReactNode; className?: string; as?: "div" | "li" } & Record<string, unknown>) {
  const reduce = useReducedMotion();
  const M = Tag === "li" ? motion.li : motion.div;
  return (
    <M
      className={className}
      variants={{ hidden: reduce ? {} : { opacity: 0, y: 12 }, show: { opacity: 1, y: 0, transition: { duration: 0.28, ease: EASE } } }}
      {...rest}
    >
      {children}
    </M>
  );
}
