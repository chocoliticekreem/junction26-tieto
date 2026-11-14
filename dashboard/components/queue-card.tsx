"use client";

import Link from "next/link";
import { ArrowRight, MessageCircleQuestion } from "lucide-react";
import { StaggerItem } from "./motion";
import { ReceiptBar, VerdictPill } from "./pills";
import { Button } from "@/components/ui/button";
import type { VerdictKind } from "@/lib/types";
import { cn } from "@/lib/utils";

export interface QueueCardData {
  changeId: string;
  requirementId: string;
  title: string;
  verdict: VerdictKind;
  status: string;
  action: { label: string; href: string; primary: boolean };
  secondary?: { label: string; href: string };
  pending: number;
  receipts: { pass: number; partial: number; fail: number };
  decided?: boolean;
}

export function QueueCard({ c }: { c: QueueCardData }) {
  const changeHref = `/change/${encodeURIComponent(c.changeId)}`;
  return (
    <StaggerItem
      as="li"
      className={cn("list-none rounded-xl border bg-card p-4 shadow-[0_1px_0_oklch(0_0_0/3%)] transition-colors duration-150 hover:border-foreground/25 sm:p-5", c.decided && "opacity-85")}
      data-testid="queue-card"
      data-change={c.changeId}
    >
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
        <VerdictPill verdict={c.verdict} />
        <span className="font-mono">{c.requirementId}</span>
        <span className="font-mono">{c.changeId}</span>
        {c.pending > 0 && (
          <Link href={`${changeHref}#decide`} className="inline-flex items-center gap-1 rounded-full border border-warn-foreground/30 bg-warn px-2 py-px font-medium text-warn-foreground hover:underline" data-testid="queue-question">
            <MessageCircleQuestion className="size-3" aria-hidden />
            {c.pending} {c.pending === 1 ? "question" : "questions"} for you
          </Link>
        )}
      </div>
      <h3 className="mt-2 text-lg font-semibold leading-snug tracking-tight sm:text-xl">
        <Link href={changeHref} className="rounded-sm hover:text-primary">
          {c.title}
        </Link>
      </h3>
      <p className="mt-1.5 max-w-3xl text-[15px] leading-6 text-muted-foreground" data-testid="queue-status">
        {c.status}
      </p>
      <div className="mt-4 flex flex-wrap items-center gap-x-4 gap-y-2">
        <Button render={<Link href={c.action.href} />} nativeButton={false} variant={c.action.primary ? "default" : "outline"} size="lg" data-testid="queue-action">
          {c.action.label} <ArrowRight data-icon="inline-end" />
        </Button>
        {c.secondary && (
          <Link href={c.secondary.href} className="text-sm text-primary underline-offset-4 hover:underline" data-testid="queue-secondary">
            {c.secondary.label}
          </Link>
        )}
        <ReceiptBar pass={c.receipts.pass} partial={c.receipts.partial} fail={c.receipts.fail} className="ml-auto" />
      </div>
    </StaggerItem>
  );
}
