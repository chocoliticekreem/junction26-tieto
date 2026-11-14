import type { Mutant } from "@/lib/types";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { cn } from "@/lib/utils";

const statusTone: Record<Mutant["status"], string> = {
  killed: "text-ok-foreground",
  "killed-by-suite": "text-info-foreground",
  survived: "text-bad-foreground font-medium",
  "compile-error": "text-muted-foreground",
  timeout: "text-warn-foreground",
};

const statusLabel: Record<Mutant["status"], string> = {
  killed: "killed",
  "killed-by-suite": "caught by existing suite",
  survived: "survived",
  "compile-error": "compile-error",
  timeout: "timeout",
};

const statusTitle: Partial<Record<Mutant["status"], string>> = {
  killed: "killed by the change's own tests",
  "killed-by-suite": "caught by the existing suite, not by the change's tests",
  survived: "no test in the full suite detects it",
};

export function MutantTable({ mutants }: { mutants: Mutant[] }) {
  const killed = mutants.filter((m) => m.status === "killed").length;
  const bySuite = mutants.filter((m) => m.status === "killed-by-suite").length;
  const survived = mutants.filter((m) => m.status === "survived").length;
  const other = mutants.length - killed - bySuite - survived;
  return (
    <div data-testid="mutant-table">
      <div className="mb-1 text-[11px] text-muted-foreground">
        mutants: <span className="font-mono text-foreground tabular-nums">{killed}</span> killed by the change&apos;s tests,{" "}
        {bySuite > 0 && (
          <>
            <span className="font-mono text-info-foreground tabular-nums">{bySuite}</span> caught only by the existing suite,{" "}
          </>
        )}
        <span className={cn("font-mono tabular-nums", survived ? "text-bad-foreground" : "text-foreground")}>{survived}</span> survived the full suite
        {other > 0 && <>, {other} other</>}
      </div>
      <Table className="text-xs">
        <TableHeader>
          <TableRow>
            <TableHead className="h-7">Location</TableHead>
            <TableHead className="h-7">Operator</TableHead>
            <TableHead className="h-7">Original → mutated</TableHead>
            <TableHead className="h-7 w-28">Status</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {mutants.map((m) => (
            <TableRow key={m.id}>
              <TableCell className="min-w-28 py-1 font-mono whitespace-normal [overflow-wrap:anywhere]">
                {m.file}:{m.line}
              </TableCell>
              <TableCell className="py-1 font-mono whitespace-normal">{m.operator}</TableCell>
              <TableCell className="min-w-40 py-1 font-mono whitespace-normal [overflow-wrap:anywhere]">
                <div className="text-muted-foreground line-through decoration-muted-foreground/50">{m.original}</div>
                <div>{m.mutated}</div>
                {m.killing_tests && m.killing_tests.length > 0 && (
                  <div className="mt-0.5 text-[10px] text-muted-foreground">
                    {m.status === "killed-by-suite" ? "caught by" : "killed by"} {m.killing_tests.join(", ")}
                  </div>
                )}
              </TableCell>
              <TableCell
                className={cn("py-1 font-mono whitespace-normal", statusTone[m.status])}
                title={statusTitle[m.status]}
              >
                {statusLabel[m.status]}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
