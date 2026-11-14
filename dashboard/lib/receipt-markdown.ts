import type { Decision, Evidence, RunBundle } from "./types";

const verdictLabel: Record<string, string> = {
  "auto-ready": "Auto-ready",
  "needs-sign-off": "Needs sign-off",
  "decision-A-B": "Decision A/B",
  blocked: "Blocked",
};

function cell(v: string | undefined | null): string {
  return (v ?? "—").replace(/\r?\n/g, " ").replace(/\|/g, "\\|").trim() || "—";
}

function code(v: string): string {
  return "`" + v.replace(/`/g, "'") + "`";
}

function receiptSummary(ev: Evidence): string {
  const cmds = ev.command_ids?.length ? `, ${ev.command_ids.length} executed command${ev.command_ids.length === 1 ? "" : "s"}` : "";
  return `${code(ev.id)} ${ev.kind}: ${ev.title} — ${ev.result}${cmds}`;
}

/** GitHub-flavoured markdown of a trust receipt, suitable for posting as a PR comment. */
export function receiptMarkdown(run: RunBundle, decision?: Decision): string {
  const r = run.receipt;
  const evidenceById = new Map(run.evidence.map((e) => [e.id, e]));
  const out: string[] = [];

  out.push(`## Trust receipt: ${r.requirement_id} — ${r.requirement_title}`);
  out.push("");
  out.push(`**Verdict: ${verdictLabel[r.verdict] ?? r.verdict}** — ${r.verdict_reason}`);
  out.push("");

  out.push(`### Checked (${r.checked.length})`);
  out.push("");
  if (r.checked.length === 0) {
    out.push("Nothing recorded as checked.");
  } else {
    out.push("| What | By | Result | Receipt |");
    out.push("|---|---|---|---|");
    for (const c of r.checked) {
      out.push(`| ${cell(c.what)} | ${cell(c.by)} | ${cell(c.result)} | ${c.evidence_id ? code(c.evidence_id) : "—"} |`);
    }
  }
  out.push("");

  out.push(`### Verified findings (${r.findings.length})`);
  out.push("");
  if (r.findings.length === 0) {
    out.push("No verified findings. This is not approval: see “Not checked”.");
  } else {
    for (const f of r.findings) {
      out.push(`- **${f.severity}** · ${f.lens} · ${code(f.id)} — ${f.claim}`);
      out.push(`  at ${code(f.location)}`);
      const ids = f.evidence_ids ?? [];
      if (ids.length === 0) {
        out.push("  receipt: none recorded");
      } else {
        for (const id of ids) {
          const ev = evidenceById.get(id);
          out.push(`  receipt: ${ev ? receiptSummary(ev) : `${code(id)} (evidence record missing)`}`);
        }
      }
    }
  }
  out.push("");

  out.push("### Unverified hunches");
  out.push("");
  out.push(
    r.hunches.length === 0
      ? "None withheld."
      : `${r.hunches.length} hunch${r.hunches.length === 1 ? "" : "es"} withheld: no executed receipt, not shown as findings.`,
  );
  out.push("");

  out.push(`### Refused to decide (${r.refused.length})`);
  out.push("");
  if (r.refused.length === 0) out.push("Nothing refused.");
  for (const x of r.refused) out.push(`- **${x.what}** — ${x.why}`);
  out.push("");

  out.push(`### Not checked (${r.not_checked.length})`);
  out.push("");
  if (r.not_checked.length === 0) out.push("The run declared no gaps. Treat with suspicion.");
  for (const x of r.not_checked) out.push(`- **${x.what}** — ${x.why}`);
  out.push("");

  out.push("### Residual risk the signer accepts");
  out.push("");
  out.push(`> ${r.residual_risk.replace(/\r?\n/g, "\n> ")}`);
  out.push("");

  if (decision) {
    const what = decision.choice === "sign-off" ? "Signed off by" : `Merged ${decision.choice} by`;
    out.push(`**${what}: ${decision.who}** at ${decision.ts}`);
  } else {
    out.push("**Signed off by: — (pending)**");
  }
  out.push("");
  out.push(`<sub>PullReceipts run ${r.run_ts} on ${r.head_sha.slice(0, 10)} · change ${code(r.change_id)}</sub>`);
  out.push("");
  return out.join("\n");
}
