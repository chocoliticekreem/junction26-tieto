import { NextResponse } from "next/server";
import { clearDemoState } from "@/lib/demo-state";

export async function GET(req: Request) {
  const res = NextResponse.redirect(new URL("/", req.url), 303);
  clearDemoState(res);
  return res;
}
