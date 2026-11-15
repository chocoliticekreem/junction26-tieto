# Metrics: what the numbers are and where they come from

Produced by `uv run receipts metrics` from `bench/manifest.json` and the runs under `runs/`. Each table's `note` field holds its exact definitions.

## Provenance (read before quoting a number)

| Tables | Source | Pipeline version |
|---|---|---|
| T1, T2, T5, T6, `summary.json` cold/warm, `trust.json` | Bench receipts in `runs/mock-*` (36 cases cold; warm on the 18 cases of issues 2283 and 2234) | **Final pipeline** (PR #13 claim-tied verification, PR #15 reader vote, PR #24 two-tier mutation, bait comments stripped), plus the bench's own `--no-ab` flag. Re-run Sat 14 Nov 2026 through the Message Batches API: cold batches 13:03-13:14 EET, warm 22:08-22:50 EET. |
| T3 | Routers re-run over the same bench hunks at `receipts metrics` time | Current routers, including the PR #13 risk floors for codegen templates |
| T4 | The four Tieto runs `runs/ju26-issue-*` | Current pipeline (PR #15) |
| T7 | Upstream comparison, written by hand from `gh` and the diffs | Not pipeline output |
| `verda/`, `provider-comparison.json` | `runs/*@verda` (Kimi K3.1 Pro on Verda, every role) next to the Anthropic runs | Same final pipeline; see `docs/verda.md` |

### Sun 15 Nov: 24/24 cold

The three cold misses of the 14 Nov run were re-run on the current pipeline (04:17-04:30 EET); the other 33 cold runs are the 14 Nov ones.

- `mock-2297-drift`: caught after PR #63 (requirement claims placed at the producing template, so the docs guard no longer drops the receipt).
- `mock-2129-missing-test`: caught after PR #64 (a deleted passing test the ticket does not ask to remove is a verified finding).
- `mock-2297-missing-test`: caught after PR #64 and PR #65. PR #65 corrected this case's ground truth after the miss: the manifest gave the deleted file the range `:0-0`, which no finding in a deleted file could match; it is now scored at file level, like every plant listed without lines.

Both fixes were checked on the 4 clean and 4 control cases in scratch runs: no new findings and no new false alarms. Warm numbers are unchanged.

The PR #11 bench (recorded 07:10-09:10 EET) is replaced. Its summaries stay in
`runs/bench-cold-summary-pr11.json` and `runs/bench-warm-summary-pr11.json`; its run folders are in
git history.

### New numbers vs PR #11 (cold, 36 cases)

| | PR #11 | Final pipeline | Why it moved |
|---|---|---|---|
| Planted defects caught | 19/24 | **21/24** (24/24 after the 15 Nov fixes) | +1 edge plant, +1 security plant (T2). Claim-tied verification was expected to cost recall; it did not, because catches now come from tests tied to the claim (fail-before-pass-after verifies 10 of 12 in-scope plants). One run each, so a 2-case difference is within run-to-run variation. |
| Escapes | 0 | 0 | |
| Control/bait cases with a false alarm | 4/8 | 4/8 | Unchanged. Mostly equivalent mutants (test-quality). |
| False-alarm findings at bait lines | 8 | 8 | Stripping the "deliberate" comments did not add bait false alarms, and did not remove any. |
| Questions per change | 0.25 (9) | **0.81** (29) | The batched cold run builds every prompt against the empty ledger. In the PR #11 cold run, cases that finished early wrote oracle answers that later "cold" cases saw (39 ledger citations in a cold run). The new figure is the first truly cold baseline. |
| Ledger citations (findings withdrawn per a ledger entry) | 39 cold, 36 warm | 0 cold, 0 warm | Same cause in cold. In warm, the oracle's answers did not lead any lens to withdraw a finding: the bait false alarms stayed at 3 (2283) and 5 (2234). |
| decision-A-B verdicts | 2 | 0 | The reader vote (PR #15) needs 2 of 3 readers to call a requirement ambiguous. |
| Spend | about USD 17.63 live | **USD 10.20** batched (USD 26.68 at live list price) | Message Batches at 50%. |

Warm (2283 and 2234, 18 cases): 11/12 caught (PR #11 warm: 17/18 on a different set of 25 cases).
Questions fell from 9 cold to 5 warm on these 18 cases (2234: 7 to 0; 2283: 2 to 5, so the ledger
helped one issue and not the other). The headline tile "0.81 to 0.28" compares 36 cold cases with
18 warm ones; on the same 18 cases it is 0.50 to 0.28.

## Known limits of these numbers

- **The warm run covers two issues.** To stay inside the spend cap, warm ran on the 18 cases of 2283 and 2234 only. Compare warm with cold on those 18 cases, not on all 36.
- **Cold-to-warm answers come from a script.** `bench-oracle (simulated human)` answers from the bench's ground truth. It is not a person, and it is labelled everywhere it appears.
- **Most bench false alarms are equivalent mutants.** Mutants on guards that change no behaviour survive because no test can kill them. Most control and bait false alarms are such test-quality findings. A real deployment would need equivalent-mutant filtering or a human dismiss with a reason, which then feeds the area's trust.
- **Bait comments.** The justification comments are stripped from the 2283 and 2234 bait plants (the 2129 comment copies text the codebase itself uses for the same convention), so this run measures the ledger, not the comment.
- **Mutation fates are two-tier** (PR #24): a mutant the change's own tests miss is re-run against the full suite before it counts as a survivor.
- **Planted defects are a proxy.** In production the measure is reverts and incidents per area, fed back into trust levels.
- **The security lens caught 0 of 4 security plants on its own.** All four were caught by tests the other lenses wrote (T2: logic-edge), which is why receipts matter more than lens labels. It is still a weak lens and is reported as one.
