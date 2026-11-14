# Receipts: auditable AI code review (Tieto "Earned Trust in AI Code", id 351)

Build spec for an autonomous Claude Code agent starting from an empty folder. Junction 2026, final submission Sun 15 Nov 10:00 (Helsinki). Brief: `/Users/sonionring/orca/projects/junction/docs/junction-2026-challenges.md`, Tieto section.

---

## 0. Manual steps for Anson (do these first in the morning, in this order)

**Status (Sat 14 Nov, 05:45):** done: `ANTHROPIC_API_KEY` and `JEV_API_KEY` are in `.env.local`, so the Jev router is the primary path (keep the LLM router as the fallback and test both). Still to do: choose the named human in the dashboard, confirm the bootstrapped reviewer context file, record the video.

| # | What | Where | Time | Env var | Build blocked without it? |
|---|---|---|---|---|---|
| 1 | Anthropic API key | console.anthropic.com → API keys | 2 min | `ANTHROPIC_API_KEY` | No. The pipeline runs in `MOCK_LLM=1` mode on recorded fixtures until the key exists. Real review numbers need it |
| 2 | Jev API key | console.typesafe.ai (Jev is "early access", v0.01) | 5 min, may need approval | `JEV_API_KEY` | No. Router falls back to the LLM router, then the rule router. If no key by Sat 14:00, drop Jev from the live demo and present it as the pluggable cheap router with the LLM router's numbers |
| 3 | Put both keys in `.env.local` in the repo root | local file, never committed | 1 min | — | — |
| 4 | Check the dashboard once at `http://localhost:3000` and pick your name as the named human | browser | 2 min | — | — |
| 5 | Confirm the bootstrapped reviewer context file (`context/REVIEW_CONTEXT.md`) | dashboard → Context tab | 2 min | — | No, but the demo claims "a human confirmed it" |
| 6 | Record the 2-minute video and fill the submission form | Junction platform | 45 min | — | — |

Already done on this Mac: .NET SDK 8.0.402 installed at `~/.dotnet` (not on PATH; call `~/.dotnet/dotnet`); a clone of the sample repo exists at `/private/tmp/claude-501/-Users-sonionring-orca-projects-junction/aae424c1-cec2-4b34-862a-bf3153e7d7ee/scratchpad/tieto/repo` (the agent re-clones its own copy). Base commit `32258fdb` builds; `tests/MessagePack.SourceGenerator.Tests` passes 167/167 in about 6 s after a ~18 s build. Docker and `uv` are installed.

Optional model credit: OpenAI gives $100 per participant via the hackerpacks page; not needed if the Anthropic key exists.

---

## 1. One-line pitch

AI review you can audit: every finding has a receipt, every gap is declared, every area has a track record, and a named human signs off in one click.

## 2. Problem and gap

Tieto's question: "How can AI review the code made by AI so that humans can trust it?" Frontier models already find bugs well. The gap is trust, and it has four holes:

| Hole | Today | What breaks |
|---|---|---|
| Findings are opinions | An LLM comments "possible null dereference" | False alarms waste time; misses are invisible; humans stop reading |
| Nobody knows what was not checked | "LGTM" covers everything and nothing | The human cannot size the residual risk they accept |
| No track record | Is the AI good at compatibility in this codebase? Unknown | Trust is all or nothing, never earned per area |
| No owner | "The AI approved it" | Tieto explicitly rejects unowned merges |

A second, quieter gap: context-free review gives wrong advice. Example: a consumer app deliberately shows generic error messages (leaking internals is a security risk); a context-free reviewer flags "the real error is hidden, hard to debug" and suggests exposing internal errors. That is actively harmful advice, and it is why teams mute AI reviewers.

## 3. Principles (the concept card's core)

1. **No receipt, no finding.** A finding is shown as a claim only if an executed artifact backs it (a failing test, a build log, a byte diff, a mutant that survived). Unbacked suspicions are shown separately as "unverified hunches", never as findings.
2. **Declare the gaps.** Every change carries a "not checked, and why" list. Silence is never approval.
3. **Depth follows risk.** A cheap router scores each hunk; only risky hunks get expensive checks. Generated code is verified by regeneration, not by reading.
4. **The AI advises; a named human decides.** The AI never merges. It refuses to decide genuine ambiguities and makes the human's choice cheap (A/B branches, each with evidence).
5. **Ask, don't assume, on a budget.** When a verdict depends on intent the reviewer cannot see, it asks at most 2-3 questions per change, only when the answer flips the verdict, and writes the answer down so it never asks twice.
6. **Trust is earned per area and withdrawn after escapes.** Measured catch rates per lens and per code area set how much each area is fast-tracked.
7. **Independent reviewers.** Each lens sees the minimum it needs (requirement + diff + its own tools), never the implementing agent's reasoning.
8. **Code never leaves the company.** Self-hosted on the company's VPS; models can be swapped for on-prem ones. The only optional external call is the router, which is pluggable.

## 4. Architecture and data flow

```
requirement (REQ-xxxx) + change (base..head)
        |
   [1] Intake: diff -> hunks, classify files (source / test / docs / generated)
        |
   [2] Router (pluggable): JevRouter | LLMRouter | RuleRouter  -> risk 1-10 + category per hunk
        |
   [3] Sandbox (.NET adapter): build base, build head, run tests
        |
   [4] Lenses (parallel, minimal context each):
         requirement-tests | logic-edge | compatibility | security | product-context | test-quality
        |
   [5] Receipt checks: requirement tests fail-on-base / pass-on-head; mutation on changed lines;
       generated-file regeneration; compatibility byte diffs
        |
   [6] Intent questions (budget <= 3) -> context ledger
        |
   [7] Verdict composer: auto-ready | needs sign-off | decision A/B | blocked
        + trust receipt (checked / not checked / refused / residual risk)
        |
   [8] Dashboard: named human signs off, or picks A or B -> git merge into mock repo main
        |
   [9] Track record: per-lens, per-area metrics; trust levels update
```

All pipeline outputs are JSON files under `runs/<change-id>/<run-ts>/`. The dashboard only reads `runs/`, `context/` and `decisions/`, and writes `decisions/` and `context/` through API routes. No database.

## 5. Components

### 5.1 Intake
- Input: a local git repo path, `base` sha, `head` ref, requirement id.
- Split the diff into hunks with file, line range and kind. Classify files: `*.g.cs` and files under snapshot folders = generated; `tests/**` = test; `*.md`, `doc/**` = docs; `*.tt` = template (source of generated code); else source.
- `ju26-issue-2297` check: 237 files, 15 not generated. The intake report must show "222 generated files routed to regeneration check; 15 reviewed".

### 5.2 Router (swappable)
Interface: `route(hunk, requirement, context) -> {risk: 1..10, category: correctness|security|compatibility|tests|docs|generated, confidence: 0..1, router: name, cost_tokens, ms}`.

| Implementation | When | Notes |
|---|---|---|
| `RuleRouter` | Always runs first | Generated -> `generated`, risk 1 (verified by regeneration). Docs-only -> risk 1. Public API surface or serializer/formatter code -> floor risk 6 |
| `JevRouter` | `JEV_API_KEY` set | Jev is a decision model returning typed Score/Choice decisions with calibrated confidence. The agent must read the request schema from the MIT-licensed `jev-review` source (github.com/NiazMorshed2007/jev-review, `src/`), which calls the TypeSafe API (the pyshine source tour shows `https://api.typesafe.ai/v1/systemone`, 30 s timeout, up to two retries on 429/529/5xx). Input ceiling about 32,768 tokens per request: send per-hunk payloads, never the whole diff. Pricing per typesafe.ai: $42 per billion input tokens |
| `LLMRouter` | Fallback | Claude Haiku 4.5 (`claude-haiku-4-5-20251001`), JSON output, same schema |

Rules: the router never clears a hunk on its own if the RuleRouter floor applies; reported Jev recall is about 75%, so the measurement must report router misses explicitly; a hunk scored under the threshold still gets the cheap lenses (build + existing tests).

### 5.3 .NET sandbox adapter (built for real)
- Worktrees: `git worktree add` for base and head inside `work/` (gitignored).
- Build: `~/.dotnet/dotnet build <test csproj> -f net8.0` with `DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1`.
- Test: `dotnet test ... --filter <expr> --logger "trx;LogFileName=..."`; parse TRX to JSON.
- Test projects in scope: `tests/MessagePack.SourceGenerator.Tests` (fast, 167 tests); add `tests/MessagePack.SourceGenerator.ExecutionTests` only if it builds in under 3 minutes (measure once, record in `notes/sandbox.md`).
- Every command, its exit code, duration and log path become a receipt.

Other adapters are described in the concept card only, not built:

| Change type | Adapter | Receipt |
|---|---|---|
| SQL / migrations | Docker throwaway Postgres; apply migration up, run queries, apply down | Up/down logs, row counts before/after |
| Web UI | Playwright before/after screenshots and console errors | Image diff, error list |
| Mobile | Simulator + Maestro flow | Flow pass/fail, screenshots |

### 5.4 Lenses
Each lens is one LLM call (Claude Opus 5.5 `claude-opus-5-5` for requirement-tests and logic-edge; Claude Sonnet 5.5 `claude-sonnet-5-5` for the rest) with only: the requirement text, the relevant hunks, the context ledger entries for touched areas, and lens instructions. Each returns structured findings: `{id, lens, claim, location, severity, receipt_plan}`. A finding becomes "verified" only after step [5] attaches a receipt.

| Lens | Question | Receipt it can produce |
|---|---|---|
| requirement-tests | Does the change do what REQ-x asks? | New xUnit tests written from the requirement text only, run on base and head |
| logic-edge | What inputs break it? | Edge-case tests that fail on head |
| compatibility | Does old data still round-trip, does the public API stay compatible? | Serialize fixtures with base-built and head-built code, compare bytes; public API listing diff |
| security | Does it weaken MessagePackSecurity, untrusted-data handling, or generated code safety? | Test or code location + rule cited |
| product-context | Does it respect the conventions in the context ledger? | Ledger entry cited; or an intent question |
| test-quality | Do the change's own tests have teeth? | Mutants on changed lines that the change's tests fail to kill |

### 5.5 Receipt checks
- **Fail-before / pass-after:** requirement tests must compile and fail on base and pass on head for bug fixes. For features where the new API does not exist on base, "does not compile on base" is accepted but labelled "new-API evidence (weaker)".
- **Mutation (lightweight, built):** for each changed source hunk with risk >= 5, generate up to 5 mutants (swap `&&`/`||`, negate condition, remove null/empty guard, `FirstOrDefault`/`First` and `SingleOrDefault`/`Single`, off-by-one on bounds, swap boolean literal, delete a statement), rebuild, run the change's own test classes; re-run each survivor against the full SourceGenerator test project. Report killed / killed-by-suite / survived. Stryker.NET cross-check: `notes/stryker.md`.
- **Regeneration (generated files):** run the generator snapshot/verification tests on head; pass = all generated files verified as a block.
- **Compatibility bytes:** a small console harness (`tools/compat-harness/`) serializes a fixed set of sample types with base and head builds and diffs the bytes.

### 5.6 Context ledger and question budget
- `context/REVIEW_CONTEXT.md`: bootstrapped once from README.md, CONTRIBUTING.md, `doc/analyzers/*.md`, `src/MessagePack.SourceGenerator/AnalyzerReleases.*.md` and `git log --since=12.months` summaries. If the repo has CLAUDE.md or AGENTS.md, seed from them first. Sections: product, users, public contract, deliberate conventions, risk areas, decisions. Status `draft` until a named human confirms in the dashboard.
- Per review, retrieve only ledger entries tagged with the touched areas (no full-repo indexing per review).
- Question budget: at most 3 per change; asked only when the lens marks `verdict_depends_on_intent: true`. Each question offers 2-3 answers plus free text.
- Answers are appended to `context/ledger.jsonl` (`{question, answer, area, answered_by, ts, change_id}`) and summarised into REVIEW_CONTEXT.md. Same question in the same area is never asked twice.
- Seeded bait for the demo: a convention entry such as "analyzer diagnostics for types from referenced assemblies are deliberately not reported" or "generic error text in X is deliberate". A bait variant (section 6) checks that the reviewer asks or cites the ledger instead of flagging.

### 5.7 A/B decisions
- Triggered when the requirement-tests or product-context lens returns two plausible readings of the requirement that lead to different code (`ambiguity: true` with two interpretations).
- The agent produces branch `review/<change>/A` and `review/<change>/B` in the mock repo (B = minimal edit from the original change implementing reading B), and runs the full receipt pipeline on both.
- The dashboard shows A and B side by side with evidence; the named human clicks "Merge A" or "Merge B"; the API route runs `git merge --no-ff` into the mock repo's `main` and writes `decisions/<change>.json` (`who, choice, evidence_ids, ts, requirement_id`).
- Demo case: REQ-2297 "add ExcludeFromCodeCoverage to generated code" is ambiguous on scope (all generated types vs formatters only; setting or unconditional). The issue says "I do not feel like a setting is needed" — A = unconditional, B = opt-out setting.

### 5.8 Verdict composer and trust receipt
Per change, `receipt.json` and a rendered view:
- requirement id and text
- verdict: `auto-ready` (low risk, all receipts green, area trust high) | `needs-sign-off` | `decision-A-B` | `blocked` (verified finding)
- checked: list of `{what, by (tool/lens/router), receipt link, result}`
- verified findings and unverified hunches (separate)
- refused to decide, and why
- not checked, and why (e.g. "UnityClient not built in sandbox", "NativeAOT not exercised")
- residual risk the signer accepts (one paragraph, plain words)
- routing map: files -> depth

Risky areas (public API, serializer formats, security settings) are never `auto-ready`.

### 5.9 Track record
- `metrics/` aggregates per lens and per area: recall, precision, false-alarm rate, mutants killed, questions asked.
- Area trust level 0-3, starting at 1. +1 after N=5 consecutive correct verdicts in the area on the mock set; reset to 0 after any escape (a planted bug the system marked auto-ready). Level 3 areas may be auto-ready; level 0 forces human review.

## 6. Mock-clone generator (the test bench)

Goal: test the reviewer on many changes with known answers, using synthetic data derived from Tieto's sample.

- `mock-repo/`: a local bare clone of `https://github.com/te-industry-tech-office/junction26-messagepack-csharp` (Tieto asks teams to copy it first) with the 4 agent branches.
- For each source branch (`ju26-issue-2283`, `2234`, `2129`, `2297`), generate variant branches `mock/<issue>/<variant>`:

| Variant | Planted defect | Expected outcome |
|---|---|---|
| `clean` | none (original agent change) | whatever the honest review finds; used as baseline |
| `logic` | invert a condition in changed code | blocked by logic-edge or requirement-tests |
| `edge` | remove a null/empty/bounds guard | blocked by logic-edge |
| `compat` | change an int key, key order or default serialization behaviour | blocked by compatibility (byte diff) |
| `security` | relax a `MessagePackSecurity` setting or untrusted-data path | blocked by security |
| `missing-test` | delete the test that covers the change | flagged by test-quality (surviving mutants) |
| `drift` | implement an adjacent behaviour instead of the requirement (e.g. attribute only on formatters) | blocked by requirement-tests |
| `bait` | a deliberate convention recorded in the ledger (looks like a bug) | no finding, ledger cited or one question asked |
| `control` | harmless refactor (rename local, reorder usings) | auto-ready or needs-sign-off, no finding |

- Planting is deterministic: each plant is a scripted patch (`bench/plants/<issue>/<variant>.patch`) proposed by an LLM once, then committed as a fixed patch; the generator verifies the variant still compiles (except where non-compilation is the point, which none are).
- Labels live in `bench/manifest.json` (`{variant, branch, category, files, lines, description}`) and are never shown to the reviewer.
- Requirement IDs: `REQ-2283`, `REQ-2234`, `REQ-2129`, `REQ-2297`, text taken from the upstream issues, stored in `requirements/`.
- Target size: 4 issues x 9 variants = 36 cases. If `2129` (28 files) or `2297` builds are too slow, cap their variants at 5.

Upstream comparison (verified Sat 14 Nov): all four upstream issues are still open. Issue #2283 has an open, unmerged human-written PR #2305 ("Fix IndexOutOfRangeException on closed generic struct from referenced assembly", 5 files, +261/-3). Compare the agent's `2283` change against #2305 (approach, files touched, tests). For #2234, #2129 and #2297 no upstream fix exists; say so in the report.

## 7. Measurement plan (tables the pipeline must produce)

Run the full bench twice: cold (empty ledger answers) and warm (ledger filled by the cold run's answers).

| Table | Columns |
|---|---|
| T1 Per-lens detection | lens, planted cases in scope, caught, missed, false alarms, recall, precision |
| T2 Per-category | variant category, caught by which lens, verdict distribution |
| T3 Routing | router (Jev / LLM / rule-only / none=full review), hunks scored, tokens, cost USD, seconds, planted hunks routed low (misses), end-to-end recall |
| T4 Human load | per change: files total, files routed to human, generated files verified by regeneration, estimated reading minutes saved |
| T5 Learning curve | change sequence index, questions asked, false alarms on bait, cold vs warm |
| T6 Receipts | findings shown, findings with executed receipts, hunches withheld |
| T7 Upstream | issue, agent change vs upstream fix (if any), key differences |

Headline numbers for the pitch: recall and false-alarm rate on the 36-case bench; percentage of `2297` routed away from humans; questions per change falling from cold to warm; router cost vs full review at equal recall.

## 8. Dashboard UI

Next.js App Router + Tailwind + shadcn/ui. Quiet, legible, evidence-first. Pages:

| Route | Content |
|---|---|
| `/` Inbox | Changes as rows: requirement id, verdict pill, risk, receipts count green/red, questions pending, area trust |
| `/change/[id]` | Trust receipt: requirement, verdict, routing map (file tree coloured by depth), verified findings with receipt links (log excerpts, failing test names, byte diffs), hunches (collapsed), refused-to-decide, not-checked, residual risk, "Sign off as <name>" button |
| `/decision/[id]` | A vs B side by side: interpretation, diff summary, receipts for each; "Merge A" / "Merge B" (writes decision, runs merge, shows resulting commit sha) |
| `/questions` | Pending intent questions with answer buttons; answered history |
| `/context` | REVIEW_CONTEXT.md rendered, ledger entries, draft/confirmed status, "Confirm as <name>" |
| `/track-record` | T1, T3, T5 as tables and two small charts (recall by lens; questions per change over time); area trust levels |

Named human: a name picker stored in a cookie (no auth); every write records the name.

## 9. Agent build instructions

### Stack (fixed)
- `pipeline/`: Python 3.12 with `uv`, `anthropic` SDK, `httpx`, `pydantic`, `gitpython` or subprocess git. CLI entry `uv run receipts <command>`.
- `dashboard/`: Next.js 15 (App Router), TypeScript, Tailwind, shadcn/ui, reads/writes the JSON folders via route handlers.
- `tools/compat-harness/`: tiny .NET 8 console app.
- Storage: JSON/JSONL files only.

### Repo layout
```
SPEC.md
CLAUDE.md                 # Mode: hackathon + how to run
.env.example              # ANTHROPIC_API_KEY=, JEV_API_KEY=, MOCK_LLM=0, DOTNET=~/.dotnet/dotnet
.gitignore                # .env.local, work/, mock-repo/, runs/*/raw/, node_modules, .venv
pipeline/                 # intake, router, sandbox, lenses, receipts, verdict, bench, metrics
dashboard/
tools/compat-harness/
requirements/             # REQ-*.md
bench/plants/ bench/manifest.json
context/                  # REVIEW_CONTEXT.md, ledger.jsonl
runs/ decisions/ metrics/
deliverables/             # concept-card.md, lessons-log.md, decision-log.md, demo-script.md
notes/                    # sandbox timings, open issues
fixtures/llm/             # recorded LLM responses for MOCK_LLM=1
```

### Commands the agent must expose
- `uv run receipts setup` (clone mock repo, fetch branches, build base once)
- `uv run receipts review --change ju26-issue-2283 --req REQ-2283`
- `uv run receipts bench generate` / `uv run receipts bench run [--warm]`
- `uv run receipts metrics`
- `cd dashboard && npm run dev`

### Build order (mock first)
1. Skeleton, `.env.example`, CLAUDE.md, MOCK_LLM fixtures interface.
2. Intake + RuleRouter + .NET sandbox on `2283` (build, existing tests, receipts JSON).
3. Dashboard Inbox + Change page reading real `runs/` output.
4. Requirement-tests lens + fail-before/pass-after on `2283`.
5. Mutation check, compatibility harness, remaining lenses.
6. Context ledger bootstrap + question budget + Questions/Context pages.
7. A/B on `2297` + Decision page + merge.
8. Mock-clone generator + bench run + metrics + Track record page.
9. LLMRouter, then JevRouter when the key exists; T3 comparison.
10. Deliverables: concept card, lessons log, decision log, demo script.

### Test plan
- Python: `uv run pytest` for intake classification (2297 counts 222/15), router schema validation, receipt composition, ledger dedupe, bench manifest integrity.
- Dashboard: `npm run lint`, `npx tsc --noEmit`, Playwright e2e: inbox loads, change page shows receipts, answering a question updates the ledger, Merge A creates a commit and a decision record.
- Golden run: `receipts review` on `2283` must produce a receipt with at least one executed test receipt and a non-empty not-checked list.

### Definition of done
- [ ] All four sample changes reviewed end to end with receipts on disk.
- [ ] 2297 routing shows 222 generated files verified by regeneration, 15 reviewed.
- [ ] Bench of at least 24 variants generated and run cold and warm; T1-T7 produced in `metrics/`.
- [ ] One A/B decision merged from the dashboard with a decision record.
- [ ] Question budget demonstrated: at least one question asked cold, not asked warm.
- [ ] Dashboard Playwright suite green; pytest green; lint and typecheck clean.
- [ ] `deliverables/concept-card.md`, `lessons-log.md`, `decision-log.md`, `demo-script.md` written with real numbers.
- [ ] Works with `MOCK_LLM=1` and no keys (demo fallback).

### Polish backlog (continue iterating after done)
1. Stryker.NET mutation on one change for comparison with the lightweight mutator.
2. Public API diff via Microsoft.DotNet.ApiCompat.
3. Per-area trust chart over the bench sequence.
4. "Why this verdict" one-paragraph narrative per change (LLM, citing receipts only).
5. Export a change's receipt as a PR comment markdown.
6. Second sandbox adapter (SQL in Docker) on a tiny synthetic repo, to prove pluggability.

## 10. Demo choreography

### 2-minute video
| Time | Shot |
|---|---|
| 0:00-0:12 | Problem: agent tokens overtook human tokens; "the AI said it was fine" is not acceptable |
| 0:12-0:30 | Inbox: 4 Tieto changes plus the bench; verdict pills |
| 0:30-0:55 | `2283` receipt: requirement test failed on base, passed on head; one surviving mutant; not-checked list |
| 0:55-1:10 | `2297` routing map: 222 generated files verified by regeneration, 15 reviewed |
| 1:10-1:30 | A/B decision on 2297 scope; named human clicks Merge A; decision record appears |
| 1:30-1:45 | Question budget: bait case asks once cold, cites the ledger warm |
| 1:45-2:00 | Track record: recall/false alarms on 36 cases, cost of routing vs full review; close on the one-line pitch |

### Live walkthrough (judges)
Same flow, then answer the three judge questions with the concept card:
- What would make you not trust your own output? Low-recall areas in T1, router misses in T3, unverified hunches, anything in "not checked".
- Who owns the decision? The named human in the decision log; the AI never merges.
- How does it scale? Depth follows risk; generated code verified as a block; ledger reduces questions over time; sandboxes per change type.

## 11. Stress test

| Attack | Fix in this spec |
|---|---|
| "Your tests are written by the same kind of model, so they pass by construction" | Tests come from the requirement only, must fail on base, and their teeth are measured by mutants |
| "Planted bugs are easy; real bugs are subtle" | Bench includes drift and bait cases; report clean-branch findings separately; compare to upstream PR #2305 |
| "Jev misses 1 in 4" | Never the sole gate; rule floors; T3 reports router misses; Jev is swappable |
| "No Jev key" | LLM router fallback, then rule-only; numbers still produced |
| "The context file gets the product wrong" | Draft until a human confirms; questions correct it; entries carry provenance |
| "Questions annoy developers" | Hard budget of 3, only verdict-flipping, never repeated; T5 shows decline |
| "A/B doubles the work" | Only on flagged ambiguity; B is a minimal edit; one click |
| ".NET builds are slow" | Worktrees, incremental builds, filtered tests, mutant cap 5, bench cap if needed; timings recorded |
| "Sending code to an API breaks your self-hosted pitch" | Router and lens models are pluggable; concept card lists on-prem model options; Jev is optional |
| "Wifi dies in the demo" | `MOCK_LLM=1` replays recorded runs; dashboard reads local files |
| "Is this just CodeRabbit/Greptile?" | Those post comments. This produces executed receipts, declared gaps, per-area trust, and a signed decision log |
| "Line-by-line review is the wrong model" (Tieto invites this) | Agreed: review by requirement and risk, verify generated code as a block, read only routed hunks |

## 12. Build plan (~28 h, 1-2 people + agents)

| Hours (from start) | Work |
|---|---|
| 0-2 | Skeleton, setup command, base build cached, intake on all 4 branches |
| 2-5 | Sandbox receipts on 2283; dashboard inbox + change page |
| 5-9 | Requirement-tests lens, fail-before/pass-after, mutation, compat harness |
| 9-12 | Remaining lenses, verdict composer, not-checked list |
| 12-15 | Context ledger, question budget, Questions/Context pages |
| 15-18 | A/B on 2297 + merge flow |
| 18-22 | Mock-clone generator, bench cold + warm, metrics, track record page |
| 22-24 | Routers comparison (LLM, Jev if key) |
| 24-26 | Deliverables with real numbers, demo script, Playwright pass |
| 26-28 | Record video, submit (target Sun 09:30) |

## 13. Done when

The definition-of-done checklist in section 9 is all ticked, the video is recorded, and the submission includes the repo, the concept card, the lessons log, the decision log and the demo video.
