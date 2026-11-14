"use client";

import { useState } from "react";
import type { ProvenLine } from "@/lib/proven";
import { ResultPill } from "./pills";

const MAX_LINES = 4;

function isOpen(el: Element | null) {
  return el?.getAttribute("aria-expanded") === "true";
}

function nextFrame() {
  return new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
}

async function waitFor(selector: string): Promise<Element | null> {
  for (let i = 0; i < 20; i++) {
    const el = document.querySelector(selector);
    if (el) return el;
    await nextFrame();
  }
  return null;
}

/** The card lives inside a closed disclosure (Receipts, or a finding); open whatever hides it, then the card itself. */
async function revealEvidence(id: string) {
  const selector = `#evidence-${CSS.escape(id)}`;
  let card = document.querySelector(selector);
  for (const toggle of ['[data-testid="receipts-toggle"]', '[data-testid="finding-toggle"]']) {
    if (card) break;
    document.querySelectorAll<HTMLElement>(toggle).forEach((t) => {
      if (!isOpen(t)) t.click();
    });
    card = await waitFor(selector);
  }
  if (!card) return;
  const toggle = card.querySelector<HTMLElement>('[data-testid="evidence-toggle"]');
  if (toggle && !isOpen(toggle)) toggle.click();
  history.replaceState(null, "", `#evidence-${id}`);
  card.scrollIntoView({ block: "start" });
}

export function Proven({ lines }: { lines: ProvenLine[] }) {
  const [showAll, setShowAll] = useState(false);
  if (lines.length === 0) return null;
  const shown = showAll ? lines : lines.slice(0, MAX_LINES);
  const hidden = lines.length - shown.length;
  return (
    <div className="mt-2 space-y-1 text-sm" data-testid="proven">
      <div className="text-xs font-semibold">Proven</div>
      {shown.map((l) => (
        <div key={l.id} className="flex items-start gap-2">
          <ResultPill result="pass" className="mt-0.5" />
          <a
            href={`#evidence-${l.id}`}
            className="min-w-0 leading-5 underline-offset-4 hover:underline [overflow-wrap:anywhere]"
            data-testid="proven-line"
            onClick={(e) => {
              e.preventDefault();
              void revealEvidence(l.id);
            }}
          >
            {l.label}
          </a>
        </div>
      ))}
      {hidden > 0 && (
        <button type="button" className="text-xs text-muted-foreground hover:text-foreground" data-testid="proven-more" onClick={() => setShowAll(true)}>
          +{hidden} more
        </button>
      )}
    </div>
  );
}
