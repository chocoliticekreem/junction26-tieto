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
6. **Verdict**: needs sign-off. Four verified findings, all test-quality gaps of medium severity, each linked to its mutant. 15 hunches withheld; one edge-case test the lens wrote did not compile, and the receipt says so instead of hiding it. Not checked: runtime test suites, NativeAOT and Unity targets, the ExecutionTests project (needs a newer compiler than the sandbox SDK).
7. **The human** reads one page, sees exactly what was proven and what was not, and signs off by name.

## What it found on Tieto's four agent-written changes

| Change | Files (generated) | Verdict | Why |
|---|---|---|---|
| 2283 crash on closed generic struct | 4 (0) | needs sign-off | Fix proven (fail on base, pass on head); the change's tests have no teeth for diagnostic reporting (4 surviving mutants). |
| 2234 analyzer off with SuppressSourceGeneration | 4 (0) | needs sign-off | Requirement tests fail on base and pass on head. 6 of 12 mutants survive on `TypeCollector.cs`. 0 of 3 independent readers found two readings. |
| 2129 formatter for type in referenced assembly | 28 (0) | blocked, and A/B refused | Executed tests show the external type's own attributes are not honoured (2 of 2 fail on head). Bytes for 124 runtime cases are unchanged; public API +3/-0. 3 of 3 readers found two readings (contractless only vs an optional surrogate type); both are built, both blocked. |
| 2297 ExcludeFromCodeCoverage on generated code | 237 (222) | blocked, and A/B refused | 222 generated files verified by 89 snapshot tests and never read; 15 reviewed. Generated tests show the attribute is dropped on netstandard2.0 and .NET Framework, where it exists. 3 of 3 readers found two readings (setting or no setting); both are built, but neither can merge until the shared defect is fixed. |

## Measured (bench of 36 changes with planted defects)

Each of the four Tieto changes was turned into 9 variants with known answers: clean, plus 8 planted defects or traps (logic, edge, compatibility, security, missing test, drift from the requirement, bait that looks like a bug but matches a convention, harmless control). The labels never reach the reviewer. Full tables: `metrics/` (provenance and limits in `metrics/README.md`).

| Measure | Result |
|---|---|
| Planted defects caught (cold run) | **19 of 24 (79 %)**; 3 of the 5 misses still left an unverified hunch at the planted line |
| Escapes (a planted defect marked auto-ready) | **0** |
| Findings shown with an executed receipt | 100 % by construction; unbacked claims are hunches |
| Control and bait cases with a false alarm | 4 of 8, almost all surviving mutants on code that changes no behaviour |
| Questions per change, cold vs warm | **0.25 to 0.04** (9 to 1; on the 25 cases complete in both runs, 7 to 1); 0 repeated questions |
| Share of 2297 routed away from humans | **222 of 237 files (94 %)**, all verified by regeneration |
| Reading time with receipts, 2297 | about 4 minutes instead of 28 (T4; assumption: 2 minutes per 100 changed lines read) |
| Time to a human-ready receipt | 3.5 to 11 minutes per change, unattended |
| Model cost per change | about $0.50 to $1.00 in lenses; routing about $0.001 (Jev) |

Per lens (cold): byte diff 4 of 4, compatibility lens 4 of 4, logic-edge 6 of 8, requirement-tests 5 of 8, tests failing on base and passing on head 9 of 12. The security lens caught 0 of 4 on its own; 3 of those plants were caught by tests the other lenses wrote. That is the case for receipts over lens labels, and it marks the security lens as the weakest link.

Routing (T3, 641 hunks): every router reaches the same end-to-end recall (79 %), because the requirement and compatibility lenses see every hunk. Jev routes the fewest planted hunks low (3 of 27, against 6 for Haiku or rules alone) for $0.05. On this codebase, though, the rule floors do most of the work, so a learned router earns its place where rules have no floor.

These numbers come from the pipeline before the last two hardening steps (claim-tied verification, reader vote); see `metrics/README.md`. A re-run on the final pipeline is queued behind an API spend limit.

## Trade-offs

- **Receipts cost compute.** A full review of the four sample changes took 3.5 to 11 minutes (including A/B branches) and $0.53 to $0.97 in lens calls each. Depth-by-risk keeps the expensive part to the hunks that need it, and generated code costs a test run, not a read.
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
