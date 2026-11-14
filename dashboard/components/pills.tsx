import { cn } from "@/lib/utils";
import type { Depth, ResultKind, Severity, VerdictKind } from "@/lib/types";

const pill = "inline-flex h-5 items-center whitespace-nowrap rounded-sm border px-1.5 text-[11px] font-medium leading-none";

const verdictStyle: Record<VerdictKind, string> = {
  "auto-ready": "border-ok-foreground/30 bg-ok text-ok-foreground",
  "needs-sign-off": "border-warn-foreground/30 bg-warn text-warn-foreground",
  "decision-A-B": "border-info-foreground/30 bg-info text-info-foreground",
  blocked: "border-bad-foreground/30 bg-bad text-bad-foreground",
};

export const verdictLabel: Record<VerdictKind, string> = {
  "auto-ready": "Auto-ready",
  "needs-sign-off": "Needs sign-off",
  "decision-A-B": "Decision A/B",
  blocked: "Blocked",
};

export function VerdictPill({ verdict, className }: { verdict: VerdictKind; className?: string }) {
  const style = verdictStyle[verdict] ?? "border-border bg-muted text-muted-foreground";
  return (
    <span className={cn(pill, style, className)} data-testid="verdict-pill">
      {verdictLabel[verdict] ?? verdict}
    </span>
  );
}

const resultStyle: Record<ResultKind, string> = {
  pass: "border-ok-foreground/30 bg-ok text-ok-foreground",
  fail: "border-bad-foreground/30 bg-bad text-bad-foreground",
  error: "border-bad-foreground/30 bg-bad text-bad-foreground",
  partial: "border-warn-foreground/30 bg-warn text-warn-foreground",
  skipped: "border-border bg-muted text-muted-foreground",
};

export function ResultPill({ result, className, children }: { result: ResultKind; className?: string; children?: React.ReactNode }) {
  return <span className={cn(pill, resultStyle[result] ?? resultStyle.skipped, className)}>{children ?? result}</span>;
}

const severityStyle: Record<Severity, string> = {
  low: "border-border bg-muted text-muted-foreground",
  medium: "border-warn-foreground/30 bg-warn text-warn-foreground",
  high: "border-bad-foreground/30 bg-bad text-bad-foreground",
  critical: "border-bad-foreground bg-bad-foreground text-background",
};

export function SeverityPill({ severity }: { severity: Severity }) {
  return <span className={cn(pill, severityStyle[severity] ?? severityStyle.low)}>{severity}</span>;
}

export const depthStyle: Record<Depth, { pill: string; dot: string; label: string }> = {
  deep: { pill: "border-bad-foreground/30 bg-bad text-bad-foreground", dot: "bg-bad-foreground", label: "deep" },
  light: { pill: "border-warn-foreground/30 bg-warn text-warn-foreground", dot: "bg-warn-foreground", label: "light" },
  regeneration: { pill: "border-border bg-muted text-muted-foreground", dot: "bg-muted-foreground/60", label: "regeneration" },
};

export function DepthPill({ depth }: { depth: Depth }) {
  const s = depthStyle[depth] ?? depthStyle.light;
  return <span className={cn(pill, s.pill)}>{s.label}</span>;
}

export function RiskPill({ risk }: { risk: number }) {
  const tone = risk >= 7 ? "text-bad-foreground" : risk >= 4 ? "text-warn-foreground" : "text-muted-foreground";
  return (
    <span className={cn("font-mono text-xs tabular-nums", tone)} title="max routed risk over hunks (1-10)">
      {risk}/10
    </span>
  );
}

export function TrustLevel({ level }: { level: number | undefined }) {
  if (level === undefined) return <span className="text-xs text-muted-foreground">—</span>;
  return (
    <span className="inline-flex items-center gap-1" title={`area trust level ${level} of 3`}>
      <span className="flex gap-0.5">
        {[0, 1, 2].map((i) => (
          <span key={i} className={cn("h-2.5 w-1.5 rounded-[1px]", i < level ? "bg-primary" : "bg-border")} />
        ))}
      </span>
      <span className="font-mono text-xs tabular-nums text-muted-foreground">L{level}</span>
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
