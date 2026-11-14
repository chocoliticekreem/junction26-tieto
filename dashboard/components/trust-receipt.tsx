"use client";

import { CircleCheck, CircleMinus, CircleX, Repeat2 } from "lucide-react";
import { motion, useReducedMotion } from "motion/react";
import { useEffect, useRef, useState } from "react";
import { humanizeTitle } from "@/lib/humanize";
import type { Decision, RunBundle } from "@/lib/types";
import { ResultIcon, resultTone, resultWord, verdictLabel } from "./pills";
import { SignedRing, Stamp } from "./stamp";
import { Term } from "./term";
import { cn } from "@/lib/utils";

const PRINT_STEP = 0.07;

function Line({ label, value, href, tone, note, testId, index, reduce }: { label: React.ReactNode; value: React.ReactNode; href?: string; tone?: string; note?: string; testId?: string; index: number; reduce: boolean }) {
  const inner = (
    <>
      <span className="min-w-0 shrink [overflow-wrap:anywhere]">{label}</span>
      <span className="dots" aria-hidden />
      <span className={cn("shrink-0 tabular-nums", tone)}>{value}</span>
    </>
  );
  return (
    <motion.li
      className="min-w-0"
      data-testid={testId}
      initial={reduce ? false : { opacity: 0, y: 4 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.18, delay: index * PRINT_STEP, ease: "easeOut" }}
    >
      {href ? (
        <a href={href} className="receipt-leader rounded-sm hover:text-primary">
          {inner}
        </a>
      ) : (
        <span className="receipt-leader">{inner}</span>
      )}
      {note && <div className="truncate pl-3 text-[10px] leading-4 text-muted-foreground">{note}</div>}
    </motion.li>
  );
}

function stampText(run: RunBundle, decision?: Decision): { kind: Parameters<typeof Stamp>[0]["kind"]; text: string } {
  if (decision) return { kind: "signed", text: decision.choice === "sign-off" ? `Signed off · ${decision.who}` : `Merged ${decision.choice} · ${decision.who}` };
  return { kind: run.receipt.verdict, text: verdictLabel[run.receipt.verdict] ?? run.receipt.verdict };
}

/**
 * The receipt itself: what was checked, line by line, with the verdict stamped on it. On first view the lines
 * print one after another and the stamp lands last. A decision arriving later re-lands the stamp only.
 */
export function TrustReceipt({ run, decision, pending = 0, className }: { run: RunBundle; decision?: Decision; pending?: number; className?: string }) {
  const r = run.receipt;
  const reduce = useReducedMotion() ?? false;
  const byResult = (k: string) => run.evidence.filter((e) => e.result === k).length;
  const stamp = stampText(run, decision);
  const lineCount = r.checked.length + 7;
  const firstStamp = useRef(stamp.text);
  const [celebrate, setCelebrate] = useState(false);
  useEffect(() => {
    if (decision && firstStamp.current !== stamp.text) setCelebrate(true);
  }, [decision, stamp.text]);
  const stampDelay = celebrate ? 0.05 : Math.min(1.6, lineCount * PRINT_STEP + 0.15);
  let i = 0;
  const next = () => i++;

  return (
    <aside id="trust-receipt" className={cn("receipt-paper min-w-0 scroll-mt-28 rounded-sm px-5 py-5 font-mono text-xs leading-5 sm:px-6", className)} data-testid="trust-receipt" aria-label="trust receipt">
      <div className="flex items-start justify-between gap-3 border-b border-dashed border-ink/30 pb-3">
        <div>
          <div className="text-[11px] font-semibold tracking-[0.18em] uppercase">
            Trust <Term k="receipt">receipt</Term>
          </div>
          <div className="mt-0.5 text-[10px] text-muted-foreground">{r.requirement_id}</div>
        </div>
        <div className="text-right text-[10px] leading-4 text-muted-foreground">
          <div>{run.changeId}</div>
          <div>{r.head_sha.slice(0, 10)}</div>
          <div>{r.run_ts}</div>
        </div>
      </div>

      <ul className="mt-3 space-y-1" data-testid="receipt-lines">
        {r.checked.map((c, k) => {
          const h = humanizeTitle(c.what);
          return (
            <Line
              key={k}
              index={next()}
              reduce={reduce}
              label={
                <>
                  {h.text}
                  {h.count && <span className="text-muted-foreground"> · {h.count}</span>}
                </>
              }
              value={
                <span className="inline-flex items-center gap-1 uppercase">
                  <ResultIcon result={c.result} />
                  {resultWord[c.result]}
                </span>
              }
              tone={resultTone[c.result]}
              href={c.evidence_id ? `#evidence-${c.evidence_id}` : undefined}
              note={h.note}
            />
          );
        })}
        {r.checked.length === 0 && <li className="text-muted-foreground">Nothing recorded as checked.</li>}
      </ul>

      <ul className="mt-3 space-y-1 border-t border-dashed border-ink/30 pt-3" data-testid="receipt-subtotals">
        <Line
          index={next()}
          reduce={reduce}
          label={<><Term k="receipt">Receipts</Term> ({run.evidence.length})</>}
          value={
            <span className="inline-flex items-center gap-2" title={`${byResult("pass")} pass · ${byResult("partial")} partial · ${byResult("fail") + byResult("error")} fail`}>
              <span className="inline-flex items-center gap-0.5 text-ok-foreground">
                <CircleCheck className="size-3" aria-hidden />
                {byResult("pass")}
              </span>
              {byResult("partial") > 0 && (
                <span className="inline-flex items-center gap-0.5 text-warn-foreground">
                  <CircleMinus className="size-3" aria-hidden />
                  {byResult("partial")}
                </span>
              )}
              {byResult("fail") + byResult("error") > 0 && (
                <span className="inline-flex items-center gap-0.5 text-bad-foreground">
                  <CircleX className="size-3" aria-hidden />
                  {byResult("fail") + byResult("error")}
                </span>
              )}
            </span>
          }
          href="#technical"
        />
        <Line index={next()} reduce={reduce} label={<><Term k="finding">Findings</Term> with proof</>} value={r.findings.length} tone={r.findings.length ? "text-bad-foreground font-semibold" : undefined} href="#proved" />
        <Line index={next()} reduce={reduce} label={<><Term k="hunch">Hunches</Term> withheld</>} value={r.hunches.length} href="#technical" />
        <Line index={next()} reduce={reduce} label={<Term k="notchecked">Not checked</Term>} value={r.not_checked.length} href="#not-checked" />
        {r.refused.length > 0 && <Line index={next()} reduce={reduce} label="Refused to decide" value={r.refused.length} href="#not-checked" />}
        {pending > 0 && <Line index={next()} reduce={reduce} label="Questions for you" value={pending} tone="text-warn-foreground" href="#decide" />}
        <Line index={next()} reduce={reduce} label={<Term k="risk">Risk</Term>} value={`${r.risk}/10`} tone={r.risk >= 7 ? "text-bad-foreground" : r.risk >= 4 ? "text-warn-foreground" : undefined} />
      </ul>

      <div className="mt-3 flex items-end justify-between gap-3 border-t-2 border-ink/70 pt-3">
        <div className="min-w-0">
          <div className="text-[10px] tracking-[0.18em] uppercase text-muted-foreground">Verdict</div>
          <div className="text-[10px] text-muted-foreground">{decision ? "decided by a named human" : "awaiting a named human"}</div>
          {decision && (
            <div className="mt-0.5 text-[10px] text-muted-foreground" title={decision.ts}>
              {decision.ts.slice(0, 16).replace("T", " ")}
              {decision.merge_sha ? ` · ${decision.merge_sha.slice(0, 10)}` : ""}
            </div>
          )}
        </div>
        <div className="relative -my-3 min-w-0 shrink overflow-hidden px-2 py-3">
          {celebrate && <SignedRing />}
          <Stamp key={stamp.text} kind={stamp.kind} testId="verdict-stamp" delay={stampDelay}>
            {stamp.text}
          </Stamp>
        </div>
      </div>

      <div className="mt-4 flex items-center justify-between gap-2 text-[10px] text-muted-foreground">
        <span>No receipt, no finding. A named human decides.</span>
        {r.mock_llm && (
          <span className="inline-flex shrink-0 items-center gap-1 whitespace-nowrap rounded-full border px-1.5 py-px" title="Lens output replayed from recorded fixtures (MOCK_LLM=1); sandbox receipts are real." data-testid="replayed-badge">
            <Repeat2 className="size-3" /> replayed
          </span>
        )}
      </div>
    </aside>
  );
}
