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
- 11:31 Verda Inference API as a second model provider with a hard spend guard (PR #33); `.env.example` with key names only, then moved to the repo root (PRs #34, #35).
- 14:32 Verda brief in CHALLENGE.md; Verda listed as the one tech challenge genuinely used (PR #36).
- 14:52 Dashboard redesign: plain-language verdicts, receipt card with stamp, paper theme, dark mode (PR #37).
- 15:37 Plain-words summary, product context section and glossary on the change page (PR #38).
- 15:43 Each run records the product context it used; lowercased home paths scrubbed (PR #39).
- 18:00 Journey-first dashboard: queue, guided change page, practice runs moved to the track record (PR #40).
- 21:08 Tieto engineer's blast-radius feedback logged as decision 16 and lesson 15 (PR #41).
- 21:27 "Impact on other features" step on the change page (PR #42).
- 22:29 Slip, the Receipts mascot: header mark, favicon, queue, receipt stamp, decision, 404 (PR #43).
- 22:32 Each fact once per screen; product context renders as a document (PR #44).
- 22:33 Blast radius in the pipeline (PR #45): Roslyn member diff and call sites, floor 8/9, caller coverage, characterization lens. Four Tieto runs: 2283 3 outside callers / 0 untested, 2234 2 / 0, 2129 14 / 6 (11 `Equals` calls matched by name), 2297 12 / 1 (Unity `CompositeResolverGenerator.cs:44`); 19 pinning tests on 9 members, all preserved; verdicts unchanged; $1.40 in 4 live lens calls.
- 23:28 Final-pipeline measurement (PR #46): Anthropic batched cold 21/24, 0 escapes, 4/8 false-alarm cases, questions 0.50 to 0.28 on the same 18 cases; Verda (Kimi K3.1 Pro) cold 18/24, 0 escapes, same verdict on all four Tieto changes; 2297 round 2 on Verda = decision-A-B, side A needs sign-off, side B not built; `metrics/provider-comparison.json`.
- 23:40 Final accuracy pass on concept card, demo script, submission pack, logs, README, BLOCKED.md and this file against `metrics/` and the latest runs.
- 00:36 Renamed the product from "Receipts" to "PullReceipts" across README, submission pack, Verda doc, concept card, demo script, logs, metrics README and notes (written documents only; CLI, package and dashboard unchanged).

## In progress
Nothing. The pipeline and measurement are final; what remains is human-only (Next).

## Next (human steps only)
- BLOCKED.md section 2: set your name in the dashboard, confirm the context, decide on 2297 round 2 side A (sign off as a plain change or send back), optionally sign off 2283 and 2234, commit `decisions/` and `context/status.json` through a PR.
- Record the video (max 2:00) from `deliverables/demo-script.md`, upload it publicly.
- Make the repo public, fill the submission form from `docs/submission.md` (tick Verda Compute Track), set it to FINAL before Sun 10:00; post the `docs/verda.md` link in Verda's Discord channel.

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
- CHALLENGE.md (added by Anson) outranks SPEC.md on scope and judging. Added from it: MIT LICENSE, top-level README, docs/submission.md (pending numbers), self-check answered in PROGRESS.md with evidence, time to merge and escapes in measurement. No tech side challenge is genuinely used (no CRACI CI, no Modal, no Codex, no Entire), so none will be ticked. Superseded 14:32 (PR #36): Verda Compute Track is genuinely used and is the one ticked.
- `.env.example` blocked by the guard hook; logged in BLOCKED.md for Anson. Resolved by PRs #34 and #35 (key names only, repo root).
- ExecutionTests project does not compile on SDK 8.0.402 (needs Roslyn 4.12); declared under not_checked, no SDK install (spec makes it optional).
- Rule router gets a risk floor of 5 for source-generator analysis code so mutation runs with the rule router alone.
- Subagents do not commit (global rule); main session commits, rebases and opens PRs.
- 2297 is `blocked` (two executed tests show the attribute is dropped on netstandard2.0/.NET Framework) as well as ambiguous; truth over choreography. The demo's A/B merge moved to 2234, then a genuine decision-A-B; since the reader vote (PR #15) 2234 is needs-sign-off (0 of 3 readers), so no Tieto change has a mergeable A/B. The real merge and context confirmation are left to Anson (BLOCKED.md section 2): the AI never merges.
- Bench cases skip building A/B branches (--no-ab); interpretations still recorded.
- Docker daemon is not running; Docker is only needed for the SQL adapter (polish item 6), so no impact on the definition of done. (The SQL adapter later ran in Docker: `runs/sql-*`, PR #27.)

## CHALLENGE.md self-check (evidence, 23:40)
| Check | Status | Evidence |
|---|---|---|
| Concept first | met | `deliverables/concept-card.md`: recommendation, 9 principles (9 = blast radius), per-change table, measured section on the final pipeline, Verda vs Anthropic, trade-offs, limits, judges' 3 questions |
| All six expectations | met | concept and principles (card); sample change with REQ id: `runs/ju26-issue-2283/20261114T193310Z` (REQ-2283); found, refused, why: receipt fields `findings`, `hunches`, `refused`, `not_checked`; accept without a full read: dashboard `/change/ju26-issue-2283`; failure modes and decisions: `deliverables/lessons-log.md` (19 entries), `deliverables/decision-log.md` (20 entries) |
| Evidence traces to a requirement | met | every receipt carries `requirement_id`; findings carry `evidence_ids` tied to the claim; e.g. 2283 `gentest-1` fails on base (4/4), passes on head (4/4); `characterize-1`/`-2` pin old behaviour, pass on base and head; T6: 242 of 242 shown findings cite an executed receipt |
| Refusals shown | met | `runs/ju26-issue-2297/20261114T202036Z` and `runs/ju26-issue-2129/20261114T194759Z`, field `refused` (3 of 3 readers, quoted requirement); `not_checked` has 4 to 8 entries per change (2283: 4, 2234: 5, 2297: 7, 2129: 8), including untested callers |
| Named human signs off, no AI-merge path | met in code; human step pending | merge and sign-off routes refuse without a name and refuse blocked changes; `ab.py` only writes `review/*` branches; a local, uncommitted sign-off of 2234 by "anson" exists in the main checkout (`decisions/ju26-issue-2234.json`, 15:08); the rest is BLOCKED.md section 2 |
| Risk routes depth | met | 2297 routing: 222 regeneration, 14 deep, 1 light (T4); blast-radius floor 8 on all four Tieto changes (`impact.json`), so none can be auto-ready; T3 router comparison |
| Trust per area with data | met (bench) | `metrics/trust.json`: tests and type-collector at 3, seven other areas at 1; reset on escape implemented; 0 escapes |
| Measurement | met, final pipeline | `metrics/summary.json`: 21/24 caught cold, 0 escapes, 4/8 control/bait cases with a false alarm, 94 % of 2297 routed away (222/237), questions 0.50 to 0.28 per change on the same 18 cases (`cold_on_warm_cases`); review time 0.8 to 11.1 min per bench case live on Verda (`provider-comparison.json`); Verda next to Anthropic in `metrics/verda/`; provenance in `metrics/README.md` |
| Judges' three questions in writing | met | concept card sections "Limits", "How it scales", "Who owns the decision"; demo script |
| Data synthetic or cleared, no 8x | met | only the public Tieto sample repo, bench variants derived from it, and the synthetic SQL repo in `examples/` |
| MIT LICENSE, README that runs, no keys in git | met | `LICENSE`; every `receipts` subcommand in `README.md` checked with `--help` (setup, review, review-sql, bench generate/run, estimate-verda, metrics); `MOCK_LLM=1 uv run pytest -q`: 312 passed, 38 skipped (agent worktree, 23:39) |
| Submission pack | met (video pending) | `docs/submission.md`: name 29 chars, punchline 189, description 4944 (counted with a script) |
| Peer review in 3 minutes | ready | demo script: 2-minute flow plus walkthrough |
| Verda tech challenge | met (Discord post pending) | `docs/verda.md`: process, Fit, results vs Anthropic, cost, scale; EUR 25.00 of EUR 40 over 372 calls |

## Time elapsed
- 23:40: 18 h 08 m since the 05:32 start. Pipeline, measurement and written deliverables final; remaining steps are human-only (Next, BLOCKED.md section 2).
