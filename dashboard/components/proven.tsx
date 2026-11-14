"use client";

import { CircleCheck } from "lucide-react";
import { useState } from "react";
import type { ProvenLine } from "@/lib/proven";

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
  for (const toggle of ['[data-testid="tech-toggle"]', '[data-testid="receipts-toggle"]', '[data-testid="show-all-findings"]', '[data-testid="finding-toggle"]']) {
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
    <section id="proven" className="scroll-mt-16 rounded-lg border border-ok-foreground/20 bg-ok/40 px-3.5 py-3 text-sm dark:bg-ok/30" data-testid="proven" aria-label="proven">
      <div className="text-xs font-semibold text-ok-foreground">Proven</div>
      <ul className="mt-1.5 space-y-1">
        {shown.map((l) => (
          <li key={l.id} className="flex items-start gap-2">
            <CircleCheck className="mt-1 size-3.5 shrink-0 text-ok-foreground" aria-hidden />
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
          </li>
        ))}
      </ul>
      {hidden > 0 && (
        <button type="button" className="mt-1 text-xs text-muted-foreground hover:text-foreground" data-testid="proven-more" onClick={() => setShowAll(true)}>
          +{hidden} more
        </button>
      )}
    </section>
  );
}
