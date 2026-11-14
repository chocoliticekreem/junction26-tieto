# Jev (TypeSafe) router: what we learned

Status 2026-11-14: **working** with the key in `.env.local`. 29 real hunks scored (8 from
`ju26-issue-2283`, 21 from `ju26-issue-2297`), all HTTP 200 on the first attempt, fixtures in
`fixtures/llm/router-jev/`. Implementation: `pipeline/receipts/routers/jev.py`.

## Source of the contract

MIT-licensed `jev-review` (github.com/NiazMorshed2007/jev-review, shallow clone of `main`):

| What | File |
|---|---|
| Endpoint, model name, auth header, timeout, retries, error mapping | `src/jev/client.ts` |
| Response schema (zod) | `src/jev/schema.ts` |
| Question types (`noul`, `score`, `choice`) and how they are phrased | `src/evaluation/questions.ts` |
| `state` shape (`task`, `diff`, `files`, `repositoryContext`) | `src/evaluation/input.ts` |
| Score mapping (`score + 1` onto 1..10), confidence handling | `src/evaluation/transform.ts` |
| ~32,768-token input ceiling (observed, not published), `max_tokens_exceeded` | `README.md` ("Input size"), `src/jev/client.ts` |

## Request

```
POST https://api.typesafe.ai/v1/systemone
Authorization: Bearer $JEV_API_KEY
Content-Type: application/json
```

Body. PullReceipts sends one hunk per request, never the whole diff:

```json
{
  "model": "jev-latest",
  "state": {
    "task": "<requirement text, e.g. the upstream issue #2283>",
    "diff": "File: src/MessagePack.SourceGenerator/CodeAnalysis/TypeCollector.cs (kind: source, area: type-collector)\n@@ -236,7 +236,7 @@ public class TypeCollector\n ... hunk body ..."
  },
  "questions": {
    "risk": {
      "type": "score",
      "instructions": "Rate how much review depth this single diff hunk needs, judged against the task. ...",
      "criteria": ["1 - Formatting, comments or docs only; no behaviour change.", "...", "10 - Change that can weaken security or corrupt data for all users."]
    },
    "category": {
      "type": "choice",
      "instructions": "Choose the single main risk dimension of this diff hunk.",
      "criteria": {"correctness": "...", "security": "...", "compatibility": "...", "tests": "...", "docs": "...", "generated": "..."}
    }
  }
}
```

A `score` question takes a list of up to 10 level descriptions; a `choice` question takes a map of
option id to description. Any question ids work; answers come back under the same ids.

## Response (real, `TypeCollector.cs#1` of 2283, legend shortened)

```json
{
  "model": "jev-1.14.0",
  "answers": {
    "risk": {
      "type": "score", "score": 4.93, "confidence": 0.56,
      "legend": {"0": "1 - Formatting, ...", "9": "10 - Change that can weaken security ..."},
      "probabilities": {"0": 0.04, "1": 0.08, "2": 0.05, "3": 0.04, "4": 0.05, "5": 0.05, "6": 0.69, "7": 0.0, "8": 0.0, "9": 0.0}
    },
    "category": {
      "type": "choice", "choice": "correctness", "confidence": 1.0,
      "probabilities": {"correctness": 1.0, "security": 0.0, "compatibility": 0.0, "tests": 0.0, "docs": 0.0, "generated": 0.0}
    }
  },
  "usage": {"input_tokens": 2661, "output_tokens": 79}
}
```

Response headers include `x-typesafe-request-id` (recorded in the fixture as `request_id`).
`model: "jev-latest"` resolved to `jev-1.14.0` on 2026-11-14.

## Mapping to `RouteResult`

- `score` is the expectation over the 0-based levels. On bimodal answers it falls between the modes
  (above: 4.93 while level 6 has p=0.69), so risk = most probable level + 1; the expectation is kept
  in `reason`. Fallback when no probabilities: `round(score) + 1`.
- `category` = the `choice` answer (validated against the six categories).
- `confidence` = min(score confidence, choice confidence): the route is only as sure as its weaker half.
- `cost_tokens` = `usage.input_tokens + usage.output_tokens` (the API returns usage; if it ever does
  not, chars/4 is used and `reason` says "estimated").
- Anything outside the schema (missing answer, unknown category, score outside 0..9, non-level
  probability keys) raises `RouterOutputError`; it is never coerced.

## Errors, retries, limits

- 30 s timeout; up to two retries on 429, 529 and 5xx, honouring `Retry-After` (capped at 5 s), else
  250 ms x 2^attempt. 401/422/400 are not retried. Mirrors `src/jev/client.ts`.
- 400 with `detail.error_type == "max_tokens_exceeded"` when the state is over the ceiling. PullReceipts
  budgets the state at (32,768 - 4,000) x 4 chars and truncates the hunk text with an explicit
  `[... truncated by receipts: N chars omitted ...]` marker; the route's `reason` then says so. No
  real hunk needed truncation (largest: 4,259 input tokens).
- Every call goes through `llm.replay_or_record` (purpose `router-jev`, key = sha256 of model, state
  and questions), so `MOCK_LLM=1` replays without network and a missing key with no fixture raises
  `JevUnavailable` (a subclass of `llm.LLMUnavailable`).

## Measured on the two sample changes

| Change | Hunks | Input+output tokens | Cost (Jev) | Cost (Haiku 4.5 LLMRouter, same hunks) |
|---|---|---|---|---|
| ju26-issue-2283 | 8 | 21,498 | $0.0009 | $0.0199 (19,910 tokens) |
| ju26-issue-2297 (15 non-generated files) | 21 | 26,478 | $0.0011 | $0.0227 (22,746 tokens) |

Jev latency: median 198 ms, p90 219 ms, max 396 ms per hunk (29 calls). Haiku median 2,062 ms.
Jev cost uses $42 per billion input tokens; TypeSafe publishes no output price, so output tokens are
counted but priced at 0. Haiku cost in `RoutingReport` uses `usd_per_token` = the input price
($1/M), a small underestimate because output tokens cost $5/M.

Risk distributions (most-probable-level mapping): 2283 Jev [1,1,1,1,7,7,7,7] vs Haiku
[1,1,2,3,3,3,4,5]; 2297 Jev ten hunks at 7, nine at 3, one at 5, one at 1, vs Haiku spread 1-7.
Jev is sharply bimodal (docs/tests near 1, logic changes at 7) and its confidence on 2297 template
hunks is low (0.06-0.47); that is the signal T3 should surface rather than hide. Haiku reports
uniformly high confidence (0.6-0.95) regardless.
