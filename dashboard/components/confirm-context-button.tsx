"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import type { ContextStatus } from "@/lib/types";

export function ConfirmContextButton({ human, status }: { human: string | null; status: ContextStatus }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (status.status === "confirmed") return null;

  async function confirm() {
    setBusy(true);
    setError(null);
    const res = await fetch("/api/context/confirm", { method: "POST" });
    setBusy(false);
    if (!res.ok) {
      setError((await res.json().catch(() => ({})))?.error ?? `HTTP ${res.status}`);
      return;
    }
    router.refresh();
  }

  return (
    <div className="flex flex-col items-end gap-1">
      <Button onClick={() => void confirm()} disabled={busy || !human} size="sm" data-testid="confirm-context">
        {human ? `Confirm as ${human}` : "Pick your name to confirm"}
      </Button>
      {error && <span className="text-xs text-bad-foreground">{error}</span>}
    </div>
  );
}
