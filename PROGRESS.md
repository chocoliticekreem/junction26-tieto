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
- 09:10 Anthropic key hit its usage limit (BLOCKED.md item 1).
- 09:12 J: implementer round 2 for 2297 pushed to mock-repo (`ju26-issue-2297-r2`, 171/171 tests); re-review blocked by the API limit (PR #17).
- 09:18 Bench results merged (PR #19): cold 19/24, 0 escapes, questions 9 to 1, 2297 94 % routed away; provenance in metrics/README.md.
- 09:20 Concept card and demo script filled with real numbers; docs/submission.md written.
- 09:22 Bait plants no longer explain themselves in comments (PR #21).
- 09:24 Stryker.NET cross-check on 2283 (PR #22): our mutants agree where they overlap; full suite kills all 6 own-test survivors; Stryker found 2 full-suite survivors we lacked.
- 09:39 Track record page redesigned with headline tiles, per-area trust charts, T2/T4/T6/T7; receipt exportable as a PR comment (PR #23).
- 09:52 Mutation receipts two-tier (own tests, then full suite) plus logical-operator and linq-first operators; 2283 now matches Stryker (2 full-suite survivors); four reviews re-run offline from fixtures, verdicts unchanged (PR #24). Deliverables corrected (PR #25).
- 09:40 Anson asked how to cut API cost; answered (batch API for bench, lower effort on the two Opus lenses, subset re-runs, no Haiku router re-run). Waiting for his go before implementing.
- 10:54 SQL migration adapter in throwaway Postgres (PR #27): irreversible down and data-losing rename blocked with receipts, about 3 s each, no API.
- 11:05 Dashboard decluttered on Anson's request (PR #28): change pages from 2,300-6,900 visible words to 250-680; decision first, detail behind disclosures.
- 11:08 Cost cuts (PR #29): Message Batches for the bench, spend guard, dry-run estimate (cold re-run $9.93 batched vs $19.86 live), per-lens effort. BLOCKED.md now asks for about $20, not $60 (PR #30).
- 11:14 Positive proof above the fold on change pages (PR #31).

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
- Waiting on Anson: raise the API limit by about $20 and run the BLOCKED.md commands (final-pipeline bench, 2297 round 2); confirm the context; make the A/B merge by name; record the video.
- Polish backlog: items 1 (Stryker), 3 (trust chart), 5 (PR comment export) and 6 (SQL adapter) are done. Item 2 is covered by the harness's reflection-based API diff. Item 4 (LLM narrative) needs the API.

## Verda spend log (budget: €40 maximum)
| When | What | Real spend | Running total |
|---|---|---|---|
| 11:30 | Provider built; `GET /v1/models` (empty list) and two 20-token chat probes returned `403 subscription_required` (not billed) | €0.00 | €0.00 |
| 13:04-13:10 | Smoke: `ju26-issue-2283` on Kimi K3.1 Pro (all roles), scratch runs folder | €0.53 | €0.53 |
| 13:11-13:19 | `ju26-issue-2297-r2` with A/B (side B not built) | €0.99 | €1.52 |
| 13:19-14:00 | `ju26-issue-2283` (replayed, €0), `2234`, `2129` with A/B | €1.99 | €3.50 |
| 14:00-14:02 | `2297`, first attempt; process killed with the session, fixtures lost | €0.45 | €3.95 |
| 21:10-21:17 | `ju26-issue-2297` with A/B | €0.76 | €4.72 |
| 21:17-22:43 | Cold bench, 36 cases (2 lost lenses to Verda disconnects/timeout) | €16.27 | €20.98 |
| 22:43-23:02 | Warm `mock-2283`, 9 cases | €3.37 | €24.36 |
| 23:02-23:10 | Re-run of the 2 affected cold cases | €0.64 | €25.00 |

Final: **€25.00 of €40** (372 calls, `work/verda-spend.json`). Prices pessimistic (Kimi at $3/$15, 1 USD = 1 EUR).

## Anthropic spend log (budget: $20 for this pass, owner's raised limit)
Guard: `work/anthropic-spend.json`, `ANTHROPIC_MAX_USD=20` (reserves the worst case before every live call and batch; a batch that does not fit is split into chunks that do).

| When | What | Real spend | Running total |
|---|---|---|---|
| 13:03-13:14 | Cold bench, 36 cases, 3 Message Batches (189 requests) | $10.20 | $10.20 |
| 21:39-22:24 | `live lens-regression` calls from another track sharing the ledger (not this pass) | $1.40 | $11.60 |
| 22:08-22:17 | Warm `mock-2283`, 3 batches (57 requests) | $2.88 | $14.47 |
| 22:32-22:41 | Warm `mock-2234`, 3 batches (56 requests) | $2.02 | $16.50 |
| 22:50 | `receipts metrics` (Haiku router re-score for T3, 10 live calls) | $0.02 | $16.51 |

This pass: **$15.12**; ledger total $16.51 of $20.

## Decisions
- 11:16 Verda added to CHALLENGE.md by Anson: Verda Inference API (OpenAI-compatible, EU-hosted open-weight models) becomes a provider behind `llm.py`, selected by `LLM_PROVIDER=verda`; Anthropic stays the default. Verda runs use change ids suffixed `@verda` so both providers' runs sit side by side.
- CHALLENGE.md (added by Anson) outranks SPEC.md on scope and judging. Added from it: MIT LICENSE, top-level README, docs/submission.md (pending numbers), self-check answered in PROGRESS.md with evidence, time to merge and escapes in measurement. No tech side challenge is genuinely used (no CRACI CI, no Modal, no Codex, no Entire), so none will be ticked.
- `.env.example` blocked by the guard hook; logged in BLOCKED.md for Anson.
- ExecutionTests project does not compile on SDK 8.0.402 (needs Roslyn 4.12); declared under not_checked, no SDK install (spec makes it optional).
- Rule router gets a risk floor of 5 for source-generator analysis code so mutation runs with the rule router alone.
- Subagents do not commit (global rule); main session commits, rebases and opens PRs.
- 2297 is `blocked` (two executed tests show the attribute is dropped on netstandard2.0/.NET Framework) as well as ambiguous; truth over choreography. The demo's A/B merge moves to 2234, a genuine decision-A-B. The real merge and context confirmation are left to Anson (BLOCKED.md section 2): the AI never merges.
- Bench cases skip building A/B branches (--no-ab); interpretations still recorded.
- Docker daemon is not running; Docker is only needed for the SQL adapter (polish item 6), so no impact on the definition of done.

## CHALLENGE.md self-check (evidence, 09:20)
| Check | Status | Evidence |
|---|---|---|
| Concept first | met | `deliverables/concept-card.md`: recommendation, 8 principles, trade-offs, limits, judges' 3 questions |
| All six expectations | met | concept and principles (card); sample change with REQ id: latest run under `runs/ju26-issue-2283/` (REQ-2283); found, refused, why: receipt fields `findings`, `hunches`, `refused`, `not_checked`; accept without a full read: dashboard `/change/ju26-issue-2283` (screenshot `dashboard/e2e/screenshots/real-change-2283-1440.png`); failure modes and decisions: `deliverables/lessons-log.md` (13 entries), `deliverables/decision-log.md` (15 entries) |
| Evidence traces to a requirement | met | every receipt carries `requirement_id`; findings carry `evidence_ids` tied to the claim (PR #13: named failing test, byte diff traced to file); e.g. 2283 `gentest-1` fails on base, passes on head |
| Refusals shown | met | latest 2297 receipt, field `refused` (3 of 3 readers, quoted requirement); `not_checked` has 3 to 6 entries per change |
| Named human signs off, no AI-merge path | met in code; human step pending | merge and sign-off routes refuse without a name and refuse blocked changes (PR #13, e2e 21/21); `ab.py` only writes `review/*` branches; the real merge is left to Anson (BLOCKED.md section 2) |
| Risk routes depth | met | 2297 routing: 222 regeneration, 14 deep, 1 light; T3 and T4 in `metrics/` |
| Trust per area with data | met (bench) | `metrics/trust.json`, `metrics/trust-history.json` (tests and type-collector reached 3; reset on escape implemented; 0 escapes) |
| Measurement | met, provenance stated | `metrics/summary.json`: 19/24 caught, 0 escapes, 94 % of 2297 routed away, questions 0.25 to 0.04 per change, time to receipt 3.5 to 11 min; `metrics/README.md` |
| Judges' three questions in writing | met | concept card sections "Limits", "How it scales", "Who owns the decision"; demo script |
| Data synthetic or cleared, no 8x | met | only the public Tieto sample repo and bench variants derived from it |
| MIT LICENSE, README that runs, no keys in git | met | `LICENSE`, `README.md`; key-pattern grep over fixtures and runs clean (adversarial review) |
| Submission pack | met (video pending) | `docs/submission.md`: name 29 chars, punchline 197, description 4282 |
| Peer review in 3 minutes | ready | demo script: 2-minute flow plus walkthrough |

## Time elapsed
- 11:15: 5 h 43 m. Definition of done: met except the items that need the API limit raised or a named human. See BLOCKED.md.
