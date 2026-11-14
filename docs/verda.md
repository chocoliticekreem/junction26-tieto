# PullReceipts on Verda: open-weight, EU-hosted review models

Status Sat 14 Nov 2026, 23:15 EET: **measured.** Every review below ran on Verda Inference with
`kimi-k3-1-pro` in every role (DeepSeek V4.1 Flash is closed to new projects, see "Models per
role"). Real Verda spend, from the guard's ledger `work/verda-spend.json`: **EUR 25.00 of the EUR 40
cap** (372 calls). Results, costs and failure modes are in "Results vs Anthropic".

## Why Verda (Fit)

PullReceipts reviews agent-written code before it merges. Principle 8 says the code under review
never leaves the company. A US-hosted API weakens that promise. Verda fits it better:

- **Verda Inference** serves open-weight models per token through an OpenAI-compatible API,
  hosted on Verda GPUs in Germany ([models][models], [FAQ][faq]).
- No training on inputs or outputs. Prompts are not stored: the in-memory cache is cleared within
  2 hours. No subprocessors, and nothing is sent to the model developer ([FAQ][faq]). SOC 2 Type
  II, ISO 27001/27017/27018/27701, C5, GDPR ([FAQ][faq]).
- **The same weights can be self-hosted.** Kimi K3.1 Pro and DeepSeek V4.1 Flash 1017 publish
  their weights on Hugging Face ([Kimi][kimi], [DeepSeek][deepseek]). A company that wants the
  code on its own hardware runs the same model on Verda GPU instances or serverless containers
  (vLLM tutorials in the [docs index][llms]). PullReceipts only needs the base URL to change.

We picked the Inference API over GPU instances for the hackathon: pay per token, no capacity to
manage, and a bench of 36 reviews needs bursts, not a GPU running all night.

## What the docs say (researched Sat 14 Nov)

| Item | Value | Source |
|---|---|---|
| Base URL | `https://inference.verda.com/v1` | [OpenAI compatibility][compat] |
| Auth | `Authorization: Bearer <inference API key>` (env name in our config: `VERDA_API_KEY`; `VERDA_INFERENCE_API_KEY` also accepted, the docs' name) | [Quickstart][quick], [FAQ][faq] |
| API | OpenAI Chat Completions (`POST /v1/chat/completions`), Responses, `GET /v1/models` | [Models][models] |
| Models | `kimi-k3-1-pro` (Moonshot AI), `deepseek-4-1-flash-1017` (DeepSeek). GLM 5.3 "coming soon" | [Models][models], [verda.com/inference][site] |
| Structured output | "JSON mode" listed for both models; `response_format` accepted. JSON-schema mode is not documented | [Kimi][kimi], [DeepSeek][deepseek], [compat][compat] |
| Context | 1,048,576 tokens, both models | [Kimi][kimi], [DeepSeek][deepseek] |
| Output limit | Not published; `max_tokens` caps reasoning + answer together | [compat][compat] |
| Reasoning | `reasoning_effort`: `none`, `low`, `high`, `max`. Kimi default `max`, DeepSeek default `none`. Reasoning bills as output | [Kimi][kimi], [DeepSeek][deepseek] |
| Timeouts | 60 s without streaming, 20 min streamed | [compat][compat] |
| Rate limits | Per model and project, RPM and TPM by account tier; `max_tokens` is reserved against TPM; 429 with `Retry-After` | [Rate limits][limits] |
| Batch API | None ("Not at the moment") | [FAQ][faq] |
| Price, Kimi K3.1 Pro | per 1M tokens: $3.00 in / $15.00 out / $0.30 cached on the model card; $2.00 / $10.00 / $0.20 on the pricing grid of the same page | [verda.com/inference][site] |
| Price, DeepSeek V4.1 Flash 1017 | per 1M tokens: $0.15 in / $0.60 out / $0.006 cached | [verda.com/inference][site] |

**Currency.** Verda lists USD. Junction credits are in EUR. We convert at **1 USD = 1 EUR**
(`VERDA_EUR_PER_USD=1.0`). This is deliberately pessimistic: at the real rate the euro figures
would be lower, so the spend guard can over-count but never under-count. For the same reason we
price Kimi at the higher of the two published prices and bill cached input at the full input price.

**Live check.** `GET /v1/models` with our key: HTTP 200, `{"object":"list","data":[]}`. A 20-token
chat call to each model: HTTP 403 `subscription_required`. No rate-limit headers came back, so the
tier limits are still unknown.

## Models per role, and why

| Role | Anthropic (recorded) | Verda | Why |
|---|---|---|---|
| Deep lenses: requirement-tests, logic-edge (write C# xUnit tests that must compile), A/B builder | Claude Opus 5.5, effort high | `kimi-k3-1-pro`, reasoning `high` | Verda's flagship for "long-horizon coding". DeepSWE 75.7 at max effort, verified by Moonshot's Vendor Verifier on Verda's deployment, plus 100 on its tool-call/JSON-schema accuracy check ([Kimi][kimi]). It costs 25% less per token than Opus ($3/$15 vs $4/$20). |
| Other lenses (compatibility, security, product-context, test-quality), readers, orientation | Claude Sonnet 5.5, effort medium | `deepseek-4-1-flash-1017`, reasoning `low` | DeepSWE v1.1 77.0, NL2Repo 68.2 ([DeepSeek][deepseek]). 13x cheaper input and 17x cheaper output than Sonnet. These lenses mostly read and classify; their output is short (269-1,560 tokens on average, `notes/cost.md`). |
| Fallback router (LLMRouter, used only when Jev is down) | Claude Haiku 4.5 | `deepseek-4-1-flash-1017`, reasoning `none` | One integer and one category per hunk. The cheapest model, with reasoning off. |

Override any of them with `VERDA_MODEL_DEEP`, `VERDA_MODEL_LENS` or `VERDA_MODEL_ROUTER`.

**What actually ran.** DeepSeek V4.1 Flash is closed to new projects on Verda, so every run here
used `VERDA_MODEL_DEEP=VERDA_MODEL_LENS=VERDA_MODEL_ROUTER=kimi-k3-1-pro`: reasoning `high` for the
two deep lenses and the A/B builder, `low` for the other lenses, readers and orientation (the
router never ran: Jev routed every hunk). Kimi reasons before it answers and reasoning bills as
output, so `max_tokens` is doubled whenever reasoning is on (a 20-token probe came back with empty
content). Kimi in the cheap roles costs 1.5x Sonnet per token instead of 13-17x less, so the costs
below are an upper bound on the two-model plan.

## How it is built (process)

All changes go through the existing single model door, `pipeline/receipts/llm.py`. No lens,
prompt or verdict rule changes per provider.

- **Selection.** `LLM_PROVIDER=anthropic|verda` (default `anthropic`). With `verda`,
  `config.MODEL_DEEP/LENS/ROUTER` resolve to the Verda models. `llm.call_json` sends a fixture miss
  to `receipts/verda.py` instead of the Anthropic SDK. Never both: a Verda run cannot reach
  Anthropic.
- **Fixtures.** The record/replay key is `sha256(model, system, user, schema[, effort])`. For a
  non-Anthropic provider, `{"provider": "verda"}` is added, and the model id is the actual Verda
  id. So a Verda answer never replays as an Anthropic one or the other way round, and every existing
  Anthropic fixture keeps its key (test `test_anthropic_key_unchanged_and_verda_key_distinct`).
  `MOCK_LLM=1` replays Verda fixtures exactly like Anthropic ones.
- **Structured output.** Verda documents JSON mode, not JSON-schema mode. Each call sends
  `response_format: {"type": "json_object"}` and adds the JSON Schema to the system prompt. The
  reply is validated against that schema in `verda.schema_errors` (types, required keys,
  `additionalProperties: false`, enums, arrays). On a mismatch, **one** repair turn sends the
  validation errors back. If it is still invalid, or the reply was cut off at `max_tokens`, the
  call raises `LLMUnavailable`: the lens shows as "not checked", and nothing is invented.
- **Effort.** Anthropic `effort` maps to `reasoning_effort`: low and medium become `low`, high
  becomes `high`, xhigh and max become `max`. Router calls run with `none`. Reasoning counts
  against `max_tokens`, so `max_tokens` is doubled whenever reasoning is on.
- **Streaming.** Every call streams with `stream_options.include_usage`, because lens replies
  (whole C# test files) exceed the 60 s non-streamed timeout. A 429 or 5xx is retried up to 3
  times, after `Retry-After` or with exponential backoff.
- **Spend guard.** `work/verda-spend.json` is a ledger shared by every process, behind a file
  lock. Before each HTTP request, the call reserves its worst case (prompt chars / 2.3 tokens in,
  plus `max_tokens` out, at list price). A call that would take spend + live reservations + its
  worst case past `VERDA_MAX_EUR` (default 40) is refused with `LLMUnavailable("budget: ...")`.
  The check runs before anything is sent. Afterwards the reservation is replaced by the reported
  usage. A dead process's reservations are released. Every call is logged with tokens, USD, EUR and
  status.
- **Cost fields.** `lenses.json`, `readers.json`, `routing.json` and `ab.json` now carry `provider`
  and `cost_eur` next to `cost_usd`. Bench summaries add `lens_eur` and `router_eur`.
- **Separate run ids.** Verda runs go to `runs/<change>@verda/`, and A/B sides to
  `runs/<change>@verda__A/`. Bench summaries go to `runs/bench-cold-summary@verda.json`, and the
  warm-run oracle archive to `context/ledger.bench-oracle@verda.jsonl`. The Anthropic runs are never
  overwritten. The dashboard's `isSafeId` accepts `@`, and its `__A`/`__B` and `ju26-issue-`
  prefix checks still match, so both providers' runs show side by side (checked in
  `dashboard/lib/paths.ts` and `lib/data.ts`; not yet rendered with a real Verda run).
- **Batch.** Verda has no batch API, so `bench run --batch` prints that and runs live, still
  capped by the guard. `--dry-run` (which needs the batch collect path) refuses.
- **Estimate.** `uv run receipts estimate-verda` projects the re-run plan from the token counts the
  same reviews recorded on Anthropic. Once `@verda` runs exist, it scales output tokens by the
  measured Verda/Anthropic output ratio per role.
- **Metrics.** `uv run receipts metrics --provider verda` writes T1, T2, T4, T5, T6 and
  `summary.json` into `metrics/verda/`. It also writes `metrics/provider-comparison.json` in the
  dashboard table shape, with one row per change plus totals for verdict, verified findings,
  hunches, generated tests compiled and discriminating, plants caught, false alarms, questions,
  USD, EUR, lens seconds and wall seconds for both providers. `metrics/` itself is not touched.
- **Prompt changes for open models:** one, format only. The JSON Schema is added to the system
  prompt ("reply with exactly one JSON object ... must validate against this JSON Schema"), and the
  repair turn quotes the validation errors. No lens instruction, finding rule or verdict rule
  changed. Any later change for compile traps will be listed here.

Tests: `pipeline/tests/test_verda.py` (16 tests, fake HTTP transport). They cover key
composition, provider selection, an Anthropic fixture that must not replay under Verda, JSON
repair and its failure, cut-off replies, API errors, budget refusal before any request, release of
dead reservations, run ids, the batch fallback, the projection and the comparison table.
`uv run pytest -q` passes 318 and skips 5, with and without `MOCK_LLM=1`.

A review with the real key and no model enabled ran end to end
(`LLM_PROVIDER=verda receipts review --change ju26-issue-2283`, into a scratch runs folder):
- The run landed in `ju26-issue-2283@verda/`.
- Builds and the existing tests ran (head 170/170, base 167/167).
- All 6 lenses were declared unavailable with Verda's 403 text.
- The verdict was `needs-sign-off` ("no AI lens ran").
- No home path appears in the run files.
- The ledger recorded EUR 0.00.

## Results vs Anthropic

Sources: `metrics/provider-comparison.json` (`uv run receipts metrics --provider verda`),
`metrics/verda/summary.json`, `metrics/summary.json` and the two spend ledgers. Both providers ran
the same final pipeline on the same branches with the same empty context ledger, so every row
compares models only. "Verified" = a finding backed by an executed receipt. "Gentests" = generated
requirement and edge tests: compiled = built and ran; discriminating = behaved as its plan says.

### The four Tieto changes, plus 2297 round 2

| Change | Anthropic verdict | Verda verdict | Verified (A / V) | Gentests compiled (A / V) | Discriminating (A / V) | Anthropic USD (recorded, live) | Verda EUR (recorded) | Slowest lens s (A / V) |
|---|---|---|---|---|---|---|---|---|
| ju26-issue-2283 | needs-sign-off | needs-sign-off | 3 / 4 | 1/2 / 1/2 | 1/2 / 0/2 | 0.68 | 0.53 | 103 / 173 |
| ju26-issue-2234 | needs-sign-off | needs-sign-off | 5 / 5 | 2/2 / 2/2 | 2/2 / 0/2 | 0.55 | 0.43 | 85 / 126 |
| ju26-issue-2129 (with A/B) | blocked | blocked | 9 / 9 | 9/9 / 4/4 | 3/9 / 0/4 | 4.30 | 2.12 (ledger: 1.56) | 134 / 178 |
| ju26-issue-2297 (with A/B) | blocked | blocked | 3 / 1 | 9/9 / 4/4 | 1/9 / 0/4 | 2.53 | 1.18 (ledger: 0.76, plus 0.45 for a first attempt lost to a killed process) | 74 / 116 |
| ju26-issue-2297-r2 (with A/B) | not run on Anthropic | decision-A-B; side A needs-sign-off; side B not built | - / 1 | - / 2/2 | - / 0/2 | - | 1.40 (ledger: 0.99) | - / 88 |
| **Four changes** | 2 blocked, 2 needs-sign-off | same | 20 / 19 | 21/22 / 11/12 | 7/22 / 0/12 | **8.06** | **4.25** (ledger: 3.28) | 99 / 148 |

"Recorded" is how `provider-comparison.json` counts cost: every call of the run and of its
`__A`/`__B` side reviews at list price. A side review replays its parent's lens answers, so
recorded cost double-counts them on both providers. "Ledger" is what Verda actually billed.

- **Same verdict on all four changes.** Both providers block 2129 and 2297 on a verified high
  finding (2297: `requirement-tests-1`, the attribute dropped on netstandard2.0/.NET Framework) and
  send 2283 and 2234 to sign-off.
- **Kimi's tests rarely discriminate.** They compile about as often (11 of 12), but none of the 12
  behaved as its own plan said (Anthropic: 7 of 22). A test that fails where it was planned to pass
  gives a red receipt but cannot verify a claim, so Kimi's verified findings rest on mutation and
  the build instead. One exception is worth a human look: on 2283, Kimi's edge test
  (`PositionalRecordNullKeyReceiptTests`) compiled and failed on head. It verifies a claim that
  MsgPack005 is dropped for positional record properties. Opus wrote a test for the same idea, but
  it did not compile.
- **Kimi built reading B on 1 of 3 ambiguous changes.** On 2297 and 2297-r2 the A/B builder (Kimi,
  reasoning high) wrote a B variant that failed to build or failed the existing tests, twice each;
  B shows under "not checked". On 2129 it built B (`review/ju26-issue-2129@verda/B`, a 297-line
  removal), but the side-B review was cut off when the process was killed at 14:00, so there is no
  B receipt (the incomplete run is not in `runs/`). Opus built and reviewed both sides on 2129 and
  2297.
- **Fewer hunches** (20 vs 58): less noise in the receipt, and fewer leads.

### 2297 round 2: mergeable?

**No, not as an A/B decision.** Run `ju26-issue-2297-r2@verda/20261114T111141Z`: the readers again
found two readings (decision-A-B).

- Side A (the r2 head) is **needs-sign-off, not blocked.** The build and 171/171 existing tests
  pass, regeneration verifies 222/222 generated files, and there is no high finding. Its one
  requirement test (`ExcludeFromCodeCoverageGeneratedCodeTests`) still fails on head. That matches
  the namespace-mirror class the implementer deliberately left alone (`notes/round2.md`), but no
  lens attached a claim to it.
- Side B (the attribute on formatters only) has **no branch.** Kimi's builder could not write a
  passing B in two attempts.

So the round-1 blockers are gone on side A, but a human cannot choose between two unblocked sides,
because only one side exists. A named human (Anson) could still sign off side A as a plain change;
the pipeline never merges. 2297-r2 was not re-run on Anthropic in this pass: the Anthropic Tieto
runs belong to another track.

### The bench: 36 cases cold, mock-2283 warm

| Bench, cold, 36 cases | Anthropic (Opus + Sonnet, batched) | Verda (Kimi, live) |
|---|---|---|
| Planted defects caught | **21/24** (87.5%) | **18/24** (75%) |
| Escapes (planted defect marked auto-ready) | 0 | 0 |
| Control/bait cases with a false alarm | 4/8 | 4/8 |
| False-alarm findings at bait lines | 8 | 6 |
| Verdicts | 26 blocked, 10 needs-sign-off | 19 blocked, 10 needs-sign-off, 7 decision-A-B |
| Verified findings / hunches | 222 / 528 | 191 / 226 |
| Gentests compiled / discriminating | 91/93 / 34/93 | 55/77 / 6/77 |
| Questions to the human | 29 (0.81 per change) | 3 (0.08 per change) |
| Real spend | **USD 10.20** (Message Batches at 50%; USD 26.68 at live list price) | **EUR 16.91** (16.27 + 0.64 to re-run 2 cases) |
| Cost per case | USD 0.28 batched (0.74 live) | EUR 0.47 |

Warm `mock-2283` (9 cases, ledger holding the cold run's oracle answers): Anthropic caught 5/6
(the security plant became decision-A-B) and asked 5 questions (2 when cold). Verda caught 4/6 and
asked none, cold or warm. Warm spend: Anthropic USD 2.88 batched, Verda EUR 3.37.

Where Kimi loses recall: the 2283 edge and security plants (needs-sign-off instead of blocked,
cold and warm) and the 2129 edge and 2297 security variants, where its tests did not compile or did
not discriminate. On the 2297 variants Kimi returns decision-A-B instead of blocked: without a
verified high finding, the ambiguity rule decides. Kimi also blocked two 2234 cases that Anthropic
sent to sign-off (`mock-2234-clean`, `mock-2234-missing-test`).

### Cost per change

| | Anthropic | Verda (Kimi in every role) |
|---|---|---|
| Tieto change without A/B (2283, 2234) | USD 0.55-0.68 live | EUR 0.43-0.53 |
| Tieto change with A/B (2129, 2297) | USD 2.53-4.30 live | EUR 1.18-2.12 recorded, 0.76-1.56 billed (no side-B review, so fewer calls) |
| Bench case | USD 0.28 batched, 0.74 live | EUR 0.47 |

On the 2283 smoke review, Kimi wrote about as many output tokens as Opus on the deep lenses
(ratio 1.04) and was billed for about 56% of Anthropic's input tokens for the same prompts (a
different tokenizer). Prices are the pessimistic ones above (Kimi at $3/$15, 1 USD = 1 EUR, cached
input at the full price), so the euro figures are upper bounds.

### Wall time

Kimi's slowest lens takes about 1.5x as long as Anthropic's (148 s vs 99 s mean over the four
changes). The Verda cold bench took 81 minutes with 3 cases in parallel, sharing the machine with
the Anthropic replay pass. The Anthropic cold run took 3 Message Batches (3-4 minutes each) plus a
58-minute replay pass.

### Failure modes seen with Kimi

| Failure | Where | Count | What the pipeline did |
|---|---|---|---|
| Transport: "Server disconnected without sending a response" | all 6 lenses of `mock-2283-security`, 21:32 EET | 6 calls | lenses unavailable (not checked); case re-run |
| Transport: read timeout (600 s, streamed) | `mock-2283-compat`, logic-edge | 1 call | unavailable; case re-run |
| Format drift: valid JSON without the 5 required top-level keys, still wrong after the repair turn | `mock-2283-compat`, requirement-tests | 1 call, billed twice | unavailable, nothing invented; case re-run |
| A/B builder cannot write a building variant | side B of 2297 and 2297-r2 (2129 built) | 2 of 3 | reading B under not checked |
| Tests that run but do not behave as planned | bench gentests | 71 of 77 | red receipt, no verified claim |
| Empty reply when `max_tokens` is tiny (reasoning used it all) | 20-token probe | 1 | `max_tokens` doubled whenever reasoning is on |

No 429s and no `finish_reason: length` cut-offs on real calls. The ledger holds every call,
including the 7 failed ones (the disconnects reported no usage, so they cost nothing).

### What the comparison means for "code never leaves the company"

An open-weight model reproduces the pipeline's decisions on the real changes: the same verdict on
all four Tieto changes, and 0 escapes on 36 planted cases, at about half of Anthropic's live price
(EUR 4.25 vs USD 8.06 for the four). It is weaker exactly where PullReceipts earns trust. Its tests
run but rarely prove a claim (6 of 77 discriminating, against 34 of 93), so it catches 3 fewer
planted defects and built the second reading of an ambiguous requirement once in three tries.

Because every claim still needs an executed receipt, the weaker model shows up as more "not
checked" and fewer verified findings, never as a false green. That is what makes self-hosting safe
to try. A company can run the same open weights on its own Verda GPU instances (only the base URL
changes), keep the code in-house, and read in the receipts what the model could not prove. Today
the price of that choice is recall on edge and security defects. The deep, test-writing lenses are
where a stronger open model, or a hybrid (open model for the read-and-classify lenses, a frontier
model only for test writing), would pay off first.


## How it scales (Path to scale)

- **Per change.** Measured: a Tieto-sized change costs EUR 0.43-1.56 on Verda with Kimi in every
  role (EUR 4.25 for the four changes, A/B builds included), against USD 0.55-4.30 live on
  Anthropic; a bench case costs EUR 0.47. Moving the four read-and-classify lenses and the readers
  to a cheaper model (DeepSeek, once Verda opens it) would cut the non-deep share further, so cost
  scales with how many hunks risk-routing sends to the two deep lenses, which the router already
  limits.
- **Throughput.** There is no batch discount. Rate limits are per model and project; no 429 came
  back across 372 calls at 3 parallel reviews. Larger limits are a support request. The guard and
  the fixture keys work across parallel bench processes.
- **Company deployment.** The same open weights run on dedicated Verda GPU instances or serverless
  containers behind the same OpenAI-compatible API. Set `VERDA_INFERENCE_BASE_URL` to the private
  endpoint and nothing else changes. That is principle 8 taken all the way: the code under review
  stays on hardware the company controls, in the EU, with no per-token bill once utilisation is
  high.

## Reproduce

From `pipeline/`, with `VERDA_API_KEY` in the env file and both models enabled:

```sh
export LLM_PROVIDER=verda RECEIPTS_ENV_FILE=<repo>/.env.local
export VERDA_MODEL_DEEP=kimi-k3-1-pro VERDA_MODEL_LENS=kimi-k3-1-pro VERDA_MODEL_ROUTER=kimi-k3-1-pro  # as measured
uv run receipts estimate-verda                               # free projection

# 1. smoke: one real review (with A/B if readers split)
uv run receipts review --change ju26-issue-2283 --req REQ-2283
uv run receipts estimate-verda                               # now uses the measured output ratio

# 2. bench, cold then warm subset (live; --batch falls back to live on Verda)
uv run receipts bench run
uv run receipts bench run --warm --only mock-2283
uv run receipts bench run --warm --only mock-2234

# 3. 2297 round 2 and the Tieto changes
uv run receipts review --change ju26-issue-2297-r2 --req REQ-2297
for n in 2234 2129 2297; do uv run receipts review --change ju26-issue-$n --req REQ-$n; done

# 4. metrics (MOCK_LLM=1 so T-table router re-scoring can never make a live call)
MOCK_LLM=1 uv run receipts metrics --provider verda
python3 -c "import json;print(json.load(open('../work/verda-spend.json'))['total_eur'])"
```

Offline replay of any recorded Verda run: `LLM_PROVIDER=verda MOCK_LLM=1 uv run receipts review ...`.

[site]: https://www.verda.com/inference
[models]: https://docs.verda.com/inference/models/
[kimi]: https://docs.verda.com/inference/models/kimi-k3-1-pro/
[deepseek]: https://docs.verda.com/inference/models/deepseek-4-1-flash-1017/
[compat]: https://docs.verda.com/inference/openai-compatibility/
[quick]: https://docs.verda.com/inference/quickstart/
[limits]: https://docs.verda.com/inference/rate-limits/
[faq]: https://docs.verda.com/inference/faq/
[llms]: https://docs.verda.com/llms.txt
