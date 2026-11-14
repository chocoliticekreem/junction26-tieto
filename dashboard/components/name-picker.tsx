"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export function NamePicker({ initial }: { initial: string | null }) {
  const router = useRouter();
  const [editing, setEditing] = useState(!initial);
  const [name, setName] = useState(initial ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setBusy(true);
    setError(null);
    const res = await fetch("/api/human", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ name }),
    });
    setBusy(false);
    if (!res.ok) {
      setError((await res.json().catch(() => ({})))?.error ?? "Could not save name");
      return;
    }
    setEditing(false);
    router.refresh();
  }

  if (!editing && initial) {
    return (
      <div className="flex items-center gap-2 text-sm" data-testid="human-display">
        <span className="text-muted-foreground">Signed in as</span>
        <span className="font-medium">{initial}</span>
        <Button variant="ghost" size="xs" onClick={() => setEditing(true)}>
          Change
        </Button>
      </div>
    );
  }

  return (
    <form
      className="flex items-center gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        void save();
      }}
    >
      <Input
        aria-label="Your name"
        placeholder="Your name (required to sign)"
        value={name}
        onChange={(e) => setName(e.target.value)}
        className="h-8 w-48 text-sm"
        data-testid="human-input"
      />
      <Button type="submit" size="sm" disabled={busy || name.trim().length < 2} data-testid="human-save">
        Use name
      </Button>
      {initial && (
        <Button type="button" variant="ghost" size="sm" onClick={() => setEditing(false)}>
          Cancel
        </Button>
      )}
      {error && <span className="text-xs text-bad-foreground">{error}</span>}
    </form>
  );
}
