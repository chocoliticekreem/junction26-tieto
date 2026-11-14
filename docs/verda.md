# Receipts on Verda: open-weight, EU-hosted review models

Status Sat 14 Nov 2026, 11:30 EET: **the provider is built and tested, but no review has run on
Verda yet.** The key in `.env.local` authenticates, but no model is enabled for its project:
`GET /v1/models` returns an empty list, and chat calls return `403 subscription_required`. Refused
requests are not billed (Verda FAQ). Enabling a model takes a click in the Console
(`BLOCKED.md` item 1b). Every number below that says "projected" comes from recorded Anthropic
token counts re-priced at Verda list prices, not from a Verda run. Verda spend so far: **EUR 0.00**.

## Why Verda (Fit)

Receipts reviews agent-written code before it merges. Principle 8 says the code under review
never leaves the company. A US-hosted API weakens that promise. Verda fits it better:

- **Verda Inference** serves open-weight models per token through an OpenAI-compatible API,
  hosted on Verda GPUs in Germany ([models][models], [FAQ][faq]).
- No training on inputs or outputs. Prompts are not stored: the in-memory cache is cleared within
  2 hours. No subprocessors, and nothing is sent to the model developer ([FAQ][faq]). SOC 2 Type
  II, ISO 27001/27017/27018/27701, C5, GDPR ([FAQ][faq]).
- **The same weights can be self-hosted.** Kimi K3.1 Pro and DeepSeek V4.1 Flash 1017 publish
  their weights on Hugging Face ([Kimi][kimi], [DeepSeek][deepseek]). A company that wants the
  code on its own hardware runs the same model on Verda GPU instances or serverless containers
  (vLLM tutorials in the [docs index][llms]). Receipts only needs the base URL to change.

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
DeepSeek scores higher than Kimi on the one benchmark both report (DeepSWE). That makes
"DeepSeek for every role" the obvious cheap ablation once the first runs are in.

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

**Not run yet** (models not enabled). The comparison table fills from
`uv run receipts metrics --provider verda`. Recorded Anthropic cost per change, for reference
(`metrics/provider-comparison.json`, all LLM calls of the run, A/B side reviews included):

| Change | Anthropic verdict | Anthropic cost (USD) | Verda verdict | Verda cost (EUR) |
|---|---|---|---|---|
| ju26-issue-2283 | needs-sign-off | 0.68 | pending | pending |
| ju26-issue-2234 | needs-sign-off | 0.55 | pending | pending |
| ju26-issue-2129 (with A/B) | blocked | 4.30 | pending | pending |
| ju26-issue-2297 (with A/B) | blocked | 2.53 | pending | pending |
| ju26-issue-2297-r2 (with A/B) | not determined (Anthropic limit hit) | 0.00 | pending | pending |
| Bench cold, 36 cases (PR #11 pipeline) | 19/24 plants caught, 0 escapes | 24.61 (lenses 24.56 + router 0.05, `metrics/summary.json`) | pending | pending |

### Projected cost of the re-run plan (EUR, at the pessimistic prices above)

From `uv run receipts estimate-verda`, with the Verda output tokens assumed equal to Anthropic's
(output ratio 1.0, because no Verda run exists yet):

| Plan | Runs | Kimi (deep) | DeepSeek (other) | Total EUR |
|---|---|---|---|---|
| (a) cold bench, 36 cases, `--no-ab` | 36 | 14.70 | 0.35 | 15.06 |
| (b) warm `mock-2283` | 9 | 3.42 | 0.07 | 3.49 |
| (b) warm `mock-2234` | 9 | 2.56 | 0.06 | 2.62 |
| (c) 2297 round 2 with A/B (token basis: round 1) | 3 | 1.47 | 0.04 | 1.51 |
| (d) four Tieto changes with A/B | 8 | 4.71 | 0.13 | 4.84 |
| **Total** | | | | **27.52** |

Kimi's output tokens make up about EUR 21 of this, and Kimi reasons before it answers. So the
total stays under the EUR 35 working ceiling only while Kimi's output is at most about **1.36x**
Opus's. The smoke review measures that ratio. If the projection then passes EUR 35, drop runs in
this order: warm `mock-2234`, then the Tieto re-runs except 2283 and 2297-r2, then the cold bench
down to a stratified half (2 issues). The guard stops everything at EUR 40 regardless.

## Failure modes to expect with open models (watch list; none observed yet)

- **Format drift.** A missing required key, a misspelled enum, prose around the JSON. This is
  handled by validation plus one repair; a second failure is declared.
- **Reasoning eats the budget.** Kimi defaults to `max` reasoning. If `max_tokens` runs out,
  `content` is empty with `finish_reason: length`. Mitigations: effort `high` (not `max`), doubled
  `max_tokens`, and a cut-off is declared without paying for a repair.
- **Compile traps in generated C# tests.** Receipts already compiles every generated test and
  shows a non-compiling one as `error`, not as evidence (`gentests_compiled` in the comparison).
- **Capacity.** "Out of capacity" models do not open to new projects, and 429s can arrive within
  limits on a busy model. 429s are retried after `Retry-After`; the default bench concurrency is 3.

## How it scales (Path to scale)

- **Per change.** At the projected rate a Tieto-sized change costs about EUR 0.5-1.5 on Verda
  (the projected EUR 4.84 for the four changes, A/B builds included), against USD 0.55-4.30 on
  Anthropic. DeepSeek handles the cheap lenses at almost no cost: EUR 0.35 for all 144 non-deep
  lens calls of the bench. So cost scales with how many hunks risk-routing sends to the two deep
  lenses, which the router already limits.
- **Throughput.** There is no batch discount, but rate limits are per model and project, so the
  deep model (Kimi) and the cheap one (DeepSeek) do not compete for the same TPM. Larger limits are
  a support request. The guard and the fixture keys work across parallel bench processes.
- **Company deployment.** The same open weights run on dedicated Verda GPU instances or serverless
  containers behind the same OpenAI-compatible API. Set `VERDA_INFERENCE_BASE_URL` to the private
  endpoint and nothing else changes. That is principle 8 taken all the way: the code under review
  stays on hardware the company controls, in the EU, with no per-token bill once utilisation is
  high.

## Reproduce

From `pipeline/`, with `VERDA_API_KEY` in the env file and both models enabled:

```sh
export LLM_PROVIDER=verda RECEIPTS_ENV_FILE=<repo>/.env.local
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
