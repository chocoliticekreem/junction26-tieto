# Submission pack

## Project name (max 30)
PullReceipts

## Punchline (max 200)
PullReceipts: AI code review where every finding has an executed receipt, every gap and untested caller is declared, trust is earned per area, and a named human signs off.

## Description (max 5000, Markdown)

**The problem.** Agent-written code now outpaces human review. An AI reviewer that only comments asks people to trust its confidence. "The AI said it was fine" is an unowned merge.

**Our recommendation: ask the AI reviewer for receipts, not opinions.** PullReceipts is a reference implementation of a PR-review factory built on five rules:

1. **No receipt, no finding.** A claim becomes a finding only when an executed artifact backs that specific claim: a test written from the requirement alone that fails on base and passes on the change, a surviving mutant, a byte diff, or a failed regeneration. Everything else is shown apart as an unverified hunch.
2. **Declare the gaps.** Every review lists what was not checked and why, generated from what did not run. Silence is never approval.
3. **Depth follows risk.** Rule floors protect public API, serializer, security and code generation. A cheap router (Jev) scores the rest, and six independent lenses see only what they need, never the implementing agent's reasoning. Generated code is verified as one block by regeneration, never read.
4. **Check what else it touches.** A Tieto engineer told us green tests are not enough: a change can do the new thing right and break an existing function other features call. So a hunk inside an existing member with callers outside the change gets risk floor 8 and is never auto-ready; new code used only by the change gets none. Tests written from the base code alone pin what those callers rely on: they must pass on base, and a failure on head is a verified high finding. Callers no test executes are declared.
5. **The AI advises; a named human decides.** When three independent readers of the requirement find two readings, the AI refuses to pick. It builds branch A and branch B, runs the receipts on both, and the human merges one. The dashboard records who decided, on which evidence, and refuses to merge a blocked branch.

**Shown on Tieto's four agent-written MessagePack-CSharp changes.** For REQ-2283, tests written from the issue text alone fail on the base commit and pass on the change: the fix is proven. Two mutants on the new lines survive the whole test suite (Stryker.NET agrees), so two branches of the fix are untested. For REQ-2297, 222 of 237 files are generated and verified by 89 snapshot tests, unread. Executed tests show the attribute is dropped on older targets. The "setting or no setting" question is refused and both readings are built, but neither can merge until the shared defect is fixed. Blast radius names one caller no test runs, in the Unity generator. After the implementing agent's fix, round 2 has no blocker: side A needs sign-off; the second reading could not be built. REQ-2129 is blocked by executed tests showing the external type's own attributes are ignored.

**Measured, not claimed.** A bench of 36 changes with planted defects and traps, labels hidden from the reviewer, on the final pipeline:

- 21 of 24 planted defects caught, 0 escapes (no planted defect marked auto-ready). 4 of 8 control and bait cases drew a false alarm, mostly mutants on code that changes no behaviour.
- 94 % of the largest change routed away from human reading.
- Questions per change fell from 0.50 to 0.28 on the same 18 cases once answers were in the ledger, none repeated.
- The security lens caught 0 of 4 on its own; tests from the logic-edge lens caught all 4.
- $0.55 to $0.68 per change without A/B branches; $0.28 per bench case through the batch API.

Trust per code area rises after 5 correct verdicts in a row and resets after an escape. Public API, serializer, security and code generation are never auto-ready.

**We attacked our own reviewer.** An adversarial pass found loose verification, API keys reaching untrusted builds, and ambiguity detection that flipped on wording. All fixed with regression tests.

**Code never leaves the company: measured on Verda.** The same pipeline runs on Verda's EU-hosted Inference API with the open-weight Kimi K3.1 Pro in every role. Same verdict on all four Tieto changes, 0 escapes, EUR 4.25 for the four changes against $8.06 on Anthropic. Its generated tests rarely behave as planned (6 of 77; Anthropic 34 of 93), so it caught 18 of 24 plants, not 21. A weaker model shows up as more "not checked", never as a false green.

**Built with:** Python 3.12, Claude Opus 5.5 and Sonnet 5.5 or Kimi K3.1 Pro on Verda, the Jev router, a .NET 8 sandbox (xUnit, mutation, byte diff, Roslyn call sites, coverlet), a throwaway-Postgres SQL sandbox, and a Next.js dashboard. Every model call is recorded, so the demo replays offline.

**Limits we state up front:** planted defects are a proxy for real escapes, measured once per case; equivalent mutants cause most false alarms; the security lens is weak; call sites are matched by name, not semantically, and caller coverage comes from one test project; runtime test suites, NativeAOT and Unity are declared as not checked.

## Links
- Repository: https://github.com/chocoliticekreem/junction26-tieto (made public at submission)
- Concept card: `deliverables/concept-card.md`
- Demo script (2 minutes): `deliverables/demo-script.md`
- Lessons log: `deliverables/lessons-log.md`
- Decision log: `deliverables/decision-log.md`
- Verda write-up: `docs/verda.md`
- Video: to be recorded (max 2 minutes)

## Tech challenges genuinely used
- **Verda Compute Track (56).** Verda's Inference API (open-weight Kimi K3.1 Pro, hosted in Germany) is a second model provider behind the pipeline's single model interface (`LLM_PROVIDER=verda`). We ran the four Tieto reviews, 2297 round 2 and the 36-case bench on it: same verdict on all four changes, 18 of 24 plants caught (Anthropic 21), 0 escapes, EUR 25.00 of a EUR 40 cap over 372 calls. Write-up: `docs/verda.md`. Post its link in Verda's Discord channel.

Not ticked, because not used: CRACI (no CI runs on it), Modal, Codex, Entire.
