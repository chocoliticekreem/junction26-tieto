"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import type { Decision } from "@/lib/types";

export function SignOffButton({ changeId, human, decision }: { changeId: string; human: string | null; decision?: Decision }) {
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
        {human ? `Sign off as ${human}` : "Pick your name to sign off"}
      </Button>
      {error && <span className="text-xs text-bad-foreground">{error}</span>}
    </div>
  );
}
