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
  readOnly = false,
}: {
  readOnly?: boolean;
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
      <span className="text-sm font-medium text-ok-foreground" data-testid="signed-off">
        {decision.choice === "sign-off" ? "Signed off" : `Merged ${decision.choice}`} by {decision.who}
      </span>
    );
  }

  // The decision sentence beside this button already says why; the button keeps the reason as its tooltip and label.
  if (refusal) {
    return (
      <Button
        disabled
        size="lg"
        variant="outline"
        className="cursor-not-allowed border-dashed text-muted-foreground opacity-100 hover:bg-transparent"
        data-testid="sign-off"
        title={refusal}
        aria-label={humanizeRefusal(verdict) ?? refusal}
      >
        <Lock data-icon="inline-start" /> Sign-off locked
      </Button>
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
    <div className="flex flex-col items-start gap-1.5">
      <Button onClick={() => void signOff()} disabled={readOnly || busy || !human} size="lg" className="px-4 text-[15px]" data-testid="sign-off">
        <PenLine data-icon="inline-start" />
        {human ? `Sign off as ${human}` : "Pick your name to sign off"}
      </Button>
      {error && <span className="text-xs text-bad-foreground">{error}</span>}
    </div>
  );
}
