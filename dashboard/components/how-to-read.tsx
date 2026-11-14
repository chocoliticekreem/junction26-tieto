"use client";

import { X } from "lucide-react";
import { useEffect, useState } from "react";

const KEY = "receipts.how-to-read.dismissed";

function read(): boolean {
  try {
    return localStorage.getItem(KEY) === "1";
  } catch {
    return false;
  }
}

function write(): void {
  try {
    localStorage.setItem(KEY, "1");
  } catch {
    // Private windows and blocked storage: the strip just shows again next time.
  }
}

/** A one-time strip at the top of a change page: three bullets on how to read a receipt. */
export function HowToRead() {
  const [show, setShow] = useState(false);
  useEffect(() => {
    if (!read()) setShow(true);
  }, []);
  if (!show) return null;
  return (
    <div className="flex items-start gap-3 rounded-lg border border-info-foreground/25 bg-info px-3.5 py-2.5 text-sm text-info-foreground" data-testid="how-to-read" role="note">
      <div className="min-w-0 flex-1">
        <div className="text-xs font-semibold tracking-tight">How to read this page</div>
        <ul className="mt-1 list-disc space-y-0.5 pl-4 leading-5">
          <li>Green lines are things that ran and passed; red lines ran and failed. Nothing here is an opinion.</li>
          <li>Hunches are suspicions without proof. They are shown apart and never count against the change.</li>
          <li>The AI never approves. It shows what was and was not checked; a named person decides.</li>
        </ul>
      </div>
      <button
        type="button"
        className="shrink-0 rounded-sm p-0.5 text-info-foreground/70 hover:text-info-foreground"
        aria-label="Dismiss"
        data-testid="how-to-read-dismiss"
        onClick={() => {
          write();
          setShow(false);
        }}
      >
        <X className="size-4" aria-hidden />
      </button>
    </div>
  );
}
