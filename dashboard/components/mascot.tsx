"use client";

import { motion, useReducedMotion } from "motion/react";
import { cn } from "@/lib/utils";

export type SlipPose = "idle" | "reviewing" | "stamp-blocked" | "stamp-signed" | "thinking" | "empty" | "pinning";

const LABEL: Record<SlipPose, string> = {
  idle: "Slip, the Receipts mascot",
  reviewing: "Slip reading a receipt line through a magnifying glass",
  "stamp-blocked": "Slip stamping a change as blocked",
  "stamp-signed": "Slip raising the stamp after a sign-off",
  thinking: "Slip weighing reading A against reading B",
  empty: "Slip resting, nothing to review",
  pinning: "Slip pinning down the old behaviour",
};

const INK = "currentColor";
const PAPER = "var(--paper)";
const fillBox = { transformBox: "fill-box", transformOrigin: "center" } as const;

/* Receipt body: torn wavy top, dog-eared corner, zigzag bottom. 28 wide, teeth 4 wide like .receipt-paper. */
const BODY =
  "M20 18 C23.5 13.2 27.5 14.2 31 16.6 C34.5 18.8 38 18 41 15.8 L44 14.5 L48 19 " +
  "V56 L46 60 L44 56 L42 60 L40 56 L38 60 L36 56 L34 60 L32 56 L30 60 L28 56 L26 60 L24 56 L22 60 L20 56 Z";
const DOG_EAR = "M44 14.5 L48 19 L43.2 19.6 Z";

function Body({ children }: { children?: React.ReactNode }) {
  return (
    <>
      <path d={BODY} fill={PAPER} stroke={INK} strokeWidth={2} strokeLinejoin="round" />
      <path d={DOG_EAR} fill={PAPER} stroke={INK} strokeWidth={1.5} strokeLinejoin="round" />
      {children}
    </>
  );
}

/* Printed lines under the face: the receipt is, after all, a receipt. */
function Print({ highlight }: { highlight?: boolean }) {
  return (
    <g stroke={INK} strokeWidth={1.5} strokeLinecap="round" opacity={0.32}>
      <path d="M25 45 H43" />
      <path d="M25 49 H36" stroke={highlight ? "var(--primary)" : INK} opacity={highlight ? 1 : undefined} />
    </g>
  );
}

function Eyes({ dy = 0, dx = 0, blink }: { dy?: number; dx?: number; blink?: boolean }) {
  const eyes = (
    <g fill={INK}>
      <circle cx={28 + dx} cy={30 + dy} r={2} />
      <circle cx={40 + dx} cy={30 + dy} r={2} />
    </g>
  );
  if (!blink) return eyes;
  return (
    <motion.g style={fillBox} animate={{ scaleY: [1, 1, 0.1, 1, 1] }} transition={{ duration: 4.2, times: [0, 0.9, 0.93, 0.96, 1], repeat: Infinity, ease: "easeInOut" }}>
      {eyes}
    </motion.g>
  );
}

function ClosedEyes() {
  return (
    <g fill="none" stroke={INK} strokeWidth={2} strokeLinecap="round">
      <path d="M25.5 31 Q28 28 30.5 31" />
      <path d="M37.5 31 Q40 28 42.5 31" />
    </g>
  );
}

function Brows({ kind = "neutral" }: { kind?: "neutral" | "worried" | "raised" | "focused" | "quizzical" }) {
  const d: Record<typeof kind, [string, string]> = {
    neutral: ["M25.5 25.2 Q28 24.2 30.5 25.2", "M37.5 25.2 Q40 24.2 42.5 25.2"],
    worried: ["M25.5 26.5 L30.5 24.2", "M37.5 24.2 L42.5 26.5"],
    raised: ["M25.5 23.5 Q28 22 30.5 23.5", "M37.5 23.5 Q40 22 42.5 23.5"],
    focused: ["M25.5 25.5 L30.5 26", "M37.5 26 L42.5 25.5"],
    quizzical: ["M25.5 23.6 Q28 22 30.5 23.2", "M37.5 25.6 Q40 24.8 42.5 25.6"],
  };
  return (
    <g fill="none" stroke={INK} strokeWidth={1.75} strokeLinecap="round">
      <path d={d[kind][0]} />
      <path d={d[kind][1]} />
    </g>
  );
}

function Mouth({ kind = "smile" }: { kind?: "smile" | "grin" | "flat" | "worried" | "hmm" | "o" }) {
  const common = { fill: "none", stroke: INK, strokeWidth: 1.75, strokeLinecap: "round" as const };
  switch (kind) {
    case "grin":
      return <path d="M29.5 37 Q34 42.5 38.5 37" {...common} />;
    case "flat":
      return <path d="M31.5 38.5 H36.5" {...common} />;
    case "worried":
      return <path d="M31 39.5 Q34 37.6 37 39.5" {...common} />;
    case "hmm":
      return <path d="M32 38.8 Q35 37.8 37.5 39.4" {...common} />;
    case "o":
      return <circle cx={34} cy={38.6} r={1.5} {...common} />;
    default:
      return <path d="M30.5 37.5 Q34 41 37.5 37.5" {...common} />;
  }
}

function Cheeks({ dy = 0 }: { dy?: number }) {
  return (
    <g fill="var(--warn-foreground)" opacity={0.35}>
      <circle cx={24.5} cy={35 + dy} r={1.5} />
      <circle cx={43.5} cy={35 + dy} r={1.5} />
    </g>
  );
}

function Arm({ d }: { d: string }) {
  return <path d={d} fill="none" stroke={INK} strokeWidth={2} strokeLinecap="round" />;
}

function Hand({ x, y }: { x: number; y: number }) {
  return <circle cx={x} cy={y} r={2.4} fill={PAPER} stroke={INK} strokeWidth={2} />;
}

/* Rubber stamp, origin at the bottom centre of its base. */
function StampTool({ x, y, ink = "var(--primary)", rotate = 0 }: { x: number; y: number; ink?: string; rotate?: number }) {
  return (
    <g transform={`translate(${x} ${y}) rotate(${rotate})`}>
      <rect x={-5.5} y={-3.5} width={11} height={3.5} rx={1} fill={ink} />
      <rect x={-1.75} y={-11} width={3.5} height={7.5} rx={1} fill={PAPER} stroke={INK} strokeWidth={1.75} />
      <circle cx={0} cy={-13} r={2.6} fill={PAPER} stroke={INK} strokeWidth={1.75} />
    </g>
  );
}

function Shadow({ cx = 34, rx = 17 }: { cx?: number; rx?: number }) {
  return <ellipse cx={cx} cy={62.5} rx={rx} ry={1.8} fill={INK} opacity={0.1} />;
}

function Idle({ reduce }: { reduce: boolean }) {
  return (
    <>
      <Shadow cx={38} rx={20} />
      <Arm d="M20 44 L13 50" />
      <Hand x={13} y={50} />
      <Arm d="M48 44 L56.5 52" />
      <StampTool x={57} y={60} />
      <Hand x={57} y={52.5} />
      <Body>
        <Print />
        <Brows />
        <Eyes blink={!reduce} />
        <Cheeks />
        <Mouth />
      </Body>
    </>
  );
}

function Reviewing() {
  return (
    <>
      <Shadow />
      <Arm d="M20 44 L14 48" />
      <Hand x={14} y={48} />
      <Body>
        <Print highlight />
        <Brows kind="focused" />
        <Eyes dy={1.5} />
        <Cheeks dy={1} />
        <Mouth kind="flat" />
      </Body>
      <Arm d="M48 42 L52 50" />
      <path d="M43.2 51.8 L52.5 60.5" stroke={INK} strokeWidth={2.5} strokeLinecap="round" />
      <circle cx={38.5} cy={47} r={6.5} fill="var(--info)" fillOpacity={0.45} stroke={INK} strokeWidth={2} />
      <path d="M34.5 44.5 Q36 42.5 38.5 42.3" fill="none" stroke={PAPER} strokeWidth={1.2} strokeLinecap="round" opacity={0.9} />
      <Hand x={52} y={50.5} />
    </>
  );
}

function StampBlocked({ reduce, delay }: { reduce: boolean; delay: number }) {
  return (
    <>
      <Shadow cx={40} rx={23} />
      <rect x={51} y={58} width={15} height={3.5} rx={0.75} fill={PAPER} stroke={INK} strokeWidth={1.5} />
      <Arm d="M20 44 L13 49" />
      <Hand x={13} y={49} />
      <Body>
        <Print />
        <Brows kind="worried" />
        <Eyes />
        <Mouth kind="worried" />
      </Body>
      <motion.g initial={reduce ? false : { y: -9 }} animate={{ y: 0 }} transition={{ type: "spring", stiffness: 520, damping: 24, mass: 0.6, delay }}>
        <Arm d="M48 43 L58 52" />
        <StampTool x={58.5} y={58.5} ink="var(--bad-foreground)" />
        <Hand x={58.5} y={51} />
      </motion.g>
    </>
  );
}

function StampSigned({ reduce, delay }: { reduce: boolean; delay: number }) {
  return (
    <>
      <Shadow cx={40} rx={23} />
      <g fill="none" stroke="var(--ok-foreground)" strokeWidth={1.75} strokeLinecap="round" strokeLinejoin="round">
        <rect x={51} y={55} width={15} height={6} rx={1} fill="var(--ok)" />
        <path d="M55.5 58 L57.6 60 L61.5 56" />
      </g>
      <Arm d="M20 44 L13 50" />
      <Hand x={13} y={50} />
      <Body>
        <Print />
        <Brows kind="raised" />
        <Eyes />
        <Cheeks />
        <Mouth kind="grin" />
      </Body>
      <motion.g initial={reduce ? false : { y: 8, rotate: 0 }} animate={{ y: 0, rotate: 0 }} transition={{ type: "spring", stiffness: 300, damping: 16, delay }}>
        <Arm d="M48 40 L57 29" />
        <StampTool x={58} y={36} ink="var(--ok-foreground)" rotate={-12} />
        <Hand x={57.2} y={30} />
      </motion.g>
    </>
  );
}

function Thinking() {
  return (
    <>
      <Shadow />
      <Arm d="M48 44 L55 50" />
      <Hand x={55} y={50} />
      <Body>
        <Print />
        <Brows kind="quizzical" />
        <Eyes dx={1} dy={-1} />
        <Mouth kind="hmm" />
      </Body>
      <Arm d="M20 46 C13 45 15 38 24 40.5" />
      <Hand x={26} y={41.5} />
      <g fill={PAPER} stroke={INK} strokeWidth={1.5}>
        <circle cx={48} cy={24} r={1.2} />
        <circle cx={51.5} cy={19.5} r={1.8} />
        <rect x={50} y={3} width={19} height={12} rx={3} />
      </g>
      <g fill={INK} fontFamily="var(--font-mono), ui-monospace, monospace" fontSize={7.5} fontWeight={600} textAnchor="middle">
        <text x={55.2} y={11.8}>A</text>
        <text x={64} y={11.8}>B</text>
      </g>
      <path d="M59.5 7 V12" stroke={INK} strokeWidth={1} opacity={0.4} />
    </>
  );
}

function Empty() {
  return (
    <>
      <Shadow cx={32} rx={22} />
      <g transform="translate(11 60) rotate(78)">
        <rect x={-5.5} y={-3.5} width={11} height={3.5} rx={1} fill="var(--primary)" />
        <rect x={-1.75} y={-11} width={3.5} height={7.5} rx={1} fill={PAPER} stroke={INK} strokeWidth={1.75} />
        <circle cx={0} cy={-13} r={2.6} fill={PAPER} stroke={INK} strokeWidth={1.75} />
      </g>
      <g transform="rotate(-4 34 60)">
        <Arm d="M20 44 C17 49 20 53 24 52" />
        <Hand x={24.5} y={52} />
        <Arm d="M48 44 C51 49 48 53 44 52" />
        <Hand x={43.5} y={52} />
        <Body>
          <Print />
          <ClosedEyes />
          <Cheeks />
          <Mouth />
        </Body>
      </g>
    </>
  );
}

function Pinning() {
  return (
    <>
      <Shadow cx={40} rx={23} />
      <g fill={PAPER} stroke={INK} strokeWidth={1.5}>
        <rect x={53} y={34} width={14} height={18} rx={1} />
        <path d="M56.5 42 H63.5 M56.5 46 H61" strokeLinecap="round" opacity={0.4} />
      </g>
      <Arm d="M20 44 L13 50" />
      <Hand x={13} y={50} />
      <Body>
        <Print />
        <Brows kind="focused" />
        <Eyes dx={1} />
        <Mouth kind="o" />
      </Body>
      <Arm d="M48 41 L54 35" />
      <g transform="translate(56.5 35.5) rotate(20)">
        <path d="M0 2 V7" stroke={INK} strokeWidth={1.5} strokeLinecap="round" />
        <rect x={-2} y={-1} width={4} height={3} fill={INK} />
        <circle cx={0} cy={-3} r={3} fill="var(--primary)" stroke={INK} strokeWidth={1.5} />
      </g>
      <Hand x={54} y={35.5} />
    </>
  );
}

/**
 * Slip, the Receipts mascot: a small paper receipt who audits carefully and never gloats.
 * Inline SVG in currentColor + theme variables, so it follows light and dark. Decorative by default
 * when placed next to text that already says what is happening; pass `label` to make it an image.
 * `delay` (seconds) holds the stamp poses' press until the receipt stamp lands.
 */
export function Slip({ pose = "idle", size = 96, className, label, decorative, delay = 0.2 }: { pose?: SlipPose; size?: number | string; className?: string; label?: string; decorative?: boolean; delay?: number }) {
  const reduce = useReducedMotion() ?? false;
  const scene =
    pose === "reviewing" ? <Reviewing /> :
    pose === "stamp-blocked" ? <StampBlocked reduce={reduce} delay={delay} /> :
    pose === "stamp-signed" ? <StampSigned reduce={reduce} delay={delay} /> :
    pose === "thinking" ? <Thinking /> :
    pose === "empty" ? <Empty /> :
    pose === "pinning" ? <Pinning /> :
    <Idle reduce={reduce} />;
  const bob = pose === "idle" || pose === "empty" || pose === "thinking";
  return (
    <svg
      viewBox="0 0 72 72"
      width={size}
      height={size}
      className={cn("shrink-0 text-ink", className)}
      role={decorative ? undefined : "img"}
      aria-label={decorative ? undefined : label ?? LABEL[pose]}
      aria-hidden={decorative || undefined}
      data-testid="slip"
      data-pose={pose}
    >
      <motion.g animate={bob && !reduce ? { y: [0, -1.2, 0] } : undefined} transition={{ duration: 3.2, repeat: Infinity, ease: "easeInOut" }}>
        {scene}
      </motion.g>
    </svg>
  );
}

/** Slip's head alone: the logo mark for the header and the favicon. */
export function SlipMark({ className, size = 24 }: { className?: string; size?: number | string }) {
  return (
    <svg viewBox="0 0 32 32" width={size} height={size} className={cn("shrink-0 text-ink", className)} aria-hidden>
      <path
        d="M7 6.5 C9.5 3 13 3.6 15.5 5.4 C17.8 7 20 6.6 22 5 L23.5 4.2 L25 6.6 V26 L23.5 29 L22 26 L20.5 29 L19 26 L17.5 29 L16 26 L14.5 29 L13 26 L11.5 29 L10 26 L8.5 29 L7 26 Z"
        fill={PAPER}
        stroke={INK}
        strokeWidth={1.75}
        strokeLinejoin="round"
      />
      <path d="M23.5 4.2 L25 6.6 L22 7 Z" fill={PAPER} stroke={INK} strokeWidth={1.25} strokeLinejoin="round" />
      <g fill={INK}>
        <circle cx={12} cy={13.5} r={1.7} />
        <circle cx={20} cy={13.5} r={1.7} />
      </g>
      <path d="M13 19 Q16 22.5 19 19" fill="none" stroke={INK} strokeWidth={1.6} strokeLinecap="round" />
    </svg>
  );
}
