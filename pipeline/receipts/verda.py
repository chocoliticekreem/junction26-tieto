"""Verda Inference backend for llm.call_json (LLM_PROVIDER=verda).

Verda serves open-weight models through the OpenAI Chat Completions API at
https://inference.verda.com/v1 (docs.verda.com/inference/openai-compatibility). What differs from
the Anthropic path, and why:

- Structured output: Verda documents JSON mode (`response_format: json_object`), not JSON-schema
  mode. So the schema goes into the system prompt, the reply is parsed and validated here against
  the same schema, and one repair turn is sent with the validation errors. Still invalid ->
  LLMUnavailable: the step is reported as not checked, never invented.
- Effort: mapped to `reasoning_effort` (models accept none/low/high/max). Router calls run with
  reasoning off. Reasoning counts as output tokens, so max_tokens is doubled when it is on.
- Streaming: a non-streamed request times out after 60 s, a streamed one after 20 min; lens calls
  write long C# files, so every call streams and asks for usage in the last chunk.
- Spend guard: every HTTP call reserves its worst case (estimated input + max_tokens at list
  price) in config.VERDA_SPEND_FILE before it is sent, and settles to the reported usage after.
  A call that would take the total over config.VERDA_MAX_EUR raises LLMUnavailable("budget ...").
"""

import fcntl
import json
import os
import random
import re
import time
import uuid
from contextlib import contextmanager
from datetime import datetime, timezone

import httpx

from . import config, llm

EFFORT_MAP = {"low": "low", "medium": "low", "high": "high", "xhigh": "max", "max": "max"}
CHARS_PER_TOKEN = 2.3  # same calibration as estimate.py
MAX_RETRIES = 3

FORMAT_RULES = ("\n\nOutput format: reply with exactly one JSON object and nothing else: no prose, "
                "no markdown code fences. It must validate against this JSON Schema (every "
                "required key present, no other keys, enum values spelled exactly):\n")


def reasoning_effort(purpose: str, effort: str) -> str:
    return "none" if purpose.startswith("router") else EFFORT_MAP.get(effort, "low")


def make_client() -> httpx.Client:
    return httpx.Client(timeout=httpx.Timeout(connect=30, read=600, write=60, pool=30))


# ---------------------------------------------------------------- schema check


def schema_errors(data, schema: dict, path: str = "$") -> list[str]:
    """The subset of JSON Schema the lens, reader and router schemas use: type, properties,
    required, additionalProperties: false, enum, items."""
    errs: list[str] = []
    t = schema.get("type")
    types = {"object": dict, "array": list, "string": str, "boolean": bool}
    if t in types and not isinstance(data, types[t]):
        return [f"{path}: expected {t}, got {type(data).__name__}"]
    if t == "integer" and (isinstance(data, bool) or not isinstance(data, int)):
        return [f"{path}: expected integer, got {data!r}"]
    if t == "number" and (isinstance(data, bool) or not isinstance(data, (int, float))):
        return [f"{path}: expected number, got {data!r}"]
    if "enum" in schema and data not in schema["enum"]:
        errs.append(f"{path}: {data!r} not one of {schema['enum']}")
    if t == "object":
        props = schema.get("properties", {})
        errs += [f"{path}: missing key {k!r}" for k in schema.get("required", []) if k not in data]
        if schema.get("additionalProperties") is False:
            errs += [f"{path}: unexpected key {k!r}" for k in data if k not in props]
        for k, sub in props.items():
            if k in data:
                errs += schema_errors(data[k], sub, f"{path}.{k}")
    if t == "array" and "items" in schema:
        for i, item in enumerate(data):
            errs += schema_errors(item, schema["items"], f"{path}[{i}]")
    return errs


def parse_reply(text: str, schema: dict) -> tuple[dict | None, list[str]]:
    body = text.strip()
    fence = re.fullmatch(r"```(?:json)?\s*(.*?)\s*```", body, re.S)
    if fence:
        body = fence.group(1)
    try:
        data = json.loads(body)
    except json.JSONDecodeError as e:
        return None, [f"not valid JSON: {e}"]
    errs = schema_errors(data, schema)
    return (data, []) if not errs else (None, errs)


# ---------------------------------------------------------------- spend ledger


@contextmanager
def _locked_ledger():
    path = config.VERDA_SPEND_FILE
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path.with_suffix(".lock"), "a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            led = json.loads(path.read_text()) if path.exists() else {}
            led.setdefault("total_eur", 0.0)
            led.setdefault("calls", [])
            led.setdefault("reservations", {})
            yield led
            led["max_eur"] = config.VERDA_MAX_EUR
            tmp = path.with_suffix(".tmp")
            tmp.write_text(json.dumps(led, indent=1))
            tmp.replace(path)
        finally:
            fcntl.flock(lock, fcntl.LOCK_UN)


def _alive(pid: int) -> bool:
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def spent_eur() -> float:
    path = config.VERDA_SPEND_FILE
    return json.loads(path.read_text()).get("total_eur", 0.0) if path.exists() else 0.0


def reserve(worst_eur: float) -> str:
    """Hold `worst_eur` against the budget; raise LLMUnavailable when it does not fit."""
    with _locked_ledger() as led:
        live = {k: v for k, v in led["reservations"].items() if _alive(v["pid"])}
        held = sum(v["eur"] for v in live.values())
        if led["total_eur"] + held + worst_eur > config.VERDA_MAX_EUR:
            led["reservations"] = live
            raise llm.LLMUnavailable(
                f"budget: Verda spend {led['total_eur']:.4f} EUR + {held:.4f} held + this call's "
                f"worst case {worst_eur:.4f} would pass VERDA_MAX_EUR={config.VERDA_MAX_EUR:g}")
        rid = uuid.uuid4().hex[:12]
        live[rid] = {"pid": os.getpid(), "eur": round(worst_eur, 6)}
        led["reservations"] = live
        return rid


def settle(rid: str, purpose: str, model: str, input_tokens: int, output_tokens: int,
           status: str) -> float:
    eur = llm.cost_eur(model, input_tokens, output_tokens)
    with _locked_ledger() as led:
        led["reservations"].pop(rid, None)
        led["total_eur"] = round(led["total_eur"] + eur, 6)
        led["calls"].append({
            "at": datetime.now(timezone.utc).isoformat(timespec="seconds"), "purpose": purpose,
            "model": model, "input_tokens": input_tokens, "output_tokens": output_tokens,
            "cost_usd": llm.cost_usd(model, input_tokens, output_tokens), "cost_eur": eur,
            "status": status})
    return eur


# ---------------------------------------------------------------- HTTP


def _error(resp: httpx.Response) -> str:
    resp.read()
    try:
        err = resp.json().get("error", {})
        return f"{err.get('code') or err.get('type') or ''}: {err.get('message', '')}".strip(": ")
    except (ValueError, AttributeError):
        return resp.text[:300]


def _stream(client: httpx.Client, body: dict) -> tuple[str, str, int, int]:
    """One streamed chat completion -> (content, finish_reason, input_tokens, output_tokens)."""
    url = f"{config.VERDA_BASE_URL}/v1/chat/completions"
    headers = {"Authorization": f"Bearer {config.VERDA_API_KEY}"}
    for attempt in range(MAX_RETRIES + 1):
        with client.stream("POST", url, json=body, headers=headers) as resp:
            if resp.status_code == 429 or resp.status_code >= 500:
                detail = _error(resp)
                if attempt == MAX_RETRIES:
                    raise llm.LLMUnavailable(f"Verda API error {resp.status_code}: {detail}")
                wait = float(resp.headers.get("retry-after") or 2 ** attempt)
                time.sleep(wait + random.random())
                continue
            if resp.status_code != 200:
                raise llm.LLMUnavailable(f"Verda API error {resp.status_code}: {_error(resp)}")
            parts: list[str] = []
            finish, usage = "", {}
            for line in resp.iter_lines():
                if not line.startswith("data:"):
                    continue
                payload = line[5:].strip()
                if payload == "[DONE]":
                    break
                chunk = json.loads(payload)
                usage = chunk.get("usage") or usage
                for ch in chunk.get("choices") or []:
                    parts.append((ch.get("delta") or {}).get("content") or "")
                    finish = ch.get("finish_reason") or finish
            return ("".join(parts), finish, int(usage.get("prompt_tokens", 0)),
                    int(usage.get("completion_tokens", 0)))
    raise AssertionError("unreachable")


def _one_call(client, purpose: str, model: str, messages: list[dict], max_tokens: int,
              effort: str) -> tuple[str, str, int, int]:
    body = {"model": model, "messages": messages, "max_tokens": max_tokens,
            "response_format": {"type": "json_object"}, "reasoning_effort": effort,
            "stream": True, "stream_options": {"include_usage": True}}
    chars = sum(len(m["content"]) for m in messages)
    worst = llm.cost_eur(model, round(chars / CHARS_PER_TOKEN), max_tokens)
    rid = reserve(worst)
    in_tok = out_tok = 0
    status = "error"
    try:
        text, finish, in_tok, out_tok = _stream(client, body)
        status = finish or "ok"
        return text, finish, in_tok, out_tok
    except (httpx.HTTPError, ValueError) as e:
        raise llm.LLMUnavailable(f"Verda connection error ({purpose}): {e}") from e
    finally:
        settle(rid, purpose, model, in_tok, out_tok, status)


def call(purpose: str, key: str, model: str, system: str, user: str, schema: dict, effort: str,
         max_tokens: int, client: httpx.Client | None = None) -> "llm.LLMResult":
    if not config.VERDA_API_KEY:
        raise llm.LLMUnavailable(f"VERDA_API_KEY not set and no fixture for {purpose}/{key}")
    reff = reasoning_effort(purpose, effort)
    budget = max_tokens * 2 if reff != "none" else max_tokens
    messages = [{"role": "system", "content": system + FORMAT_RULES + json.dumps(schema)},
                {"role": "user", "content": user}]
    own = client is None
    client = client or make_client()
    t0 = time.monotonic()
    in_total = out_total = 0
    try:
        text, finish, i, o = _one_call(client, purpose, model, messages, budget, reff)
        in_total, out_total = i, o
        data, errs = parse_reply(text, schema)
        repaired = False
        if data is None:
            if finish == "length":
                raise llm.LLMUnavailable(f"Verda output cut off at max_tokens={budget} ({purpose})")
            messages += [{"role": "assistant", "content": text},
                         {"role": "user", "content": "Your reply did not validate:\n- "
                          + "\n- ".join(errs[:20]) + "\nReply again with the corrected JSON object "
                          "only, same content otherwise."}]
            text, finish, i, o = _one_call(client, purpose, model, messages, budget, reff)
            in_total, out_total = in_total + i, out_total + o
            data, errs = parse_reply(text, schema)
            repaired = True
            if data is None:
                raise llm.LLMUnavailable(f"Verda output invalid after one repair ({purpose}): "
                                         + "; ".join(errs[:5]))
    finally:
        if own:
            client.close()
    ms = int((time.monotonic() - t0) * 1000)
    llm._write_fixture(purpose, key, model, data, in_total, out_total, ms, user, provider="verda",
                       reasoning_effort=reff, repaired=repaired,
                       cost_eur=llm.cost_eur(model, in_total, out_total))
    return llm.LLMResult(data, model, in_total, out_total, ms, False, "verda")
