import { NextResponse } from "next/server";
import { readContextMarkdown, readContextStatus, writeContextStatus } from "@/lib/data";
import { nowIso, requireHuman } from "@/lib/human";
import { readOnlyRefusal } from "@/lib/readonly";

export async function POST() {
  const readOnly = readOnlyRefusal();
  if (readOnly) return readOnly;
  const auth = await requireHuman();
  if ("error" in auth) return auth.error;
  if (!readContextMarkdown()) return NextResponse.json({ error: "No REVIEW_CONTEXT.md to confirm." }, { status: 404 });
  if (readContextStatus().status === "confirmed") return NextResponse.json({ error: "Already confirmed." }, { status: 409 });
  const status = { status: "confirmed" as const, confirmed_by: auth.who, ts: nowIso() };
  writeContextStatus(status);
  return NextResponse.json({ status });
}
