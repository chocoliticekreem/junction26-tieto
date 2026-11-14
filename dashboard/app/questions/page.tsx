import Link from "next/link";
import { loadLatestRuns, pendingQuestions, readLedger } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { QuestionForm } from "@/components/question-form";
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
      <PageTitle title="Questions" eyebrow="asked only when the answer flips a verdict" />

      <Section title="Pending" count={pending.length}>
        {pending.length === 0 ? (
          <EmptyState>No pending questions.</EmptyState>
        ) : (
          <div className="space-y-3">
            {pending.map((q) => (
              <div key={q.id} className="space-y-2 rounded-md border p-3 [overflow-wrap:anywhere]" data-testid="pending-question">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 font-mono text-xs text-muted-foreground">
                  <Link href={`/change/${encodeURIComponent(q.change_id)}`} className="hover:underline">
                    {q.change_id}
                  </Link>
                  <span>{q.area}</span>
                  <span>{q.lens}</span>
                  <span>{q.id}</span>
                </div>
                <p className="text-sm font-medium leading-6">{q.text}</p>
                <p className="text-xs text-muted-foreground">{q.why_it_matters}</p>
                <QuestionForm q={q} human={human} />
              </div>
            ))}
          </div>
        )}
      </Section>

      <Section title="Answered" count={ledger.length}>
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
