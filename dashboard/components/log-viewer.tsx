"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { MonoBlock } from "./pills";

export function LogViewer({ changeId, runTs, logPath, label }: { changeId: string; runTs: string; logPath: string; label?: string }) {
  const [state, setState] = useState<{ open: boolean; text?: string; meta?: string; error?: string; loading: boolean }>({ open: false, loading: false });

  async function toggle() {
    if (state.open) return setState((s) => ({ ...s, open: false }));
    if (state.text !== undefined) return setState((s) => ({ ...s, open: true }));
    setState((s) => ({ ...s, loading: true }));
    const q = new URLSearchParams({ change: changeId, run: runTs, path: logPath, lines: "120" });
    const res = await fetch(`/api/log?${q}`);
    if (!res.ok) {
      const err = (await res.json().catch(() => ({})))?.error ?? `HTTP ${res.status}`;
      return setState({ open: true, loading: false, error: err });
    }
    const data = (await res.json()) as { text: string; truncated: boolean; totalLines: number };
    setState({
      open: true,
      loading: false,
      text: data.text,
      meta: data.truncated ? `last 120 of ${data.totalLines} lines` : `${data.totalLines} lines`,
    });
  }

  return (
    <div className="w-full min-w-0 space-y-1.5">
      <Button variant="outline" size="xs" onClick={() => void toggle()} disabled={state.loading} data-testid="log-toggle">
        {state.open ? "Hide log" : state.loading ? "Loading" : label ?? "View log"}
        <span className="font-mono text-[10px] text-muted-foreground">{logPath}</span>
      </Button>
      {state.open && state.error && <p className="text-xs text-bad-foreground">{state.error}</p>}
      {state.open && state.text !== undefined && (
        <div>
          {state.meta && <div className="mb-1 text-[10px] text-muted-foreground">{state.meta}</div>}
          <MonoBlock data-testid="log-excerpt">{state.text}</MonoBlock>
        </div>
      )}
    </div>
  );
}
