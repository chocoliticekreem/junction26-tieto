import { NextResponse } from "next/server";
import { readLog } from "@/lib/data";

export async function GET(req: Request) {
  const url = new URL(req.url);
  const change = url.searchParams.get("change") ?? "";
  const run = url.searchParams.get("run") ?? "";
  const path = url.searchParams.get("path") ?? "";
  const lines = Math.min(2000, Math.max(1, Number(url.searchParams.get("lines") ?? 200) || 200));
  if (!change || !run || !path) return NextResponse.json({ error: "change, run and path are required" }, { status: 400 });
  const log = readLog(change, run, path, lines);
  if (!log) return NextResponse.json({ error: `Log not found: ${path}` }, { status: 404 });
  return NextResponse.json(log);
}
