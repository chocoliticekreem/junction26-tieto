import { NextResponse } from "next/server";

export const READONLY_MESSAGE = "Public demo: decisions are recorded by the named reviewer on their own machine.";
export const READONLY_NOTE = "Demo site — signing happens on the reviewer's machine";

export function isReadOnly(): boolean {
  return process.env.RECEIPTS_READONLY === "1";
}

/** Write routes return this first, before any fs write or git call. */
export function readOnlyRefusal(): NextResponse | null {
  return isReadOnly() ? NextResponse.json({ error: READONLY_MESSAGE }, { status: 403 }) : null;
}
