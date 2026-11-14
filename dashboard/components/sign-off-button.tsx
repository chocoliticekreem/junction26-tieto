"use client";

import { Lock, PenLine } from "lucide-react";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { humanizeRefusal } from "@/lib/humanize";
import type { Decision, VerdictKind } from "@/lib/types";

export function SignOffButton({
  changeId,
  human,
  decision,
  refusal,
  verdict,
}: {
  changeId: string;
  human: string | null;
  decision?: Decision;
  /** The API's refusal text; kept as the tooltip. */
  refusal?: string;
  verdict: VerdictKind;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (decision) {
    return (
      <span className="text-sm text-muted-foreground" data-testid="signed-off">
        {decision.choice === "sign-off" ? "Signed off" : `Merged ${decision.choice}`} by {decision.who}
      </span>
    );
  }

  if (refusal) {
    return (
      <div className="flex min-w-0 flex-col items-start gap-1 sm:items-end" title={refusal}>
        <Button disabled size="sm" variant="outline" className="cursor-not-allowed border-dashed text-muted-foreground opacity-100 hover:bg-transparent" data-testid="sign-off" aria-describedby="sign-off-refused">
          <Lock data-icon="inline-start" /> Sign-off locked
        </Button>
        <span id="sign-off-refused" className="max-w-xs text-xs text-muted-foreground [overflow-wrap:anywhere] sm:text-right" data-testid="sign-off-refused" title={refusal}>
          {humanizeRefusal(verdict) ?? refusal}
        </span>
      </div>
    );
  }

  async function signOff() {
    setBusy(true);
    setError(null);
    const res = await fetch("/api/decisions/sign-off", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ change_id: changeId }),
    });
    setBusy(false);
    if (!res.ok) {
      setError((await res.json().catch(() => ({})))?.error ?? `HTTP ${res.status}`);
      return;
    }
    router.refresh();
  }

  return (
    <div className="flex flex-col items-end gap-1">
      <Button onClick={() => void signOff()} disabled={busy || !human} size="sm" data-testid="sign-off">
        <PenLine data-icon="inline-start" />
        {human ? `Sign off as ${human}` : "Pick your name to sign off"}
      </Button>
      {error && <span className="text-xs text-bad-foreground">{error}</span>}
    </div>
  );
}
