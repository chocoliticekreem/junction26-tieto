"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import type { Decision, Interpretation } from "@/lib/types";

export function MergeButtons({
  changeId,
  human,
  decision,
  interpretations,
  blocked = {},
}: {
  changeId: string;
  human: string | null;
  decision?: Decision;
  interpretations: Interpretation[];
  /** label -> verdict reason, for branches whose own run is blocked; those cannot be merged. */
  blocked?: Record<string, string>;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState<"A" | "B" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<Decision | null>(null);

  const done = decision ?? result;
  if (done) {
    return (
      <div className="rounded-md border border-ok-foreground/30 bg-ok px-3 py-2 text-sm text-ok-foreground" data-testid="merge-result">
        <span className="font-medium">
          {done.choice === "sign-off" ? "Signed off" : `Merged ${done.choice} into main`} by {done.who}
        </span>{" "}
        <span className="font-mono text-xs">
          {done.merge_sha ? (
            <>
              · <span data-testid="merge-sha">{done.merge_sha}</span>
            </>
          ) : (
            "· no merge commit"
          )}{" "}
          · {done.ts}
        </span>
      </div>
    );
  }

  async function merge(choice: "A" | "B") {
    setBusy(choice);
    setError(null);
    const res = await fetch("/api/decisions/merge", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ change_id: changeId, choice }),
    });
    const body = await res.json().catch(() => ({}));
    setBusy(null);
    if (!res.ok) {
      setError(body?.error ?? `HTTP ${res.status}`);
      return;
    }
    setResult(body.decision as Decision);
    router.refresh();
  }

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2" data-testid="merge-panel">
      <div className="flex flex-wrap gap-2">
        {interpretations.map((i) => (
          <Button
            key={i.label}
            onClick={() => void merge(i.label)}
            disabled={!human || busy !== null || i.label in blocked}
            size="sm"
            data-testid={`merge-${i.label}`}
            title="git merge --no-ff into main, authored by you; writes the decision record"
          >
            {busy === i.label ? `Merging ${i.label}` : `Merge ${i.label}`}
          </Button>
        ))}
      </div>
      <span className="text-xs text-muted-foreground">{human ? `as ${human}` : "Pick your name in the header to decide"}</span>
      {Object.entries(blocked).map(([label, reason]) => (
        <p key={label} className="text-xs text-bad-foreground [overflow-wrap:anywhere]" data-testid={`merge-blocked-${label}`}>
          {label} cannot be merged: its own review is blocked ({reason}).
        </p>
      ))}
      {error && (
        <p className="text-xs text-bad-foreground" data-testid="merge-error">
          {error}
        </p>
      )}
    </div>
  );
}
