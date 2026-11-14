import { cookies } from "next/headers";
import { NextResponse } from "next/server";

export const HUMAN_COOKIE = "receipts_human";

export function normalizeName(raw: unknown): string | null {
  if (typeof raw !== "string") return null;
  const name = raw.trim().replace(/\s+/g, " ").slice(0, 60);
  return name.length >= 2 ? name : null;
}

export async function currentHuman(): Promise<string | null> {
  const jar = await cookies();
  return normalizeName(jar.get(HUMAN_COOKIE)?.value);
}

/** Route handlers call this first; every write needs a named human. */
export async function requireHuman(): Promise<{ who: string } | { error: NextResponse }> {
  const who = await currentHuman();
  if (!who) {
    return {
      error: NextResponse.json(
        { error: "No named human. Pick your name in the header before writing." },
        { status: 400 },
      ),
    };
  }
  return { who };
}

export function nowIso(): string {
  return new Date().toISOString().replace(/\.\d{3}Z$/, "+00:00");
}
