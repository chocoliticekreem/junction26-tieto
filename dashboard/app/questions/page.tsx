import Link from "next/link";
import { MessageCircleQuestion } from "lucide-react";
import { loadLatestRuns, pendingQuestions, readLedger } from "@/lib/data";
import { currentHuman } from "@/lib/human";
import { isReadOnly } from "@/lib/readonly";
import { DemoNote } from "@/components/demo-note";
import { lensLabel } from "@/lib/humanize";
import { QuestionForm } from "@/components/question-form";
import { EmptyState, PageTitle, Panel, Section } from "@/components/section";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

export const dynamic = "force-dynamic";

export default async function QuestionsPage() {
  const runs = loadLatestRuns();
  const pending = pendingQuestions(runs);
  const ledger = readLedger().slice().reverse();
  const human = await currentHuman();
  const readOnly = isReadOnly();

  return (
    <div className="space-y-8">
      <PageTitle title="Questions">
        <p className="mt-1 text-sm text-muted-foreground">Asked only when the answer flips a verdict. Answers go to the ledger, so the same question is never asked twice.</p>
      </PageTitle>

      <Section title="Pending" count={pending.length} icon={<MessageCircleQuestion />}>
        {pending.length === 0 ? (
          <EmptyState>No pending questions.</EmptyState>
        ) : (
          <div className="space-y-3">
            {readOnly && <DemoNote />}
            {pending.map((q) => (
              <Panel key={q.id} className="space-y-3 p-4 [overflow-wrap:anywhere]" data-testid="pending-question">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                  <Link href={`/change/${encodeURIComponent(q.change_id)}`} className="font-mono hover:text-foreground hover:underline">
                    {q.change_id}
                  </Link>
                  <span className="font-mono">{q.area}</span>
                  <span>{lensLabel[q.lens] ?? q.lens} lens</span>
                  <span className="font-mono text-[11px]">{q.id}</span>
                </div>
                <p className="text-base font-medium leading-6 tracking-tight">{q.text}</p>
                <p className="text-sm text-muted-foreground">{q.why_it_matters}</p>
                <QuestionForm q={q} human={human} readOnly={readOnly} />
              </Panel>
            ))}
          </div>
        )}
      </Section>

      <Section title="Answered" count={ledger.length}>
        {ledger.length === 0 ? (
          <EmptyState>The ledger is empty.</EmptyState>
        ) : (
          <Panel className="overflow-hidden">
            <Table>
              <TableHeader>
                <TableRow className="bg-muted/40 hover:bg-muted/40">
                  <TableHead className="h-8 text-xs text-muted-foreground">Question</TableHead>
                  <TableHead className="h-8 text-xs text-muted-foreground">Answer</TableHead>
                  <TableHead className="h-8 w-40 text-xs text-muted-foreground">Area</TableHead>
                  <TableHead className="h-8 w-44 text-xs text-muted-foreground">By</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody data-testid="ledger-table">
                {ledger.map((e, i) => (
                  <TableRow key={i}>
                    <TableCell className="py-2 text-sm whitespace-normal">
                      {e.question}
                      <div className="mt-0.5 font-mono text-[11px] text-muted-foreground">{e.change_id}</div>
                    </TableCell>
                    <TableCell className="py-2 text-sm font-medium whitespace-normal">{e.answer}</TableCell>
                    <TableCell className="py-2 font-mono text-xs whitespace-normal">{e.area}</TableCell>
                    <TableCell className="py-2 text-xs">
                      {e.answered_by}
                      <div className="font-mono text-[11px] text-muted-foreground">{e.ts}</div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Panel>
        )}
      </Section>
    </div>
  );
}
