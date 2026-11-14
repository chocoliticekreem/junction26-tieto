"use client";

import { GitMerge, Lock } from "lucide-react";
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
  readOnly = false,
}: {
  readOnly?: boolean;
  changeId: string;
  human: string | null;
  decision?: Decision;
  interpretations: Interpretation[];
  /** label -> verdict reason, for branches whose own run is blocked; those cannot be merged. The side card says why. */
  blocked?: Record<string, string>;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState<"A" | "B" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<Decision | null>(null);

  const done = decision ?? result;
  if (done) {
    return (
      <div className="rounded-lg border border-ok-foreground/25 bg-ok px-3 py-2 text-sm text-ok-foreground" data-testid="merge-result">
        <span className="font-medium">
          {done.choice === "sign-off" ? "Signed off" : `Merged ${done.choice} into main`} by {done.who}
        </span>{" "}
        <span className="font-mono text-xs">
          {done.merge_sha ? (
            <>
              · <span data-testid="merge-sha">{done.merge_sha}</span>
            </>
          ) : done.simulated ? (
            <>
              · <span data-testid="merge-simulated">Merge recorded (simulated on the public demo: no git repository here)</span>
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
    <div className="space-y-2" data-testid="merge-panel">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <div className="flex flex-wrap gap-2">
          {interpretations.map((i) => {
            const locked = i.label in blocked;
            const apiReason = locked ? `${i.label} cannot be merged: its own review is blocked (${blocked[i.label]}).` : "git merge --no-ff into main, authored by you; writes the decision record";
            return (
              <Button
                key={i.label}
                onClick={() => void merge(i.label)}
                disabled={readOnly || !human || busy !== null || locked}
                size="lg"
                variant={locked ? "outline" : "default"}
                className={locked ? "cursor-not-allowed border-dashed px-4 text-[15px] text-muted-foreground opacity-100" : "px-4 text-[15px]"}
                data-testid={`merge-${i.label}`}
                data-locked={locked ? "true" : undefined}
                title={apiReason}
              >
                {locked ? <Lock data-icon="inline-start" /> : <GitMerge data-icon="inline-start" />}
                {busy === i.label ? `Merging ${i.label}` : `Merge ${i.label}`}
              </Button>
            );
          })}
        </div>
        <span className="text-sm text-muted-foreground">{human ? `as ${human}` : "Pick your name in the header to decide"}</span>
      </div>
      {error && (
        <p className="text-xs text-bad-foreground" data-testid="merge-error">
          {error}
        </p>
      )}
    </div>
  );
}
