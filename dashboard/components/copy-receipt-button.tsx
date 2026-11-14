"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";

export function CopyReceiptButton({ markdown, apiHref }: { markdown: string; apiHref: string }) {
  const [state, setState] = useState<"idle" | "copied" | "failed">("idle");

  async function copy() {
    try {
      await navigator.clipboard.writeText(markdown);
      setState("copied");
    } catch {
      setState("failed");
    }
    setTimeout(() => setState("idle"), 2000);
  }

  return (
    <span className="inline-flex items-center gap-2">
      <Button variant="outline" size="sm" onClick={() => void copy()} data-testid="copy-pr-comment">
        {state === "copied" ? "Copied" : state === "failed" ? "Copy failed" : "Copy as PR comment"}
      </Button>
      <a href={apiHref} className="font-mono text-[11px] text-muted-foreground underline-offset-4 hover:underline" data-testid="receipt-markdown-link">
        raw .md
      </a>
    </span>
  );
}
