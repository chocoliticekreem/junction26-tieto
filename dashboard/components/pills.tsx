import { AlertTriangle, CircleCheck, CircleDashed, CircleMinus, CircleX, GitFork, Lock, UserCheck, Zap } from "lucide-react";
import { cn } from "@/lib/utils";
import type { Depth, ResultKind, Severity, VerdictKind } from "@/lib/types";

const pill = "inline-flex h-5 items-center gap-1 whitespace-nowrap rounded-full border px-1.5 text-[11px] font-medium leading-none";

const verdictStyle: Record<VerdictKind, string> = {
  "auto-ready": "border-ok-foreground/25 bg-ok text-ok-foreground",
  "needs-sign-off": "border-warn-foreground/25 bg-warn text-warn-foreground",
  "decision-A-B": "border-info-foreground/25 bg-info text-info-foreground",
  blocked: "border-bad-foreground/25 bg-bad text-bad-foreground",
};

export const verdictLabel: Record<VerdictKind, string> = {
  "auto-ready": "Auto-ready",
  "needs-sign-off": "Needs sign-off",
  "decision-A-B": "Decision A/B",
  blocked: "Blocked",
};

export const verdictIcon: Record<VerdictKind, React.ComponentType<{ className?: string }>> = {
  "auto-ready": Zap,
  "needs-sign-off": UserCheck,
  "decision-A-B": GitFork,
  blocked: Lock,
};

export function VerdictPill({ verdict, className }: { verdict: VerdictKind; className?: string }) {
  const style = verdictStyle[verdict] ?? "border-border bg-muted text-muted-foreground";
  const Icon = verdictIcon[verdict];
  return (
    <span className={cn(pill, style, className)} data-testid="verdict-pill">
      {Icon && <Icon className="size-3" aria-hidden />}
      {verdictLabel[verdict] ?? verdict}
    </span>
  );
}

const resultStyle: Record<ResultKind, string> = {
  pass: "border-ok-foreground/25 bg-ok text-ok-foreground",
  fail: "border-bad-foreground/25 bg-bad text-bad-foreground",
  error: "border-bad-foreground/25 bg-bad text-bad-foreground",
  partial: "border-warn-foreground/25 bg-warn text-warn-foreground",
  skipped: "border-border bg-muted text-muted-foreground",
};

export const resultIcon: Record<ResultKind, React.ComponentType<{ className?: string }>> = {
  pass: CircleCheck,
  fail: CircleX,
  error: AlertTriangle,
  partial: CircleMinus,
  skipped: CircleDashed,
};

export const resultTone: Record<ResultKind, string> = {
  pass: "text-ok-foreground",
  fail: "text-bad-foreground",
  error: "text-bad-foreground",
  partial: "text-warn-foreground",
  skipped: "text-muted-foreground",
};

export const resultWord: Record<ResultKind, string> = {
  pass: "pass",
  fail: "fail",
  error: "error",
  partial: "partial",
  skipped: "skipped",
};

export function ResultPill({ result, className, children }: { result: ResultKind; className?: string; children?: React.ReactNode }) {
  const Icon = resultIcon[result] ?? CircleDashed;
  return (
    <span className={cn(pill, resultStyle[result] ?? resultStyle.skipped, className)}>
      <Icon className="size-3" aria-hidden />
      {children ?? result}
    </span>
  );
}

export function ResultIcon({ result, className }: { result: ResultKind; className?: string }) {
  const Icon = resultIcon[result] ?? CircleDashed;
  return <Icon className={cn("size-3.5 shrink-0", resultTone[result] ?? resultTone.skipped, className)} aria-label={result} />;
}

const severityStyle: Record<Severity, string> = {
  low: "border-border bg-muted text-muted-foreground",
  medium: "border-warn-foreground/25 bg-warn text-warn-foreground",
  high: "border-bad-foreground/25 bg-bad text-bad-foreground",
  critical: "border-bad-foreground bg-bad-foreground text-background",
};

export function SeverityPill({ severity }: { severity: Severity }) {
  return <span className={cn(pill, severityStyle[severity] ?? severityStyle.low)}>{severity}</span>;
}

export const depthStyle: Record<Depth, { pill: string; dot: string; label: string }> = {
  deep: { pill: "border-bad-foreground/25 bg-bad text-bad-foreground", dot: "bg-bad-foreground", label: "deep" },
  light: { pill: "border-warn-foreground/25 bg-warn text-warn-foreground", dot: "bg-warn-foreground", label: "light" },
  regeneration: { pill: "border-border bg-muted text-muted-foreground", dot: "bg-muted-foreground/60", label: "regeneration" },
};

export function DepthPill({ depth }: { depth: Depth }) {
  const s = depthStyle[depth] ?? depthStyle.light;
  return <span className={cn(pill, s.pill)}>{s.label}</span>;
}

/** Risk 1-10 as a compact 10-segment meter; the number stays for screen readers and the tooltip. */
export function RiskMeter({ risk, className }: { risk: number; className?: string }) {
  const tone = risk >= 7 ? "bg-foreground/75" : "bg-foreground/45";
  const text = risk >= 7 ? "text-bad-foreground" : risk >= 4 ? "text-warn-foreground" : "text-muted-foreground";
  return (
    <span className={cn("inline-flex items-center gap-1.5", className)} title={`max routed risk ${risk} of 10 over the change's hunks`} aria-label={`risk ${risk} of 10`}>
      <span className="flex gap-px" aria-hidden>
        {Array.from({ length: 10 }, (_, i) => (
          <span key={i} className={cn("h-2.5 w-1 rounded-[1px]", i < risk ? tone : "bg-border")} />
        ))}
      </span>
      <span className={cn("font-mono text-xs tabular-nums", text)}>{risk}</span>
    </span>
  );
}

export function RiskPill({ risk }: { risk: number }) {
  return <RiskMeter risk={risk} />;
}

export function TrustLevel({ level }: { level: number | undefined }) {
  if (level === undefined) return <span className="text-xs text-muted-foreground">—</span>;
  return (
    <span className="inline-flex items-center gap-1" title={`area trust level ${level} of 3`}>
      <span className="flex gap-0.5" aria-hidden>
        {[0, 1, 2].map((i) => (
          <span key={i} className={cn("h-2.5 w-1.5 rounded-[1px]", i < level ? "bg-primary" : "bg-border")} />
        ))}
      </span>
      <span className="font-mono text-xs tabular-nums text-muted-foreground">L{level}</span>
    </span>
  );
}

/** pass / partial / fail segments of a change's receipts as one small bar. */
export function ReceiptBar({ pass, partial, fail, className }: { pass: number; partial: number; fail: number; className?: string }) {
  const total = pass + partial + fail;
  if (total === 0) return <span className="font-mono text-xs text-muted-foreground">—</span>;
  const seg = (n: number, tone: string, label: string) =>
    n > 0 ? <span key={label} className={cn("h-2 min-w-1 rounded-[2px]", tone)} style={{ flexGrow: n }} title={`${n} ${label}`} /> : null;
  return (
    <span className={cn("inline-flex items-center gap-2", className)} title={`${pass} pass · ${partial} partial · ${fail} fail`} data-testid="receipt-counts">
      <span className="flex w-14 gap-px" aria-hidden>
        {seg(pass, "bg-ok-foreground", "pass")}
        {seg(partial, "bg-warn-foreground", "partial")}
        {seg(fail, "bg-bad-foreground", "fail")}
      </span>
      <span className="font-mono text-xs tabular-nums">
        <span className={pass ? "text-ok-foreground" : "text-muted-foreground"}>{pass}</span>
        <span className="text-muted-foreground">/</span>
        <span className={fail ? "text-bad-foreground" : "text-muted-foreground"}>{fail}</span>
      </span>
      <span className="sr-only">
        {pass} green, {fail} red
      </span>
    </span>
  );
}

export function Mono({ children, className }: { children: React.ReactNode; className?: string }) {
  return <code className={cn("rounded bg-muted px-1 py-0.5 font-mono text-[0.85em] [overflow-wrap:anywhere]", className)}>{children}</code>;
}

export function MonoBlock({ children, className, ...props }: React.ComponentProps<"pre">) {
  return (
    <pre className={cn("max-h-80 overflow-auto rounded-md border bg-muted/60 p-3 font-mono text-xs leading-5 whitespace-pre-wrap break-words [overflow-wrap:anywhere]", className)} {...props}>
      {children}
    </pre>
  );
}
