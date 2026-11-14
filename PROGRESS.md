# Progress log

**Start time:** 2026-11-14 05:32 EET (Helsinki). Minimum run until 08:32 EET, then until the definition of done is verified, then polish backlog.

## Done
- 05:33 Repo created: github.com/chocoliticekreem/junction26-tieto (private), initial commit on `main`.

- 05:36 Skeleton merged (PR #1): `receipts` CLI, data contract `models.py`, record/replay LLM layer `llm.py`, run layout `runstore.py`.

- 05:46 Track E merged (PR #2): real REQ files, REVIEW_CONTEXT.md draft (24 entries), seeded convention (TypeCollector IsInMetadata skip), upstream T7.
- 05:48 Track D1 merged (PR #3): compat harness, 124 cases; planted changes caught; 2129 = 0 byte diffs, API +3.
- 05:51 Track A merged (PR #4): golden run on 2283 = head 170/170, base 167/167, needs-sign-off, 11 not-checked; 2297 intake 237/222/15; review 17-20 s.
- 06:07 Track D2 merged (PR #5): 2283 requirement test fails on base 2/3, passes on head 3/3; mutation 2 killed / 4 survived; 2297 regeneration 222/222 via 89 snapshot tests; 2129 compat 0/124 diffs, API +3.
- 06:08 Track B merged (PR #6): dashboard, 6 pages, real git merge, Playwright 18/18, screenshots at 1440/390 reviewed.
- 06:09 Track C merged (PR #7): Jev router works (api.typesafe.ai, ~200 ms/hunk, $0.001/change), Haiku fallback, six lenses (~$0.65/change), generated requirement tests compile and discriminate on 2283 and 2297; 2297 flags ambiguity A/B.
- 06:12 Bench merged (PR #8): 36 plants (4 issues x 9 variants), all compile, compat/security plants probed.
- 06:32 F2 phase 1 merged (PR #9): bench run, oracle (simulated human, labelled), metrics T1-T6, trust levels; smoke on 2283 subset caught 4/6 planted before lenses.
- 07:09 Track G merged (PR #10): lenses wired, A/B branches, real reviews: 2283 needs-sign-off (req tests fail base 4/4, pass head 4/4; 4 surviving mutants), 2234 decision-A-B, 2129 blocked (executed tests), 2297 blocked + A/B refused (guard drops attribute on netstandard2.0/.NET Framework; 222 generated verified by 89 snapshot tests). MOCK_LLM=1 replay identical on all 10 runs.
- 07:10 Fix merged (PR #11): a change's own new test failing on head blocks; change ids kept out of lens prompts.
- 07:11 Verified merge flow on real 2234 data against a scratch copy of the mock repo: no-name 400, blocked 2297 refused, 2234 A merged --no-ff (2 parents, author = named human) + decision record.
- 07:12 Drafts on branch `deliverables`: decision log, lessons log, concept card, demo script (bench numbers pending).
- 07:13 Adversarial reviewer (spec + code only) found 11 defects, 7 confirmed: findings verified by unrelated evidence, keys reaching dotnet, MOCK_LLM overridable by env file, re-asked questions, sign-off on blocked, raw diff in prompts, codegen not risky, inflated bench recall.
- 08:08 Track H merged (PR #13): all pipeline/dashboard defects fixed with regression tests; four reviews re-recorded ($6.88); pytest 234 both modes; e2e 21/21.
- 08:51 PR #14: MIT LICENSE, README, CHALLENGE.md (outranks SPEC on scope), lessons 11-13, decisions 13-15.
- 09:03 Track I merged (PR #15): ambiguity = vote of 3 independent requirement readers (see only the requirement). 2297 3/3 and 2129 3/3 ambiguous, 2283 1/3, 2234 0/3. MOCK replay identical on all 8 runs.

## In progress (started ~05:38, parallel subagents in worktrees)
| Track | Model | Scope |
|---|---|---|
| A core | opus | setup (bare mock repo), intake, RuleRouter, .NET sandbox, verdict, review orchestration, golden run on 2283 |
| B dashboard | fable | Next.js: inbox, change, decision (real git merge), questions, context, track record; Playwright |
| C routers + lenses | opus | LLMRouter, JevRouter (from jev-review source), six lenses, question budget |
| D1 compat | opus | tools/compat-harness byte diff + public API listing |
| E content | sonnet | requirements/REQ-*.md from upstream issues, context bootstrap + seeded bait, upstream comparison T7 |
| F1 bench | opus | 36 deterministic plants, manifest, `bench generate` |

## Next
- J running since 09:04: an agent playing the implementer pushes round-2 fix for 2297 (verified findings only); reviewer re-reviews; expect a mergeable A/B for the human.
- F2 still on its first full bench run (pre-fix pipeline); after it ends: metrics fixes (blocked-verdict inflation, build/tests credit text, bait leakage), then re-run bench cold + warm on the final pipeline.
- Then: numbers into concept card, docs/submission.md, self-check with evidence, final Playwright pass.
- F2 (after D2): bench run cold + warm, metrics T1-T7, trust levels.
- A/B on 2297, deliverables, adversarial review.

## Decisions
- CHALLENGE.md (added by Anson) outranks SPEC.md on scope and judging. Added from it: MIT LICENSE, top-level README, docs/submission.md (pending numbers), self-check answered in PROGRESS.md with evidence, time to merge and escapes in measurement. No tech side challenge is genuinely used (no CRACI CI, no Modal, no Codex, no Entire), so none will be ticked.
- `.env.example` blocked by the guard hook; logged in BLOCKED.md for Anson.
- ExecutionTests project does not compile on SDK 8.0.402 (needs Roslyn 4.12); declared under not_checked, no SDK install (spec makes it optional).
- Rule router gets a risk floor of 5 for source-generator analysis code so mutation runs with the rule router alone.
- Subagents do not commit (global rule); main session commits, rebases and opens PRs.
- 2297 is `blocked` (two executed tests show the attribute is dropped on netstandard2.0/.NET Framework) as well as ambiguous; truth over choreography. The demo's A/B merge moves to 2234, a genuine decision-A-B. The real merge and context confirmation are left to Anson (BLOCKED.md section 2): the AI never merges.
- Bench cases skip building A/B branches (--no-ab); interpretations still recorded.
- Docker daemon is not running; Docker is only needed for the SQL adapter (polish item 6), so no impact on the definition of done.

## Time elapsed
- 09:05 — 3 h 33 m. Minimum 3 h reached at 08:32; definition of done not yet met (bench cold/warm + metrics, A/B merge by Anson, question cold/warm), so work continues.