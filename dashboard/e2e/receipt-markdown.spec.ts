import { expect, test } from "@playwright/test";
import { receiptMarkdown } from "../lib/receipt-markdown";
import type { Decision, RunBundle, TrustReceipt } from "../lib/types";

// Unit tests for the PR-comment generator. No server needed; Playwright is the dashboard's only test runner.

function run(overrides: Partial<TrustReceipt> = {}, evidence: RunBundle["evidence"] = []): RunBundle {
  const receipt: TrustReceipt = {
    change_id: "mock/2283/logic",
    requirement_id: "REQ-2283",
    requirement_title: "Fix the crash | keep bytes identical",
    requirement_text: "",
    run_ts: "20261114T063000Z",
    head_sha: "9f3c1a7e2b4d5c6f8a9b0c1d2e3f4a5b6c7d8e9f",
    verdict: "blocked",
    verdict_reason: "requirement test fails on head",
    risk: 8,
    areas: ["tests"],
    checked: [{ what: "Base | head compile", by: "sandbox", evidence_id: "E-build", result: "pass" }],
    findings: [
      {
        id: "F-1",
        lens: "logic-edge",
        claim: "Off-by-one in the loop bound",
        location: "src/A.cs:10",
        severity: "high",
        receipt_plan: { kind: "test", detail: "boundary test" },
        verified: true,
        evidence_ids: ["E-req", "E-missing"],
      },
    ],
    hunches: [],
    refused: [{ what: "Pick between two readings", why: "the requirement is ambiguous" }],
    not_checked: [{ what: "NativeAOT", why: "not built in the sandbox" }],
    residual_risk: "Line one.\nLine two.",
    routing: {},
    counts: {},
    ...overrides,
  };
  return { changeId: receipt.change_id, runTs: receipt.run_ts, receipt, commands: [], tests: [], evidence, findings: [], mutants: [], questions: [] };
}

test("markdown carries requirement, verdict, checked table, findings with receipts, refused, not checked, residual risk", () => {
  const md = receiptMarkdown(
    run({}, [
      { id: "E-build", kind: "build", title: "Head builds", result: "pass", command_ids: ["c1"] },
      { id: "E-req", kind: "fail-before-pass-after", title: "Req tests fail on base, pass on head", result: "fail", command_ids: ["c2", "c3"] },
    ]),
  );
  expect(md.startsWith("## Trust receipt: REQ-2283 — Fix the crash | keep bytes identical\n")).toBe(true);
  expect(md).toContain("**Verdict: Blocked** — requirement test fails on head");
  expect(md).toContain("| What | By | Result | Receipt |");
  expect(md).toContain("| Base \\| head compile | sandbox | pass | `E-build` |");
  expect(md).toContain("- **high** · logic-edge · `F-1` — Off-by-one in the loop bound");
  expect(md).toContain("  at `src/A.cs:10`");
  expect(md).toContain("  receipt: `E-req` fail-before-pass-after: Req tests fail on base, pass on head — fail, 2 executed commands");
  expect(md).toContain("  receipt: `E-missing` (evidence record missing)");
  expect(md).toContain("### Unverified hunches\n\nNone withheld.");
  expect(md).toContain("### Refused to decide (1)\n\n- **Pick between two readings** — the requirement is ambiguous");
  expect(md).toContain("### Not checked (1)\n\n- **NativeAOT** — not built in the sandbox");
  expect(md).toContain("> Line one.\n> Line two.");
  expect(md).toContain("**Signed off by: — (pending)**");
  expect(md).toContain("<sub>PullReceipts run 20261114T063000Z on 9f3c1a7e2b · change `mock/2283/logic`</sub>");
});

test("empty sections say so instead of disappearing, and hunches are counted not listed", () => {
  const md = receiptMarkdown(run({ checked: [], findings: [], refused: [], not_checked: [], hunches: [run().findings[0]] }));
  expect(md).toContain("### Checked (0)\n\nNothing recorded as checked.");
  expect(md).toContain("No verified findings. This is not approval");
  expect(md).toContain("1 hunch withheld: no executed receipt, not shown as findings.");
  expect(md).not.toContain("Off-by-one");
  expect(md).toContain("Nothing refused.");
  expect(md).toContain("The run declared no gaps. Treat with suspicion.");
});

test("a recorded decision replaces the pending sign-off line", () => {
  const decision: Decision = { change_id: "mock/2283/logic", requirement_id: "REQ-2283", who: "Anson Test", choice: "A", evidence_ids: [], ts: "2026-11-14T10:00:00+00:00" };
  const md = receiptMarkdown(run(), decision);
  expect(md).toContain("**Merged A by: Anson Test** at 2026-11-14T10:00:00+00:00");
  expect(md).not.toContain("(pending)");
});
