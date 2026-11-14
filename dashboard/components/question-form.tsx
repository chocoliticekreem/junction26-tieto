"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import type { Question } from "@/lib/types";

export function QuestionForm({ q, human }: { q: Question; human: string | null }) {
  const router = useRouter();
  const [free, setFree] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function answer(text: string) {
    setBusy(true);
    setError(null);
    const res = await fetch("/api/questions/answer", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ question_id: q.id, change_id: q.change_id, area: q.area, question: q.text, answer: text }),
    });
    setBusy(false);
    if (!res.ok) {
      setError((await res.json().catch(() => ({})))?.error ?? `HTTP ${res.status}`);
      return;
    }
    router.refresh();
  }

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2">
        {q.options.map((o) => (
          <Button key={o} variant="outline" size="sm" disabled={busy || !human} onClick={() => void answer(o)} data-testid="answer-option">
            {o}
          </Button>
        ))}
      </div>
      <form
        className="flex flex-col gap-2 sm:flex-row sm:items-start"
        onSubmit={(e) => {
          e.preventDefault();
          if (free.trim()) void answer(free.trim());
        }}
      >
        <Textarea
          value={free}
          onChange={(e) => setFree(e.target.value)}
          placeholder="Or answer in your own words"
          className="min-h-9 text-sm"
          rows={1}
          data-testid="answer-free"
        />
        <Button type="submit" size="sm" variant="secondary" disabled={busy || !human || !free.trim()} data-testid="answer-submit">
          Answer{human ? ` as ${human}` : ""}
        </Button>
      </form>
      {!human && <p className="text-xs text-muted-foreground">Pick your name in the header to answer.</p>}
      {error && <p className="text-xs text-bad-foreground">{error}</p>}
    </div>
  );
}
