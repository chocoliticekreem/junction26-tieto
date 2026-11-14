# Submission pack

## Project name (max 30)
Receipts: auditable AI review

## Punchline (max 200)
AI code review where every finding has an executed receipt, every gap is declared, trust is earned per area on a measured bench, and a named human signs off on exactly what was and was not checked.

## Description (max 5000, Markdown)

**The problem.** Agent-written code now outpaces human review. An AI reviewer that only comments asks people to trust its confidence, and Tieto rightly rejects that: "the AI said it was fine" is an unowned merge.

**Our recommendation: ask the AI reviewer for receipts, not opinions.** Receipts is a reference implementation of a PR-review factory built on four rules:

1. **No receipt, no finding.** A claim becomes a finding only when an executed artifact backs that specific claim. The artifact can be a test written from the requirement alone that fails on the base commit and passes on the change, a mutant the change's own tests fail to kill, a byte diff of serialized data, or a failed regeneration of generated code. Everything else is shown apart as an unverified hunch.
2. **Declare the gaps.** Every review lists what was not checked and why, generated from what did not run. Silence is never approval.
3. **Depth follows risk.** Rule floors protect public API, serializer, security and code-generation areas. A cheap router (Jev, about $0.001 per change) scores the rest, and six independent lenses (requirement tests, logic edge, compatibility, security, product context, test quality) see only what they need, never the implementing agent's reasoning. Generated code is verified as one block by regeneration, never read.
4. **The AI advises; a named human decides.** When three independent readers of the requirement find two readings, the AI refuses to pick. It builds branch A and branch B, runs the receipts on both, and the human merges one. The dashboard records who decided, on which evidence, and refuses to merge a blocked branch or sign off a blocked change.

**Shown on Tieto's four agent-written MessagePack-CSharp changes.** For REQ-2283, the reviewer wrote tests from the issue text alone: they fail on the base commit and pass on the change, so the fix is proven. Two mutants on the new lines still survive the whole test suite (Stryker.NET agrees), so two branches of the fix are untested. For REQ-2297, 222 of 237 files are generated and verified by 89 snapshot tests without a human reading them. Executed tests show the change drops the attribute on netstandard2.0 and .NET Framework, where it exists. The requirement's "setting or no setting" question is refused and both readings are built, but neither can merge until the shared defect is fixed. REQ-2129 is blocked by executed tests showing that the external type's own attributes are ignored.

**Measured, not claimed.** A bench of 36 changes with planted defects (logic, edge cases, compatibility, security, missing tests, drift from the requirement, convention bait and harmless controls), labels hidden from the reviewer:

- 19 of 24 planted defects caught, 0 escapes (no planted defect marked auto-ready).
- 94 % of the largest change routed away from human reading.
- Questions per change fell from 0.25 to 0.04 once answers were in the ledger, with none repeated.
- Per-lens recall shows where not to trust it: the security lens caught 0 of 4 on its own (tests from other lenses caught 3).

Trust levels per code area rise after 5 correct verdicts in a row and reset after an escape. Only top-level areas can ever be auto-ready, and public API, serializer format, security and code generation never are.

**We attacked our own reviewer.** An independent reviewer given only the spec and the code found that our first verification was too loose (a claim could be "verified" by an unrelated failing check), that API keys reached the builds of untrusted code, and that ambiguity detection flipped on prompt wording. All three are fixed with regression tests, and the decision and lessons logs record them.

**Built with:** Python 3.12 pipeline (uv, pydantic, Anthropic SDK: Claude Opus 5.5 and Sonnet 5.5 lenses, Haiku 4.5 fallback router), Jev decision API router, a real .NET 8 sandbox (git worktrees, xUnit, mutation, a byte-diff harness), and a Next.js and shadcn dashboard. Every model call is recorded, so the whole demo replays offline.

**Limits we state up front:** planted defects are a proxy for real escapes; equivalent mutants cause most false alarms; the bench numbers predate our last two hardening fixes and a re-run is queued; runtime test suites, NativeAOT and Unity targets are declared as not checked.

## Links
- Repository: https://github.com/chocoliticekreem/junction26-tieto (made public at submission)
- Concept card: `deliverables/concept-card.md`
- Demo script (2 minutes): `deliverables/demo-script.md`
- Lessons log: `deliverables/lessons-log.md`
- Decision log: `deliverables/decision-log.md`
- Video: to be recorded (max 2 minutes)

## Tech challenges genuinely used
None. We do not run CI on CRACI, receipts do not run in Modal sandboxes, Codex was not used to build it, and Entire was not used. Ticking any of them would be untrue.
