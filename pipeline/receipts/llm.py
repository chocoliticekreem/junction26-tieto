"""The single door to every LLM call, with record/replay fixtures.

Every call is keyed by sha256(model, system, user, schema), plus the effort when a caller passes a
`default_effort` and runs at a different one (so fixtures recorded at the default keep their keys),
plus the provider when it is not Anthropic (config.LLM_PROVIDER; the Verda backend is verda.py), so
a Verda answer never replays as an Anthropic one or the other way round.
A real call writes its response to fixtures/llm/<purpose>/<key>.json. With MOCK_LLM=1 the call is
answered from that file only. Prompts must therefore be deterministic: never put timestamps, run
ids or absolute temp paths in them.

When no key and no fixture exist, LLMUnavailable is raised. Callers catch it and record the step
under "not checked" instead of inventing output.

config.LLM_MODE changes what a fixture miss does:
  collect  append the request to <BATCH_DIR>/queue.jsonl and raise PendingLLM (a subclass of
           LLMUnavailable, so every independent call in the process still queues). run_batch later
           answers the queue through the Message Batches API and writes the same fixture files.
  replay   raise LLMUnavailable; never call the API.
In both, a request whose batch result failed (<BATCH_DIR>/failed.jsonl) raises LLMUnavailable with
that reason, so it is reported, never retried silently or invented.
"""

import fcntl
import hashlib
import json
import threading
import time
from collections.abc import Callable
from dataclasses import dataclass, field

from . import config

BATCH_DISCOUNT = 0.5  # Message Batches bill every token at half the standard price


class LLMUnavailable(Exception):
    pass


class PendingLLM(LLMUnavailable):
    """collect mode: the request is queued for a batch; nothing was answered."""


@dataclass
class LLMResult:
    data: dict
    model: str
    input_tokens: int
    output_tokens: int
    ms: int
    from_fixture: bool
    provider: str = "anthropic"


def _key(model: str, system: str, user: str, schema: dict, effort: str | None = None,
         provider: str = "anthropic") -> str:
    parts: list = [model, system, user, schema]
    if effort is not None:
        parts.append({"effort": effort})
    # Left out for Anthropic so every fixture recorded before the provider switch keeps its key.
    if provider != "anthropic":
        parts.append({"provider": provider})
    blob = json.dumps(parts, sort_keys=True, ensure_ascii=False)
    return hashlib.sha256(blob.encode()).hexdigest()[:24]


def fixture_key(model: str, system: str, user: str, schema: dict, effort: str,
                default_effort: str | None = None, provider: str | None = None) -> str:
    keyed = effort if default_effort is not None and effort != default_effort else None
    return _key(model, system, user, schema, keyed, provider or config.LLM_PROVIDER)


def _fixture_path(purpose: str, key: str):
    return config.FIXTURES_LLM / purpose / f"{key}.json"


def request_params(model: str, system: str, user: str, schema: dict, effort: str,
                   max_tokens: int) -> dict:
    """The Messages API body, identical for a live call and a batch request."""
    output_config: dict = {"format": {"type": "json_schema", "schema": schema}}
    if not model.startswith("claude-haiku"):
        output_config["effort"] = effort
    return {"model": model, "max_tokens": max_tokens, "system": system,
            "messages": [{"role": "user", "content": user}], "output_config": output_config}


def _parse_message(msg, purpose: str) -> dict:
    if msg.stop_reason == "refusal":
        raise LLMUnavailable(f"model refused ({purpose})")
    text = next((b.text for b in msg.content if b.type == "text"), "")
    try:
        return json.loads(text)
    except json.JSONDecodeError as e:
        raise LLMUnavailable(f"non-JSON output ({msg.stop_reason}): {text[:200]}") from e


def _write_fixture(purpose: str, key: str, model: str, data: dict, input_tokens: int,
                   output_tokens: int, ms: int, user: str, **extra) -> None:
    rec = {"purpose": purpose, "model": model, "data": data, "input_tokens": input_tokens,
           "output_tokens": output_tokens, "ms": ms, "prompt_preview": user[:400], **extra}
    path = _fixture_path(purpose, key)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(rec, indent=2, ensure_ascii=False))


# ---------------------------------------------------------------- collect queue

_pending_lock = threading.Lock()
_pending = 0


def queue_path():
    return config.BATCH_DIR / "queue.jsonl"


def failed_path():
    return config.BATCH_DIR / "failed.jsonl"


def pending_count() -> int:
    """Requests this process queued in collect mode. A caller whose next prompt depends on earlier
    answers checks this and stops instead of building that prompt from missing answers."""
    return _pending


def _append_jsonl(path, rec: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    # Several review processes append at once; one locked write per line keeps lines whole.
    with path.open("a") as fh:
        fcntl.flock(fh, fcntl.LOCK_EX)
        try:
            fh.write(json.dumps(rec, ensure_ascii=False) + "\n")
            fh.flush()
        finally:
            fcntl.flock(fh, fcntl.LOCK_UN)


def read_jsonl(path) -> list[dict]:
    if not path.exists():
        return []
    return [json.loads(ln) for ln in path.read_text().splitlines() if ln.strip()]


def failed_reason(key: str) -> str:
    return next((r["reason"] for r in read_jsonl(failed_path()) if r["custom_id"] == key), "")


def _enqueue(purpose: str, key: str, params: dict) -> None:
    global _pending
    _append_jsonl(queue_path(), {"custom_id": key, "purpose": purpose, "params": params})
    with _pending_lock:
        _pending += 1


# ---------------------------------------------------------------- call


def call_json(
    purpose: str, model: str, system: str, user: str, schema: dict, effort: str = "medium",
    max_tokens: int = 16000, default_effort: str | None = None,
) -> LLMResult:
    """Ask `model` for a JSON object matching `schema`. `purpose` names the fixture folder.
    `default_effort`: the effort this call's fixtures were recorded at (see fixture_key)."""
    key = fixture_key(model, system, user, schema, effort, default_effort)
    path = _fixture_path(purpose, key)
    if path.exists():
        rec = json.loads(path.read_text())
        return LLMResult(rec["data"], model, rec.get("input_tokens", 0), rec.get("output_tokens", 0),
                         rec.get("ms", 0), True, rec.get("provider", "anthropic"))
    params = request_params(model, system, user, schema, effort, max_tokens)
    if config.LLM_MODE in ("collect", "replay"):
        reason = failed_reason(key)
        if reason:
            raise LLMUnavailable(f"batch request {purpose}/{key} failed: {reason}")
        if config.LLM_MODE == "replay":
            raise LLMUnavailable(f"replay mode and no fixture for {purpose}/{key}")
        _enqueue(purpose, key, params)
        raise PendingLLM(f"queued for batch: {purpose}/{key}")
    if config.MOCK_LLM:
        raise LLMUnavailable(f"MOCK_LLM=1 and no fixture for {purpose}/{key}")
    if config.LLM_PROVIDER == "verda":
        from . import verda

        return verda.call(purpose, key, model, system, user, schema, effort, max_tokens)
    if not config.ANTHROPIC_API_KEY:
        raise LLMUnavailable(f"ANTHROPIC_API_KEY not set and no fixture for {purpose}/{key}")

    import anthropic

    from . import anthropic_spend

    rid = anthropic_spend.reserve(anthropic_spend.worst_usd(params), f"live {purpose}")
    client = anthropic.Anthropic(api_key=config.ANTHROPIC_API_KEY, timeout=300, max_retries=2)
    t0 = time.monotonic()
    msg = None
    try:
        with client.messages.stream(**params) as stream:
            msg = stream.get_final_message()
    except anthropic.APIConnectionError as e:
        raise LLMUnavailable(f"connection error: {e}") from e
    except anthropic.APIStatusError as e:
        raise LLMUnavailable(f"API error {e.status_code}: {e.message}") from e
    finally:
        tin, tout = (msg.usage.input_tokens, msg.usage.output_tokens) if msg else (0, 0)
        anthropic_spend.settle(rid, f"live {purpose}", cost_usd(model, tin, tout), model=model,
                               input_tokens=tin, output_tokens=tout,
                               status=msg.stop_reason if msg else "error")
    ms = int((time.monotonic() - t0) * 1000)
    data = _parse_message(msg, purpose)
    _write_fixture(purpose, key, model, data, msg.usage.input_tokens, msg.usage.output_tokens, ms,
                   user)
    return LLMResult(data, model, msg.usage.input_tokens, msg.usage.output_tokens, ms, False)


# ---------------------------------------------------------------- batch


@dataclass
class BatchReport:
    batch_id: str = ""
    submitted: int = 0
    succeeded: int = 0
    errored: list[dict] = field(default_factory=list)  # {custom_id, purpose, reason}
    input_tokens: dict[str, int] = field(default_factory=dict)  # model -> tokens
    output_tokens: dict[str, int] = field(default_factory=dict)

    @property
    def cost_usd(self) -> float:
        """Actual spend at batch prices, from the usage the results report."""
        models = set(self.input_tokens) | set(self.output_tokens)
        return round(sum(cost_usd(m, self.input_tokens.get(m, 0), self.output_tokens.get(m, 0))
                         for m in models) * BATCH_DISCOUNT, 6)


def unique_requests(entries: list[dict]) -> dict[str, dict]:
    """Queue lines -> {custom_id: entry}, dropping repeats and requests a fixture already answers
    (several cases can queue the same prompt, e.g. the readers of one requirement)."""
    out: dict[str, dict] = {}
    for e in entries:
        if e["custom_id"] not in out and not _fixture_path(e["purpose"], e["custom_id"]).exists():
            out[e["custom_id"]] = e
    return out


def _error_reason(result) -> str:
    err = getattr(result, "error", None)
    inner = getattr(err, "error", err)
    detail = ": ".join(str(x) for x in (getattr(inner, "type", ""), getattr(inner, "message", ""))
                       if x)
    return f"{result.type}" + (f" ({detail})" if detail else "")


def run_batch(requests: dict[str, dict], client=None, poll_s: float = 30.0,
              sleep: Callable[[float], None] = time.sleep, log: Callable[[str], None] = print
              ) -> BatchReport:
    """Submit `requests` ({custom_id: queue entry}) as one Message Batch, wait for it, and write
    every successful result as a fixture, exactly as a live call would. A failed result (errored,
    canceled, expired, refused, non-JSON or missing) is appended to failed.jsonl and reported.

    The batch id stays in <BATCH_DIR>/inflight.json until its results are written, so a run that
    dies while polling resumes the same batch instead of paying for a second one."""
    report = BatchReport()
    if not requests:
        return report
    if client is None:
        import anthropic

        client = anthropic.Anthropic(api_key=config.ANTHROPIC_API_KEY, max_retries=2)
    inflight = config.BATCH_DIR / "inflight.json"
    ids = sorted(requests)
    if inflight.exists():
        state = json.loads(inflight.read_text())
        # A subset is fine: results written before a crash are fixtures now and drop out.
        if not set(ids) <= set(state["custom_ids"]):
            raise RuntimeError(
                f"{inflight} holds unfinished batch {state['batch_id']} for a different set of "
                "requests. Its results stay retrievable for 29 days; delete the file to discard it.")
        report.batch_id = state["batch_id"]
        log(f"batch: resuming {report.batch_id} ({len(ids)} requests)")
    else:
        batch = client.messages.batches.create(
            requests=[{"custom_id": k, "params": requests[k]["params"]} for k in ids])
        report.batch_id = batch.id
        config.BATCH_DIR.mkdir(parents=True, exist_ok=True)
        inflight.write_text(json.dumps({"batch_id": batch.id, "custom_ids": ids}))
        log(f"batch: submitted {batch.id} ({len(ids)} requests)")
    report.submitted = len(ids)

    while (status := client.messages.batches.retrieve(report.batch_id).processing_status) != "ended":
        log(f"batch: {report.batch_id} {status}; next check in {poll_s:.0f}s")
        sleep(poll_s)

    seen: set[str] = set()

    def fail(cid: str, reason: str) -> None:
        rec = {"custom_id": cid, "purpose": requests[cid]["purpose"], "reason": reason}
        report.errored.append(rec)
        _append_jsonl(failed_path(), rec)

    for r in client.messages.batches.results(report.batch_id):
        cid = r.custom_id
        if cid not in requests or cid in seen:
            continue
        seen.add(cid)
        e = requests[cid]
        if r.result.type != "succeeded":
            fail(cid, _error_reason(r.result))
            continue
        msg = r.result.message
        try:
            data = _parse_message(msg, e["purpose"])
        except LLMUnavailable as ex:
            fail(cid, str(ex))
            continue
        model = e["params"]["model"]
        _write_fixture(e["purpose"], cid, model, data, msg.usage.input_tokens,
                       msg.usage.output_tokens, 0, e["params"]["messages"][0]["content"],
                       batch_id=report.batch_id)
        report.succeeded += 1
        report.input_tokens[model] = report.input_tokens.get(model, 0) + msg.usage.input_tokens
        report.output_tokens[model] = report.output_tokens.get(model, 0) + msg.usage.output_tokens
    for cid in ids:
        if cid not in seen:
            fail(cid, "no result returned by the batch")
    inflight.unlink(missing_ok=True)
    return report


# USD per million tokens (input, output), Anthropic first-party list prices; Jev per typesafe.ai;
# Verda per verda.com/inference (14 Nov 2026). That page shows Kimi K3.1 Pro at both $2/$10 and
# $3/$15; the higher one is used so the spend guard never under-counts. Cached input (cheaper on
# Verda) is billed here at the full input price: an upper bound.
PRICES_PER_MTOK: dict[str, tuple[float, float]] = {
    "claude-opus-5-5": (4.0, 20.0),
    "claude-sonnet-5-5": (2.0, 10.0),
    "claude-haiku-4-5-20251001": (1.0, 5.0),
    "jev-latest": (0.042, 0.0),  # $42 per billion input tokens; output price not published
    "kimi-k3-1-pro": (3.0, 15.0),
    "deepseek-4-1-flash-1017": (0.15, 0.60),
}


def cost_usd(model: str, input_tokens: int, output_tokens: int) -> float:
    pin, pout = PRICES_PER_MTOK.get(model, (0.0, 0.0))
    return round((input_tokens * pin + output_tokens * pout) / 1_000_000, 6)


def cost_eur(model: str, input_tokens: int, output_tokens: int) -> float:
    """At config.VERDA_EUR_PER_USD (default parity, see config)."""
    return round(cost_usd(model, input_tokens, output_tokens) * config.VERDA_EUR_PER_USD, 6)


def replay_or_record(purpose: str, key_parts: list, fetch: Callable[[], dict]) -> tuple[dict, bool]:
    """Record/replay for non-Anthropic HTTP calls (e.g. Jev), with the same keying and MOCK_LLM rules.

    `key_parts` must be deterministic (no keys, timestamps or temp paths). `fetch` performs the real
    call and returns a JSON-able record; it raises LLMUnavailable when it cannot. Returns (record,
    from_fixture).
    """
    blob = json.dumps(key_parts, sort_keys=True, ensure_ascii=False)
    key = hashlib.sha256(blob.encode()).hexdigest()[:24]
    path = _fixture_path(purpose, key)
    if path.exists():
        return json.loads(path.read_text()), True
    if config.MOCK_LLM:
        raise LLMUnavailable(f"MOCK_LLM=1 and no fixture for {purpose}/{key}")
    rec = fetch()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(rec, indent=2, ensure_ascii=False))
    return rec, False
