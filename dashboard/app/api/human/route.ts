import { NextResponse } from "next/server";
import { HUMAN_COOKIE, normalizeName } from "@/lib/human";

export async function POST(req: Request) {
  const body = await req.json().catch(() => ({}));
  const name = normalizeName(body?.name);
  if (!name) return NextResponse.json({ error: "Name must be at least 2 characters." }, { status: 400 });
  const res = NextResponse.json({ name });
  res.cookies.set(HUMAN_COOKIE, name, { path: "/", httpOnly: false, sameSite: "lax", maxAge: 60 * 60 * 24 * 365 });
  return res;
}

export async function DELETE() {
  const res = NextResponse.json({ ok: true });
  res.cookies.set(HUMAN_COOKIE, "", { path: "/", maxAge: 0 });
  return res;
}
