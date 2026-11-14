import type { CommandReceipt, Evidence, Mutant, TestRun } from "@/lib/types";
import { LogViewer } from "./log-viewer";
import { Mono, MonoBlock, ResultPill } from "./pills";
import { MutantTable } from "./mutant-table";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

interface Ctx {
  changeId: string;
  runTs: string;
  commands: CommandReceipt[];
  mutants: Mutant[];
  tests: TestRun[];
}

function isStringArray(v: unknown): v is string[] {
  return Array.isArray(v) && v.every((x) => typeof x === "string");
}

/** Kind-specific rendering of Evidence.details; falls back to compact JSON. */
function Details({ ev, ctx }: { ev: Evidence; ctx: Ctx }) {
  const d = ev.details ?? {};
  const blocks: React.ReactNode[] = [];
  const used = new Set<string>();

  const tests = d.tests;
  if (Array.isArray(tests) && tests.length) {
    used.add("tests");
    blocks.push(
      <Table key="tests" className="text-xs">
        <TableHeader>
          <TableRow>
            <TableHead className="h-7">Test</TableHead>
            <TableHead className="h-7 w-20">Base</TableHead>
            <TableHead className="h-7 w-20">Head</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {(tests as Record<string, unknown>[]).map((t, i) => (
            <TableRow key={i}>
              <TableCell className="py-1 font-mono break-all whitespace-normal">{String(t.name ?? "")}</TableCell>
              <TableCell className="py-1 font-mono">{String(t.base ?? "—")}</TableCell>
              <TableCell className="py-1 font-mono">{String(t.head ?? "—")}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>,
    );
  }

  for (const key of ["failing_tests", "killing_tests", "generated_files", "files"]) {
    const v = d[key];
    if (isStringArray(v) && v.length) {
      used.add(key);
      blocks.push(
        <div key={key}>
          <div className="mb-1 text-[11px] text-muted-foreground">{key.replace("_", " ")}</div>
          <ul className="space-y-0.5 font-mono text-xs">
            {v.map((t) => (
              <li key={t} className="break-all">
                {t}
              </li>
            ))}
          </ul>
        </div>,
      );
    }
  }

  const mutantIds = d.mutant_ids;
  if (isStringArray(mutantIds) && mutantIds.length) {
    used.add("mutant_ids");
    const rows = ctx.mutants.filter((m) => mutantIds.includes(m.id));
    if (rows.length) blocks.push(<MutantTable key="mutants" mutants={rows} />);
  }

  for (const key of ["diff", "byte_diff", "api_diff"]) {
    const v = d[key];
    if (typeof v === "string" && v.trim()) {
      used.add(key);
      blocks.push(
        <div key={key}>
          <div className="mb-1 text-[11px] text-muted-foreground">{key.replace("_", " ")}</div>
          <MonoBlock>{v}</MonoBlock>
        </div>,
      );
    }
  }

  const rest = Object.fromEntries(Object.entries(d).filter(([k]) => !used.has(k)));
  const restEntries = Object.entries(rest);
  if (restEntries.length) {
    const simple = restEntries.every(([, v]) => ["string", "number", "boolean"].includes(typeof v));
    blocks.push(
      simple ? (
        <dl key="rest" className="grid grid-cols-[max-content_1fr] gap-x-3 gap-y-0.5 text-xs">
          {restEntries.map(([k, v]) => (
            <div key={k} className="contents">
              <dt className="text-muted-foreground">{k}</dt>
              <dd className="font-mono break-all">{String(v)}</dd>
            </div>
          ))}
        </dl>
      ) : (
        <MonoBlock key="rest">{JSON.stringify(rest, null, 2)}</MonoBlock>
      ),
    );
  }
  return <>{blocks}</>;
}

export function EvidenceCard({ ev, ctx }: { ev: Evidence; ctx: Ctx }) {
  const cmds = (ev.command_ids ?? []).map((id) => ctx.commands.find((c) => c.id === id)).filter((c): c is CommandReceipt => !!c);
  return (
    <div className="space-y-2 rounded-md border bg-card p-3" data-testid="evidence-card" id={`evidence-${ev.id}`}>
      <div className="flex flex-wrap items-center gap-2">
        <ResultPill result={ev.result} />
        <span className="min-w-0 text-sm font-medium [overflow-wrap:anywhere]">{ev.title}</span>
        <span className="ml-auto flex items-center gap-2 font-mono text-[11px] text-muted-foreground">
          <span>{ev.kind}</span>
          <span>{ev.id}</span>
        </span>
      </div>
      {ev.excerpt && <MonoBlock>{ev.excerpt}</MonoBlock>}
      <Details ev={ev} ctx={ctx} />
      {cmds.length > 0 && (
        <div className="space-y-1.5 border-t pt-2">
          {cmds.map((c) => (
            <div key={c.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
              <Mono className="break-all">{c.cmd.join(" ")}</Mono>
              <span className="font-mono text-muted-foreground tabular-nums">
                exit {c.exit_code} · {c.duration_s.toFixed(1)}s
              </span>
              <LogViewer changeId={ctx.changeId} runTs={ctx.runTs} logPath={c.log_path} />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
