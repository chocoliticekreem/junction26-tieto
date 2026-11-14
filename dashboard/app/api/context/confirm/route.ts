import { NextResponse } from "next/server";
import { readContextMarkdown, writeContextStatus } from "@/lib/data";
import { confirmContext, contextStatus, isEphemeral, readDemoState, saveDemoState } from "@/lib/demo-state";
import { nowIso, requireHuman } from "@/lib/human";
import { readOnlyRefusal } from "@/lib/readonly";

export async function POST() {
  const readOnly = readOnlyRefusal();
  if (readOnly) return readOnly;
  const auth = await requireHuman();
  if ("error" in auth) return auth.error;
  if (!readContextMarkdown()) return NextResponse.json({ error: "No REVIEW_CONTEXT.md to confirm." }, { status: 404 });
  if ((await contextStatus()).status === "confirmed") return NextResponse.json({ error: "Already confirmed." }, { status: 409 });
  const status = { status: "confirmed" as const, confirmed_by: auth.who, ts: nowIso() };
  if (isEphemeral()) {
    const res = NextResponse.json({ status });
    saveDemoState(res, confirmContext(await readDemoState(), auth.who, status.ts));
    return res;
  }
  writeContextStatus(status);
  return NextResponse.json({ status });
}
