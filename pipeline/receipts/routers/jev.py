"""JevRouter: TypeSafe's Jev decision model scores one hunk via typed Score + Choice questions.

API contract read from the MIT-licensed jev-review source (github.com/NiazMorshed2007/jev-review,
src/jev/client.ts, src/jev/schema.ts, src/evaluation/questions.ts); details in notes/jev.md.

  POST https://api.typesafe.ai/v1/systemone
  Authorization: Bearer $JEV_API_KEY
  {"state": {...}, "model": "jev-latest", "questions": {id: {type, instructions, criteria}}}
  -> {"model", "answers": {id: {type: "score", score: 0..9, confidence, probabilities, legend}
                               | {type: "choice", choice, confidence, probabilities}},
      "usage": {"input_tokens", "output_tokens"}}

30 s timeout, up to two retries on 429/529/5xx (honouring Retry-After, capped at 5 s). Every call
goes through llm.replay_or_record (purpose "router-jev"), so MOCK_LLM=1 replays recorded answers.
"""

import time

import httpx

from .. import config, llm
from ..models import Hunk, RouteResult
from .llm_router import RouterOutputError, truncate

PURPOSE = "router-jev"
ENDPOINT = "https://api.typesafe.ai/v1/systemone"
JEV_MODEL = "jev-latest"
TIMEOUT_S = 30.0
MAX_RETRIES = 2
INPUT_TOKEN_CEILING = 32_768  # observed by jev-review; not published by TypeSafe
STATE_CHAR_BUDGET = (INPUT_TOKEN_CEILING - 4_000) * 4  # leave room for questions; ~4 chars/token

RISK_LEVELS = [
    "1 - Formatting, comments or docs only; no behaviour change.",
    "2 - Trivial rename or refactor; behaviour clearly unchanged.",
    "3 - Small local behaviour change that is easy to verify by reading.",
    "4 - Local behaviour change with a few branches to check.",
    "5 - Logic change that other code depends on, or a change to behaviour-guarding tests.",
    "6 - Non-trivial logic change with edge cases that need executed tests.",
    "7 - Change to error handling, diagnostics or code generation paths.",
    "8 - Change to public API, serialization format or security-relevant handling.",
    "9 - Change that can silently break wire compatibility or corrupt data.",
    "10 - Change that can weaken security or corrupt data for all users.",
]
CATEGORY_CRITERIA = {
    "correctness": "The main risk is wrong behaviour or logic: missing behaviour, edge cases, regressions.",
    "security": "The main risk is weakened security: untrusted input handling, limits, unsafe code.",
    "compatibility": "The main risk is breaking existing users: public API, wire format, serialized bytes.",
    "tests": "The hunk is test code; the main risk is tests that do not really guard the behaviour.",
    "docs": "The hunk is documentation or comments only.",
    "generated": "The hunk is machine-generated output (snapshots, *.g.cs) that should be regenerated, not read.",
}
QUESTIONS = {
    "risk": {
        "type": "score",
        "instructions": (
            "Rate how much review depth this single diff hunk needs, judged against the task. "
            "Higher means more likely to hide a defect or break existing behaviour. "
            "Judge only what the hunk shows; do not invent concerns."
        ),
        "criteria": RISK_LEVELS,
    },
    "category": {
        "type": "choice",
        "instructions": "Choose the single main risk dimension of this diff hunk.",
        "criteria": CATEGORY_CRITERIA,
    },
}


class JevUnavailable(llm.LLMUnavailable):
    pass


def build_state(hunk: Hunk, requirement_text: str, area: str = "") -> tuple[dict, bool]:
    """Per-hunk state. Returns (state, truncated)."""
    header = f"File: {hunk.file} (kind: {hunk.kind}{', area: ' + area if area else ''})\n"
    header += f"@@ -{hunk.old_start},{hunk.old_lines} +{hunk.new_start},{hunk.new_lines} @@ {hunk.header}".rstrip()
    budget = STATE_CHAR_BUDGET - len(requirement_text) - len(header) - 200  # 200: truncation marker
    diff = truncate(hunk.text, max(budget, 2_000))
    return {"task": requirement_text.strip(), "diff": header + "\n" + diff}, len(diff) < len(hunk.text)


def _post(state: dict) -> dict:
    if not config.JEV_API_KEY:
        raise JevUnavailable("JEV_API_KEY not set and no fixture")
    body = {"state": state, "model": JEV_MODEL, "questions": QUESTIONS}
    headers = {"Authorization": f"Bearer {config.JEV_API_KEY}", "Content-Type": "application/json"}
    t0 = time.monotonic()
    last = ""
    with httpx.Client(timeout=TIMEOUT_S) as client:
        for attempt in range(MAX_RETRIES + 1):
            try:
                r = client.post(ENDPOINT, json=body, headers=headers)
            except httpx.TimeoutException as e:
                raise JevUnavailable(f"Jev did not respond within {TIMEOUT_S:.0f}s") from e
            except httpx.HTTPError as e:
                raise JevUnavailable(f"could not reach Jev: {type(e).__name__}") from e
            if r.status_code == 200:
                return {"response": r.json(), "ms": int((time.monotonic() - t0) * 1000),
                        "request_id": r.headers.get("x-typesafe-request-id", ""), "attempts": attempt + 1}
            last = f"HTTP {r.status_code}: {r.text[:300]}"
            retryable = r.status_code in (429, 529) or r.status_code >= 500
            if not retryable or attempt == MAX_RETRIES:
                break
            time.sleep(_retry_delay(r.headers.get("retry-after"), attempt))
    raise JevUnavailable(f"Jev request failed: {last}")


def _retry_delay(retry_after: str | None, attempt: int) -> float:
    try:
        return min(max(float(retry_after), 0.0), 5.0) if retry_after is not None else 0.25 * 2**attempt
    except ValueError:
        return 0.25 * 2**attempt


def parse_answers(resp: dict, hunk_id: str, ms: int, est_tokens: int) -> RouteResult:
    answers = resp.get("answers") if isinstance(resp, dict) else None
    if not isinstance(answers, dict):
        raise RouterOutputError("Jev response has no answers object")
    risk_a, cat_a = answers.get("risk"), answers.get("category")
    if not isinstance(risk_a, dict) or risk_a.get("type") != "score":
        raise RouterOutputError(f"Jev omitted the risk score: {risk_a!r}")
    if not isinstance(cat_a, dict) or cat_a.get("type") != "choice":
        raise RouterOutputError(f"Jev omitted the category choice: {cat_a!r}")
    score = risk_a.get("score")
    if not isinstance(score, (int, float)) or not 0 <= score <= 9:
        raise RouterOutputError(f"Jev score out of range 0..9: {score!r}")
    category = cat_a.get("choice")
    if category not in CATEGORY_CRITERIA:
        raise RouterOutputError(f"Jev chose an unknown category: {category!r}")
    conf = min(float(risk_a.get("confidence", 0.0)), float(cat_a.get("confidence", 0.0)))
    usage = resp.get("usage") or {}
    if "input_tokens" in usage:
        tokens = int(usage["input_tokens"]) + int(usage.get("output_tokens", 0))
        tok_note = f"{tokens} tokens"
    else:
        tokens = est_tokens
        tok_note = f"{tokens} tokens (estimated, chars/4)"
    # Jev's `score` is the expectation over levels; on bimodal answers it lands between the modes and
    # understates risk, so the most probable level decides and the expectation is kept in the reason.
    probs = risk_a.get("probabilities") or {}
    try:
        level = int(max(probs, key=probs.get)) if probs else round(score)
    except (TypeError, ValueError) as e:
        raise RouterOutputError(f"Jev risk probabilities are not keyed by level: {probs!r}") from e
    risk = min(10, max(1, level + 1))
    reason = (f"jev {resp.get('model', '?')}: most likely level {level}/9, expected {score:.2f} -> risk {risk} "
              f"(p={risk_a.get('confidence', 0):.2f}); category {category} "
              f"(p={cat_a.get('confidence', 0):.2f}); {tok_note}")
    return RouteResult(hunk_id=hunk_id, risk=risk, category=category, confidence=round(conf, 3),
                       router="jev", cost_tokens=tokens, ms=ms, reason=reason)


class JevRouter:
    name = "jev"
    usd_per_token = llm.PRICES_PER_MTOK[JEV_MODEL][0] / 1_000_000

    def route(self, hunk: Hunk, requirement_text: str, context: dict | None = None) -> RouteResult:
        area = (context or {}).get("area", "")
        state, truncated = build_state(hunk, requirement_text, area)
        rec, _ = llm.replay_or_record(PURPOSE, [JEV_MODEL, state, QUESTIONS], lambda: _post(state))
        est = (len(state["task"]) + len(state["diff"])) // 4
        result = parse_answers(rec["response"], hunk.id, rec.get("ms", 0), est)
        if truncated:
            result.reason += "; hunk truncated to fit Jev's input ceiling"
        return result
