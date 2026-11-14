import { ChevronRight } from "lucide-react";
import type { MetricsTable } from "@/lib/types";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

export function fmtCell(v: string | number | null | undefined, column: string): string {
  if (v === null || v === undefined) return "—";
  if (typeof v === "number") {
    if (/recall|precision|rate|share/i.test(column) && v >= 0 && v <= 1) return `${(v * 100).toFixed(0)}%`;
    if (/usd|cost/i.test(column)) return `$${v.toFixed(v < 1 ? 4 : 2)}`;
    return Number.isInteger(v) ? String(v) : v.toFixed(2);
  }
  return v;
}

/** First sentence of a note; the rest goes behind the "Definitions" disclosure. */
export function splitNote(note: string): { lead: string; rest: string } {
  const m = note.match(/^([^]*?[.!?])(\s+|$)/);
  if (!m) return { lead: note, rest: "" };
  return { lead: m[1], rest: note.slice(m[0].length).trim() };
}

export function Disclosure({ label, children, testId }: { label: React.ReactNode; children: React.ReactNode; testId?: string }) {
  return (
    <Collapsible>
      <CollapsibleTrigger className="group inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground" data-testid={testId}>
        <ChevronRight className="size-3 transition-transform group-aria-expanded:rotate-90" />
        {label}
      </CollapsibleTrigger>
      <CollapsibleContent>
        <div className="mt-2">{children}</div>
      </CollapsibleContent>
    </Collapsible>
  );
}

export function NoteLine({ note }: { note?: string }) {
  if (!note) return null;
  const { lead, rest } = splitNote(note);
  return (
    <div className="space-y-1 text-xs text-muted-foreground">
      <p>{lead}</p>
      {rest && (
        <Disclosure label="Definitions">
          <p className="max-w-3xl leading-5 [overflow-wrap:anywhere]">{rest}</p>
        </Disclosure>
      )}
    </div>
  );
}

const LONG = 48;

function LongCell({ text }: { text: string }) {
  const parts = text.split(text.includes(";") ? /;\s*/ : /,\s*/).filter(Boolean);
  return (
    <Disclosure label={`${text.slice(0, 22).trimEnd()}… (${parts.length > 1 ? `${parts.length} items` : "show"})`}>
      <ul className="space-y-0.5 font-mono text-[11px] [overflow-wrap:anywhere]">
        {parts.map((p, i) => (
          <li key={i}>{p}</li>
        ))}
      </ul>
    </Disclosure>
  );
}

export function MetricsTableView({ t, testId, rows, wrap = false }: { t: MetricsTable; testId: string; rows?: MetricsTable["rows"]; wrap?: boolean }) {
  const data = rows ?? t.rows;
  const numeric = t.columns.map((_, ci) => data.every((r) => r[ci] === null || typeof r[ci] === "number") && data.some((r) => typeof r[ci] === "number"));
  return (
    <div className="space-y-2">
      <div className="overflow-x-auto rounded-md border">
        <Table data-testid={testId}>
          <TableHeader>
            <TableRow className="hover:bg-transparent">
              {t.columns.map((c, i) => (
                <TableHead key={c} className={`h-auto py-1.5 align-bottom leading-tight whitespace-normal [overflow-wrap:normal] ${numeric[i] ? "text-right" : ""}`}>
                  {c.replace(/_/g, " ")}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.map((row, ri) => (
              <TableRow key={ri}>
                {row.map((v, ci) => {
                  const long = typeof v === "string" && v.length > LONG;
                  return (
                    <TableCell
                      key={ci}
                      className={`py-1.5 align-top ${wrap && !numeric[ci] ? "min-w-56 max-w-md text-xs whitespace-normal [overflow-wrap:anywhere]" : "font-mono text-xs tabular-nums"} ${numeric[ci] ? "text-right" : ""}`}
                    >
                      {long && !wrap ? <LongCell text={v} /> : fmtCell(v, t.columns[ci] ?? "")}
                    </TableCell>
                  );
                })}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      <NoteLine note={t.note} />
    </div>
  );
}

export function StatTile({ label, value, unit, definition, testId }: { label: string; value: string; unit?: string; definition: string; testId?: string }) {
  return (
    <div className="min-w-0 rounded-md border bg-card px-3 py-2.5" data-testid={testId ?? "stat-tile"}>
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-0.5 flex flex-wrap items-baseline gap-x-1.5">
        <span className="text-2xl font-semibold tracking-tight whitespace-nowrap tabular-nums" data-testid="stat-value">
          {value}
        </span>
        {unit && <span className="text-xs text-muted-foreground">{unit}</span>}
      </div>
      <div className="mt-1 text-[11px] leading-4 text-muted-foreground">{definition}</div>
    </div>
  );
}
