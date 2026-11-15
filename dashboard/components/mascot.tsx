"use client";

import { motion, useReducedMotion } from "motion/react";
import { cn } from "@/lib/utils";

export type SlipPose =
  | "idle"
  | "reviewing"
  | "stamp-blocked"
  | "stamp-signed"
  | "thinking"
  | "empty"
  | "pinning"
  | "hero"
  | "shrug"
  | "waving"
  | "pointing"
  | "celebrating"
  | "question"
  | "reading"
  | "thumbs-up";

const LABEL: Record<SlipPose, string> = {
  idle: "Slip, the PullReceipts mascot",
  reviewing: "Slip in reading glasses, magnifier over a receipt line",
  "stamp-blocked": "Slip stamping a change as blocked",
  "stamp-signed": "Slip raising the stamp after a sign-off",
  thinking: "Slip weighing reading A against reading B",
  empty: "Slip resting, nothing to review",
  pinning: "Slip pinning down the old behaviour",
  hero: "Slip stamping a receipt as it prints",
  shrug: "Slip shrugging: not checked",
  waving: "Slip waving hello",
  pointing: "Slip pointing at a result",
  celebrating: "Slip celebrating under confetti",
  question: "Slip holding up a question mark",
  reading: "Slip reading a long receipt",
  "thumbs-up": "Slip giving a thumbs up",
};

const STAMP_POSES: SlipPose[] = ["stamp-blocked", "stamp-signed"];
const BOB_POSES: SlipPose[] = ["idle", "empty", "thinking", "reading"];

/* Hand-drawn, risograph-printed Slip. Two files per pose (light and dark inks) under public/mascot/hand. */
function Art({ file, alt }: { file: string; alt: string }) {
  return (
    <>
      {/* eslint-disable-next-line @next/next/no-img-element -- static SVG art with inline filters; next/image would rasterise it */}
      <img src={`/mascot/hand/${file}.svg`} alt={alt} draggable={false} className="block size-full select-none dark:hidden" />
      {/* eslint-disable-next-line @next/next/no-img-element -- dark-ink variant of the same drawing */}
      <img src={`/mascot/hand/${file}-dark.svg`} alt="" aria-hidden draggable={false} className="hidden size-full select-none dark:block" />
    </>
  );
}

/**
 * Slip, the PullReceipts mascot: a small paper receipt who audits carefully and never gloats.
 * Decorative by default when placed next to text that already says what is happening; pass `label` to make it an image.
 * `delay` (seconds) holds the stamp poses' press until the receipt stamp lands.
 */
export function Slip({ pose = "idle", size = 96, className, label, decorative, delay = 0.2 }: { pose?: SlipPose; size?: number | string; className?: string; label?: string; decorative?: boolean; delay?: number }) {
  const reduce = useReducedMotion() ?? false;
  const stamp = STAMP_POSES.includes(pose);
  const bob = BOB_POSES.includes(pose);
  const alt = decorative ? "" : label ?? LABEL[pose];
  return (
    <motion.span
      className={cn("inline-block shrink-0", className)}
      style={{ width: size, height: size }}
      role={decorative ? undefined : "img"}
      aria-label={decorative ? undefined : alt}
      aria-hidden={decorative || undefined}
      data-testid="slip"
      data-pose={pose}
      initial={reduce ? false : stamp ? { opacity: 1, y: -10, scaleY: 1.08, scaleX: 0.96 } : { opacity: 1, scaleY: 0.82, scaleX: 1.12 }}
      animate={{ opacity: 1, y: 0, scaleY: 1, scaleX: 1 }}
      transition={{ type: "spring", stiffness: stamp ? 560 : 380, damping: stamp ? 22 : 15, mass: 0.7, delay: stamp ? delay : 0 }}
    >
      <motion.span className="block size-full" animate={bob && !reduce ? { y: [0, -2, 0] } : undefined} transition={{ duration: 3.2, repeat: Infinity, ease: "easeInOut" }}>
        <Art file={`slip-${pose}`} alt={alt} />
      </motion.span>
    </motion.span>
  );
}

/** Slip's head alone: the logo mark for the header. */
export function SlipMark({ className, size = 24 }: { className?: string; size?: number | string }) {
  return (
    <span className={cn("inline-block shrink-0", className)} style={{ width: size, height: size }} aria-hidden>
      <Art file="slip-mark" alt="" />
    </span>
  );
}
