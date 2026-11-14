# Bench re-run cost: batch mode, per-lens effort, cost guard

Status Sat 14 Nov 2026: built and tested offline. The Anthropic key is over its spend limit, so no
real call was made. Every number below comes from `bench run --dry-run`, which queues the requests
but sends nothing.

## Where the money goes

Measured on the PR #11 bench (`runs/mock-*`, real calls only):

| lens | model, effort | mean input tok | mean output tok |
|---|---|---|---|
| requirement-tests | Opus 5.5, high | 15,452 | 12,018 |
| logic-edge | Opus 5.5, high | 13,269 | 9,541 |
| test-quality | Sonnet 5.5, medium | 14,864 | 1,560 |
| product-context | Sonnet 5.5, medium | 16,763 | 903 |
| compatibility | Sonnet 5.5, medium | 10,524 | 952 |
| security | Sonnet 5.5, medium | 10,310 | 269 |

The two Opus lenses are about 85% of lens cost, and most of that is output tokens.

## What exists now

- **Per-lens effort.** One table in `pipeline/receipts/config.py`: `LENS_EFFORT_DEFAULT` holds the
  effort each lens had when the fixtures were recorded. `LENS_EFFORT` is that table plus the
  override from `RECEIPTS_LENS_EFFORT="requirement-tests=medium,logic-edge=medium"`. An unknown
  lens or effort level stops the run. `lenses.json` now records `effort` for each lens.
- **Fixture key.** Effort used to be left out of the key, so a run at a lower effort would have
  replayed answers recorded at high. Effort is now part of the key only when a lens runs at a
  level other than its historical default. Existing fixtures keep their keys. The four Tieto
  reviews still replay with `MOCK_LLM=1`: all 6 lenses and 3 readers come from fixtures, plus the
  orientation call for 2129 and 2297.
- **Batch mode.** `receipts bench run --batch`:
  1. Collect round. Every case runs `receipts review` with `RECEIPTS_LLM_MODE=collect`. This runs
     intake, routing and worktrees, then builds the lens and reader prompts. It runs no builds:
     no prompt depends on a build. A request with no fixture goes to `work/batch/queue.jsonl` and
     raises `PendingLLM` (a subclass of `LLMUnavailable`). The run directory is then deleted.
  2. The queue is deduplicated. For example, the readers of one requirement send the same prompt
     for all 9 variants, so that prompt is sent once.
  3. The estimate is printed.
  4. The requests go out as one Message Batch, with custom_id set to the fixture key.
  5. Each result is written as a fixture, exactly as a live call would write it.
  6. The loop repeats until a round queues nothing, with at most 4 batches.
  7. Final pass. Every case runs in full with `RECEIPTS_LLM_MODE=replay`, so no live Anthropic call
     can happen in this pass.

  A failed result (errored, expired, canceled, refused, non-JSON, or missing) goes to
  `work/batch/failed.jsonl`. It is printed and listed in the summary under `batch.rounds[].errored`,
  and in the receipt it shows up as "not checked". It is never re-sent silently and never invented.
  If the process dies while polling, `work/batch/inflight.json` lets the next run pick up the same
  batch instead of paying again. Jev stays a live call.
- **Cost guard.** `--max-usd N` (only with `--batch`) estimates each batch before it is sent and
  refuses if the estimate is above N. `--dry-run` runs collect round 1, prints the estimate at batch
  prices and at standard (live) prices, and submits nothing.

### Call graph: rounds

| round | calls | why this round |
|---|---|---|
| 1 | 6 lenses + 3 readers | depend only on intake, routes, the ledger and the style files |
| 1 or 2 | `readers-orient` | needs the reader vote. If the reader answers are already fixtures (true today for the empty cold ledger), it goes in round 1; otherwise round 2 |
| before the lenses | Haiku router (only when Jev is not the router) | lens hunk selection depends on the routes. The review stops after routing until the router answers |
| none | `ab-variant` | the bench passes `--no-ab` (see `review.py`); the A/B chain never runs |

If a reader is still queued, the orientation call is held back
(`readers.run_panel`, `llm.pending_count`): the vote and the chosen pair could still change.

**Cold-run change.** In a cold batch run, the oracle answers each case's questions after the
final pass, in manifest order. In the old cold run, a case that finished early wrote answers that
later cases saw in their prompts. With batching, all prompts are built up front against the empty
ledger, so every cold case now really sees the empty ledger. The warm run is unchanged.

## Estimates (dry run, offline, Sat 14 Nov)

The output tokens per request are the historical means above. All of those calls ran at `high`
for the Opus lenses, so **the medium-effort rows are an upper bound: none of the possible saving
from medium effort is in these numbers yet**. Input tokens are counted as prompt characters / 2.3.
The usual characters / 4 came out 1.71-1.80x below the input token counts the API reported on this
bench.

| run | requests | live (standard $) | batch $ |
|---|---|---|---|
| cold, 36 cases, today's efforts | 189 | 19.86 | **9.93** |
| cold, 36 cases, Opus lenses at medium | 197 | 22.05 | **11.02** (upper bound) |
| warm, `mock-2283` only, medium | 56 | 5.30 | 2.65 (upper bound) |
| warm, `mock-2234` only, medium | 56 | 5.05 | 2.52 (upper bound) |
| cold, `mock-2283` only, medium | 46 | 5.02 | 2.51 (upper bound) |
| cold, `mock-2283` only, high | 44 | 4.50 | 2.25 |

Why medium shows more requests than high: the 4 `clean` variants share their head commit with the
recorded Tieto reviews. At `high` their Opus calls replay from those fixtures. At `medium` they
need new calls.

Recipe total, upper bound: cold 36 at $11.02 + warm 2283 at $2.65 + warm 2234 at $2.52 =
**about $16.20 at batch prices**, plus a few cents for round-2 orientation calls. The old plan in
`BLOCKED.md` was full cold + full warm, live, at high: about $45. The warm subset figures were
estimated against the current oracle archive (`context/ledger.bench-oracle.jsonl`). After a new
cold run, the oracle writes new answers, so re-run the warm dry run before you submit.

Offline caveat: the dry run used `MOCK_LLM=1`, so Jev answered from fixtures. 34 of 36 cases were
fully covered. Two cases fell back to rule routes for some hunks (10 of 11 and 7 of 10 hunks), so
their lens selection may differ slightly in a live run.

## Cheapest meaningful re-run (exact commands)

Run these from `pipeline/` after the spend limit is raised. Run `--dry-run` first each time: it
is free.

```sh
export RECEIPTS_LENS_EFFORT="requirement-tests=medium,logic-edge=medium"

# 1. cold, all 36 cases, batched
uv run receipts bench run --dry-run
uv run receipts bench run --batch --max-usd 12

# 2. warm, on the two cleanest learning-curve issues only (the summary merges by case id)
uv run receipts bench run --warm --dry-run --only mock-2283
uv run receipts bench run --warm --batch --only mock-2283 --max-usd 3
uv run receipts bench run --warm --dry-run --only mock-2234
uv run receipts bench run --warm --batch --only mock-2234 --max-usd 3

uv run receipts metrics
```

The batch-mode summary holds `lens_effort`, every round's estimate, the batch id, the actual cost
and every failed request. Most batches finish within an hour; the maximum is 24 hours. The run
polls every 30 s.

## Quality trade: medium effort is untested on this bench

Nothing has been measured at `medium` for the Opus lenses. The risk: fewer generated tests and
shallower edge cases, so lower recall on the `logic`, `edge` and `missing-test` plants. Before you
use medium for the full bench, measure it on `mock-2283` at both levels (about $4.75 at batch prices
for both runs together):

```sh
# medium
RECEIPTS_LENS_EFFORT="requirement-tests=medium,logic-edge=medium" \
  uv run receipts bench run --batch --only mock-2283 --max-usd 3
cp ../runs/bench-cold-summary.json ../runs/bench-cold-summary.medium-2283.json

# high (today's default), same 9 cases
uv run receipts bench run --batch --only mock-2283 --max-usd 3
cp ../runs/bench-cold-summary.json ../runs/bench-cold-summary.high-2283.json
```

Compare per case: verdict, findings, and caught plant (T1). Also compare `output_tokens` for the
two Opus lenses in each run's `lenses.json`. The token ratio is the real medium saving: feed it
back into this note and replace the upper bounds. Both runs keep their fixtures, so whichever level
wins, the full cold run reuses the 9 `mock-2283` answers at that level.

Each cold run appends the oracle's answers to `context/ledger.jsonl`. Those answers are what the
next warm run sees. After the two measurement runs, delete the lines with
`"answered_by": "bench-oracle (simulated human)"` from that file before the full cold run.
Otherwise the warm `mock-2283` run sees three copies of each answer instead of one. Every cold run
starts from an empty ledger anyway, so the cold results are not affected.
