import Link from "next/link";
import { loadLatestRuns, pendingQuestions, readLedger } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { QuestionForm } from "@/components/question-form";
import { Mono } from "@/components/pills";
import { EmptyState, PageTitle, Section } from "@/components/section";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

export const dynamic = "force-dynamic";

export default async function QuestionsPage() {
  const runs = loadLatestRuns();
  const pending = pendingQuestions(runs);
  const ledger = readLedger().slice().reverse();
  const human = await currentHuman();

  return (
    <div className="space-y-8">
      <PageTitle title="Questions" eyebrow="intent the reviewer could not see">
        <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
          Asked only when the answer flips a verdict, at most three per change. Every answer lands in the context ledger and is never asked twice.
        </p>
      </PageTitle>

      <Section title="Pending" count={pending.length}>
        {pending.length === 0 ? (
          <EmptyState>No pending questions.</EmptyState>
        ) : (
          <div className="space-y-3">
            {pending.map((q) => (
              <div key={q.id} className="space-y-3 rounded-md border p-4" data-testid="pending-question">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                  <Link href={`/change/${encodeURIComponent(q.change_id)}`} className="font-mono hover:underline">
                    {q.change_id}
                  </Link>
                  <Mono>{q.area}</Mono>
                  <span className="font-mono">{q.lens}</span>
                  <span className="font-mono">{q.id}</span>
                </div>
                <p className="text-sm font-medium leading-6">{q.text}</p>
                <p className="text-xs text-muted-foreground">
                  <span className="font-medium text-foreground">Why it matters:</span> {q.why_it_matters}
                </p>
                <QuestionForm q={q} human={human} />
              </div>
            ))}
          </div>
        )}
      </Section>

      <Section title="Answered" count={ledger.length} hint="context/ledger.jsonl, newest first">
        {ledger.length === 0 ? (
          <EmptyState>The ledger is empty.</EmptyState>
        ) : (
          <div className="rounded-md border">
            <Table>
              <TableHeader>
                <TableRow className="hover:bg-transparent">
                  <TableHead className="h-8">Question</TableHead>
                  <TableHead className="h-8">Answer</TableHead>
                  <TableHead className="h-8 w-40">Area</TableHead>
                  <TableHead className="h-8 w-44">By</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody data-testid="ledger-table">
                {ledger.map((e, i) => (
                  <TableRow key={i}>
                    <TableCell className="py-2 text-sm whitespace-normal">
                      {e.question}
                      <div className="mt-0.5 font-mono text-[11px] text-muted-foreground">{e.change_id}</div>
                    </TableCell>
                    <TableCell className="py-2 text-sm whitespace-normal">{e.answer}</TableCell>
                    <TableCell className="py-2 font-mono text-xs">{e.area}</TableCell>
                    <TableCell className="py-2 text-xs">
                      {e.answered_by}
                      <div className="font-mono text-[11px] text-muted-foreground">{e.ts}</div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </Section>
    </div>
  );
}
