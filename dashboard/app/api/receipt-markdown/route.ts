import { NextResponse } from "next/server";
import { loadRun, readDecision } from "@/lib/data";
import { receiptMarkdown } from "@/lib/receipt-markdown";

export async function GET(req: Request) {
  const url = new URL(req.url);
  const change = url.searchParams.get("change") ?? "";
  if (!change) return NextResponse.json({ error: "change is required" }, { status: 400 });
  const run = loadRun(change);
  if (!run) return NextResponse.json({ error: `No run for change ${change}` }, { status: 404 });
  return new NextResponse(receiptMarkdown(run, readDecision(change)), {
    headers: { "content-type": "text/markdown; charset=utf-8" },
  });
}
