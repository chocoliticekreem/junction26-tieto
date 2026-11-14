# Metrics: what the numbers are and where they come from

Produced by `uv run receipts metrics` from `bench/manifest.json` and the runs under `runs/`. Each table's `note` field holds its exact definitions.

## Provenance (read before quoting a number)

| Tables | Source | Pipeline version |
|---|---|---|
| T1, T2, T5, T6, `summary.json` cold/warm, `trust.json` | Bench receipts in `runs/mock-*` (36 cases, cold and warm) | Pipeline as of PR #11, plus the bench's own `--no-ab` flag. Recorded Sat 14 Nov 2026 between 07:10 and 09:10 EET. |
| T3 | Routers re-run over the same 641 bench hunks at `receipts metrics` time | Current routers, including the PR #13 risk floors for codegen templates |
| T4 | The four Tieto runs `runs/ju26-issue-*` | Current pipeline (PR #15) |
| T7 | Upstream comparison, written by hand from `gh` and the diffs | Not pipeline output |

The bench was not re-run after the adversarial-review fixes (PR #13: verification tied to each claim's own artifact) or the reader vote (PR #15). Those changed the lens prompts, so the recorded fixtures do not replay, and the Anthropic key reached its spend limit during the warm run. Expect the PR #13 verification rule to lower recall somewhat (some catches were verified by looser matching) and to lower false alarms. The re-run command is in `BLOCKED.md`.

## Known limits of these numbers

- **The warm run is incomplete.** The usage limit hit during it: 11 of 36 warm cases ran without lenses. They are excluded from warm figures, not counted as misses (`summary.json` lists them). The fair cold-versus-warm comparison is on the 25 cases complete in both runs.
- **Cold-to-warm answers come from a script.** `bench-oracle (simulated human)` answers from the bench's ground truth. It is not a person, and it is labelled everywhere it appears.
- **Most bench false alarms are equivalent mutants.** Mutants on guards that change no behaviour survive because no test can kill them. Most control and bait false alarms are such test-quality findings. A real deployment would need equivalent-mutant filtering or a human dismiss with a reason, which then feeds the area's trust.
- **The bait patches contain code comments that say the code is deliberate.** That made "no finding on bait" easier than in real life in this run. The justification comments are now stripped from the 2283 and 2234 bait plants (the 2129 comment copies text the codebase itself uses for the same convention), so the next run measures the ledger, not the comment.
- **Mutation fates in the bench runs are own-tests-only.** Since PR #24, survivors are confirmed against the full suite.
- **Planted defects are a proxy.** In production the measure is reverts and incidents per area, fed back into trust levels.
- **The security lens caught 0 of 4 security plants on its own.** Three were caught by tests the other lenses wrote, which is why receipts matter more than lens labels. It is still a weak lens and is reported as one.
