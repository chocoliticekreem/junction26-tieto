import type { Mutant } from "@/lib/types";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { cn } from "@/lib/utils";

const statusTone: Record<Mutant["status"], string> = {
  killed: "text-ok-foreground",
  survived: "text-bad-foreground font-medium",
  "compile-error": "text-muted-foreground",
  timeout: "text-warn-foreground",
};

export function MutantTable({ mutants }: { mutants: Mutant[] }) {
  const killed = mutants.filter((m) => m.status === "killed").length;
  const survived = mutants.filter((m) => m.status === "survived").length;
  return (
    <div data-testid="mutant-table">
      <div className="mb-1 text-[11px] text-muted-foreground">
        mutants: <span className="font-mono text-foreground tabular-nums">{killed}</span> killed,{" "}
        <span className={cn("font-mono tabular-nums", survived ? "text-bad-foreground" : "text-foreground")}>{survived}</span> survived
        {mutants.length - killed - survived > 0 && <>, {mutants.length - killed - survived} other</>}
      </div>
      <Table className="text-xs">
        <TableHeader>
          <TableRow>
            <TableHead className="h-7">Location</TableHead>
            <TableHead className="h-7">Operator</TableHead>
            <TableHead className="h-7">Original → mutated</TableHead>
            <TableHead className="h-7 w-20">Status</TableHead>
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
                  <div className="mt-0.5 text-[10px] text-muted-foreground">killed by {m.killing_tests.join(", ")}</div>
                )}
              </TableCell>
              <TableCell className={cn("py-1 font-mono", statusTone[m.status])}>{m.status}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
