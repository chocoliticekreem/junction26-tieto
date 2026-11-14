# Receipts: AI review you can audit

**Recommendation.** Stop asking an AI reviewer for an opinion. Ask it for receipts. A finding is shown to the human only when an executed artifact backs it. Every gap is declared, each code area earns trust from a measured track record, and a named human signs off on exactly what was and was not checked.

Tieto's question: *How can AI review the code made by AI so that humans can trust it?* Our answer: trust comes from evidence the human can check in seconds, not from the reviewer's confidence.

## The four trust holes, and what closes each

| Hole | Today | Receipts |
|---|---|---|
| Findings are opinions | "Possible null dereference" | No receipt, no finding: a failing test, a surviving mutant, a byte diff or a failed regeneration. Unbacked suspicions are listed apart as hunches. |
| Nobody knows what was not checked | "LGTM" | Every change carries "not checked, and why". Silence is never approval. |
| No track record | All-or-nothing trust | Per-lens recall and false alarms on a bench with known answers; trust per code area, raised by correct verdicts and reset after an escape. |
| No owner | "The AI approved it" | The AI never merges. A named human signs off or picks A or B, and the decision record names them and the evidence. |

## Principles

1. **No receipt, no finding.** Findings need an executed artifact. Hunches are shown, collapsed, labelled "no receipt".
2. **Declare the gaps.** The not-checked list is generated from what did not run, never written by hand.
3. **Depth follows risk.** Rule floors first, then a cheap router (Jev, about $0.001 per change), then six lenses only on hunks that need them. Generated code is verified by regeneration as one block.
4. **The AI advises; a named human decides.** Genuine ambiguity is refused, not guessed: the reviewer builds branch A and branch B, runs the receipts on both, and the human merges one.
5. **Ask, don't assume, on a budget.** At most 3 questions per change, only when the answer flips the verdict, written to a ledger so the same question is never asked twice.
6. **Trust is earned per area and withdrawn after escapes.** Level 0 to 3 per area; only level 3 areas can ever be auto-ready, and public API, serializer format and security never are.
7. **Independent reviewers.** Each lens sees the requirement, its hunks and the ledger. It never sees the implementing agent's reasoning, and the requirement-test lens never sees the change's own tests.
8. **Code never leaves the company.** Self-hostable; every model call is behind one interface with record and replay, so on-prem models can be swapped in.

## How it works on one change (REQ-2283, 4 files)

1. **Intake** splits the diff into hunks and classifies files (source, test, docs, generated, template).
2. **Routing**: the RuleRouter's floors cannot be lowered. Jev scores each hunk (about 200 ms each), and Claude Haiku is the fallback.
3. **Sandbox** (.NET, real): builds base and head and runs the source generator tests: 167/167 on base, 170/170 on head.
4. **Lenses**: requirement-tests, logic-edge, compatibility, security, product-context, test-quality.
5. **Receipt checks**: the requirement-tests lens wrote 4 tests from the issue text alone. They **fail on base (the generator and analyzer do not survive the closed generic struct) and pass on head**. Mutation testing on `TypeCollector.cs` left **4 surviving mutants**: the change's own tests still pass when the diagnostic-reporting lines are deleted.
6. **Verdict**: needs sign-off. Four verified findings, all test-quality gaps of medium severity, each linked to its mutant. 12 hunches withheld. Not checked: runtime test suites, NativeAOT and Unity targets, the ExecutionTests project (needs a newer compiler than the sandbox SDK).
7. **The human** reads one page, sees exactly what was proven and what was not, and signs off by name.

## What it found on Tieto's four agent-written changes

| Change | Files (generated) | Verdict | Why |
|---|---|---|---|
| 2283 crash on closed generic struct | 4 (0) | needs sign-off | Fix proven (fail on base, pass on head); the change's tests have no teeth for diagnostic reporting (4 surviving mutants). |
| 2234 analyzer off with SuppressSourceGeneration | 4 (0) | decision A/B | Two plausible readings of the requirement. Both branches were built and tested; the human chooses. |
| 2129 formatter for type in referenced assembly | 28 (0) | blocked | Executed tests show the type's own `[Key]`/`[IgnoreMember]` attributes are ignored, and a formatter nested in a non-partial type fails to compile (CS0260). |
| 2297 ExcludeFromCodeCoverage on generated code | 237 (222) | blocked, and A/B refused | 222 generated files verified by 89 snapshot tests and never read; 15 reviewed. Two generated tests show the attribute is dropped on netstandard2.0 and .NET Framework, where it exists. The setting/no-setting ambiguity is refused and both readings are built, but neither can merge until the shared defect is fixed. |

## Measured (bench of 36 changes with planted defects)

BENCH_NUMBERS

## Trade-offs

- **Receipts cost compute.** A full review of the four sample changes took 3.5 to 11 minutes (including A/B branches) and $0.48 to $1.05 in model calls each. Depth-by-risk keeps the expensive part to the hunks that need it, and generated code costs a test run, not a read.
- **Receipts are only as strong as the checks.** A byte diff over 124 sample values can miss a case outside the set (it did once, during building). Equivalent mutants survive on code that changes no behaviour. We report these as limits rather than hide them.
- **Hunches are withheld from the main view.** A real bug the reviewer could not prove stays a hunch. That costs some recall and buys trust; the hunch list stays one click away.
- **Questions interrupt.** Capped at 3, only verdict-flipping ones, never repeated.

## Limits (what would make us not trust our own output)

- Low-recall lenses and areas in the track-record tables.
- Router misses: planted hunks a router scored low (reported per router).
- Anything in "not checked": here, runtime test suites, NativeAOT and Unity, other target frameworks.
- A track record measured on planted defects is a proxy. In production, the measure is reverts and incidents per area, fed back into trust levels.

## How it scales

Depth follows risk, so cost grows with risky lines, not total lines. Generated code is verified as a block: 222 of 237 files in 2297 needed no reading. The ledger turns each answered question into context, so questions fall over time. Sandboxes are per change type: .NET is built; SQL (throwaway Postgres), web UI (Playwright screenshots) and mobile (simulator flows) follow the same receipt shape.

## Who owns the decision

The named human in `decisions/<change>.json`: who, which choice, which evidence ids, and the merge commit. The AI never merges, and the dashboard refuses to merge a branch whose own receipt is blocked.
