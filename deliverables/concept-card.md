# PullReceipts: AI review you can audit

**Recommendation.** Stop asking an AI reviewer for an opinion. Ask it for receipts. A finding is shown to the human only when an executed artifact backs it. Every gap is declared, each code area earns trust from a measured track record, and a named human signs off on exactly what was and was not checked.

Tieto's question: *How can AI review the code made by AI so that humans can trust it?* Our answer: trust comes from evidence the human can check in seconds, not from the reviewer's confidence.

## The four trust holes, and what closes each

| Hole | Today | PullReceipts |
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
8. **Code never leaves the company.** Self-hostable; every model call is behind one interface with record and replay, so on-prem models can be swapped in. Measured with an open-weight model on Verda (EU), below.
9. **Check what else the change touches.** A Tieto engineer, reviewing our demo: "Even if it's all green, I need to know if this implementation has the risk of impacting other features... It has to be a concrete rule." So: a hunk inside an existing member that code outside the change calls gets risk floor 8 (a deleted one, 9) and can never be auto-ready; new code used only by the change gets no extra floor. For up to 3 modified members, a lens that sees only the base code and its callers writes tests pinning today's behaviour. They must pass on base; if they fail on head, the change provably breaks an old feature (a verified high finding). Callers no test executes are listed under "not checked".

## How it works on one change (REQ-2283, 4 files)

1. **Intake** splits the diff into hunks and classifies files (source, test, docs, generated, template).
2. **Routing**: the RuleRouter's floors cannot be lowered. Jev scores each hunk (about 200 ms each), and Claude Haiku is the fallback.
3. **Sandbox** (.NET, real): builds base and head and runs the source generator tests: 167/167 on base, 170/170 on head.
4. **Lenses**: requirement-tests, logic-edge, compatibility, security, product-context, test-quality.
5. **Receipt checks**: the requirement-tests lens wrote 4 tests from the issue text alone. They **fail on base (the generator and analyzer do not survive the closed generic struct) and pass on head**. Mutation testing on the lines the change added to `TypeCollector.cs`: of 9 mutants, 3 are killed by the change's own tests, 4 only by the existing suite, and **2 survive the full suite** (`&&` swapped for `||` on line 1446, `FirstOrDefault()` for `First()` on line 1434). A Stryker.NET cross-check found the same two.
6. **Impact on other features**: the change modifies 2 existing members, `TypeCollector.GetObjectInfo` and the `TypeCollector` constructor, which 3 call sites outside the change use. All 3 are covered by tests. 4 tests written from the base code alone pin what those callers rely on; they pass on base and on head: old behaviour preserved.
7. **Verdict**: needs sign-off. The reason names the blast radius ("modifies existing code used elsewhere: TypeCollector.GetObjectInfo, 2 callers outside the change"). Three verified findings: two medium test gaps (one per full-suite survivor) and one low note that the change's own tests miss what the existing suite catches. 15 hunches withheld; one edge-case test the lens wrote did not compile, and the receipt says so instead of hiding it. Not checked: runtime test suites, NativeAOT and Unity targets, a semantic call graph.
8. **The human** reads one page, sees exactly what was proven and what was not, and signs off by name.

## What it found on Tieto's four agent-written changes

| Change | Files (generated) | Verdict | Why |
|---|---|---|---|
| 2283 crash on closed generic struct | 4 (0) | needs sign-off | Fix proven (fail on base, pass on head). 2 mutants on the new lines survive the whole test suite (confirmed by Stryker.NET), so two branches of the fix are untested. |
| 2234 analyzer off with SuppressSourceGeneration | 4 (0) | needs sign-off | Requirement tests fail on base and pass on head. 4 of 12 mutants on `TypeCollector.cs` survive the full suite. 0 of 3 independent readers found two readings. |
| 2129 formatter for type in referenced assembly | 28 (0) | blocked, and A/B refused | Executed tests show the external type's own attributes are not honoured (2 of 2 fail on head). Bytes for 124 runtime cases are unchanged; public API +3/-0. 3 of 3 readers found two readings (contractless only vs an optional surrogate type); both are built, both blocked. |
| 2297 ExcludeFromCodeCoverage on generated code | 237 (222) | blocked, and A/B refused | 222 generated files verified by 89 snapshot tests and never read; 15 reviewed. Generated tests show the attribute is dropped on netstandard2.0 and .NET Framework, where it exists. 3 of 3 readers found two readings (setting or no setting); both are built, but neither can merge until the shared defect is fixed. |
| 2297 round 2 (implementer's fix, reviewed on Verda) | 241 (222) | decision A/B | The round-1 blockers are gone: the build and 171/171 existing tests pass, 222/222 generated files regenerate, no high finding, so side A needs sign-off. 3 of 3 readers again found two readings (all generated types vs formatters only), but the builder could not write a passing side B in 2 attempts, so there is nothing to choose between yet. |

**Blast radius (principle 9)**, from `impact.json` of each run:

| Change | Existing members modified | Call sites outside the change | Untested callers | Old behaviour pinned |
|---|---|---|---|---|
| 2283 | 2 (`GetObjectInfo`, `TypeCollector` constructor) | 3 | 0 | 4 tests on 2 members: preserved |
| 2234 | 1 (`GetObjectInfo`) | 2 | 0 | 3 tests on 1 member: preserved |
| 2129 | 6 (plus 10 new members) | 14, of which 11 are `.Equals(` calls matched by name only | 6. One real: `MessagePackGenerator.cs:156`, `Comparer.Equals` (the incremental generator's comparer, untouched by the change) calls the modified `ObjectSerializationInfo.Equals`. The other 5 are name-only noise. | 7 tests on 3 members: preserved |
| 2297 | 7 (six T4 `TransformText` methods, `EmitNestingTypesAndNamespaces`) | 12 | 1: `src/MessagePack.SourceGenerator.Unity/CompositeResolverGenerator.cs:44` calls the modified `CompositeResolverTemplate.TransformText`, and no test executes it | 5 tests on 3 members: preserved |

Every change modifies code other features call, so every one carries floor 8 and can never be auto-ready. No pinned behaviour changed, so blast radius added no blocker; it added the reason on 2283 and 2234 and the two concrete coverage gaps above.

## Measured (bench of 36 changes with planted defects)

Each of the four Tieto changes was turned into 9 variants with known answers: clean, plus 8 planted defects or traps (logic, edge, compatibility, security, missing test, drift from the requirement, bait that looks like a bug but matches a convention, harmless control). The labels never reach the reviewer. All numbers are from the final pipeline, re-run Sat 14 Nov. Full tables: `metrics/` (provenance and limits in `metrics/README.md`).

| Measure | Result |
|---|---|
| Planted defects caught (cold run) | **24 of 24** |
| Escapes (a planted defect marked auto-ready) | **0** |
| Findings shown with an executed receipt | 100 % by construction (242 of 242); unbacked claims are hunches |
| Control and bait cases with a false alarm | 4 of 8 (8 false-alarm findings at bait lines), mostly surviving mutants on code that changes no behaviour |
| Questions per change, cold vs warm | **0.50 to 0.28** (9 to 5) on the same 18 cases (issues 2283 and 2234); 0 repeated questions. The ledger helped 2234 (7 to 0) and not 2283 (2 to 5) |
| Share of 2297 routed away from humans | **222 of 237 files (94 %)**, all verified by regeneration |
| Reading time with receipts, 2297 | 3.6 minutes instead of 27.8 (T4; assumption: 2 minutes per 100 changed lines read) |
| Time per review | 0.8 to 11.1 minutes per bench case live on Verda (mean 6.1); on Anthropic, 1.3 to 8 minutes after the batched model answers (mean 4.7) |
| Model cost per change | Tieto changes: $0.55 to $0.68 without A/B, $2.53 to $4.30 with A/B branches, plus $0.30 to $0.40 for the blast-radius lens. Bench case: $0.28 batched ($0.74 at list price). Routing: $0.05 for all 641 bench hunks (Jev) |

Per lens (cold, plants in its scope, T1): logic-edge 7 of 8, requirement-tests 6 of 8, compatibility lens 3 of 4, byte diff 3 of 4, test-quality (mutation) 2 of 4, tests failing on base and passing on head 10 of 12. The security lens caught 0 of 4 on its own; all 4 security plants were caught by tests the logic-edge lens wrote (T2). That is the case for receipts over lens labels, and it marks the security lens as the weakest link.

Routing (T3, 641 hunks): every router reaches the same end-to-end recall (87.5 %), because the requirement and compatibility lenses see every hunk. Jev routes the fewest planted hunks low (3 of 27, against 6 for Haiku or rules alone) for $0.05. On this codebase, though, the rule floors do most of the work, so a learned router earns its place where rules have no floor.

**Open-weight model on Verda** (Kimi K3.1 Pro in every role, hosted in Germany; same pipeline, same branches; `docs/verda.md`):

| | Anthropic (Opus 5.5 + Sonnet 5.5) | Verda (Kimi K3.1 Pro) |
|---|---|---|
| Verdicts on the four Tieto changes | 2 blocked, 2 needs sign-off | the same on all four |
| Planted defects caught, cold (14 Nov run, both providers) | 21 of 24 | 18 of 24 |
| Escapes | 0 | 0 |
| Generated tests that behave as planned | 34 of 93 | 6 of 77 |
| Cost, four Tieto changes | $8.06 | EUR 4.25 recorded (3.28 billed) |
| Cost per bench case | $0.28 batched, $0.74 list | EUR 0.47 |

The weaker model shows up as more "not checked" and fewer verified findings, never as a false green.

## Trade-offs

- **Receipts cost compute.** A review takes minutes, not seconds (0.8 to 11.1 minutes per bench case live), and the four sample changes cost $0.53 to $0.97 each in lens calls, before A/B branches and the blast-radius lens. Depth-by-risk keeps the expensive part to the hunks that need it, and generated code costs a test run, not a read.
- **Receipts are only as strong as the checks.** A byte diff over 124 sample values can miss a case outside the set (it did once, during building). Equivalent mutants survive on code that changes no behaviour. Our lightweight mutator makes 5 to 9 mutants per file. Cross-checked against Stryker.NET on 2283, the fates agree on every mutant both tools made, and Stryker made more. We report these as limits rather than hide them.
- **Hunches are withheld from the main view.** A real bug the reviewer could not prove stays a hunch. That costs some recall and buys trust; the hunch list stays one click away.
- **Questions interrupt.** Capped at 3, only verdict-flipping ones, never repeated.

## Limits (what would make us not trust our own output)

- Low-recall lenses and areas in the track-record tables. The security lens caught 0 of 4 security plants on its own.
- Router misses: planted hunks a router scored low (reported per router).
- Blast radius matches call sites by name on syntax trees, not with a semantic model. Common names over-count (11 of the 14 outside callers on 2129 are `.Equals(` calls matched by name, most on other types); framework, reflection and cross-repo callers are missed.
- Caller coverage comes from one test project (`MessagePack.SourceGenerator.Tests`). A caller in a project it does not load, like the Unity one on 2297, shows as untested even if another suite covers it.
- The model matters where it writes tests. On Verda, Kimi's generated tests compiled but only 6 of 77 behaved as planned (Anthropic: 34 of 93), so it caught 3 fewer planted defects.
- Anything in "not checked": here, runtime test suites, NativeAOT and Unity, other target frameworks.
- A track record measured on planted defects is a proxy, measured once per case. In production, the measure is reverts and incidents per area, fed back into trust levels.

## How it scales

Depth follows risk, so cost grows with risky lines, not total lines. Generated code is verified as a block: 222 of 237 files in 2297 needed no reading. The ledger turns each answered question into context, so questions can fall over time (measured: 0.50 to 0.28 per change on 18 cases). The model sits behind one interface: the same pipeline ran on an EU-hosted open-weight model for EUR 0.47 per bench case, and the same weights can run on company hardware. Sandboxes are per change type: .NET and SQL (throwaway Postgres) are built; web UI (Playwright screenshots) and mobile (simulator flows) follow the same receipt shape.

## Who owns the decision

The named human in `decisions/<change>.json`: who, which choice, which evidence ids, and the merge commit. The AI never merges, and the dashboard refuses to merge a branch whose own receipt is blocked.
