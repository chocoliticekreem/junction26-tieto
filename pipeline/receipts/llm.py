"""The single door to every LLM call, with record/replay fixtures.

Every call is keyed by sha256(model, system, user, schema). A real call writes its response to
fixtures/llm/<purpose>/<key>.json. With MOCK_LLM=1 the call is answered from that file only.
Prompts must therefore be deterministic: never put timestamps, run ids or absolute temp paths in them.

When no key and no fixture exist, LLMUnavailable is raised. Callers catch it and record the step
under "not checked" instead of inventing output.
"""

import hashlib
import json
import time
from dataclasses import dataclass

from . import config


class LLMUnavailable(Exception):
    pass


@dataclass
class LLMResult:
    data: dict
    model: str
    input_tokens: int
    output_tokens: int
    ms: int
    from_fixture: bool


def _key(model: str, system: str, user: str, schema: dict) -> str:
    blob = json.dumps([model, system, user, schema], sort_keys=True, ensure_ascii=False)
    return hashlib.sha256(blob.encode()).hexdigest()[:24]


def _fixture_path(purpose: str, key: str):
    return config.FIXTURES_LLM / purpose / f"{key}.json"


def call_json(
    purpose: str, model: str, system: str, user: str, schema: dict, effort: str = "medium"
) -> LLMResult:
    """Ask `model` for a JSON object matching `schema`. `purpose` names the fixture folder."""
    key = _key(model, system, user, schema)
    path = _fixture_path(purpose, key)
    if path.exists():
        rec = json.loads(path.read_text())
        return LLMResult(rec["data"], model, rec.get("input_tokens", 0), rec.get("output_tokens", 0),
                         rec.get("ms", 0), True)
    if config.MOCK_LLM:
        raise LLMUnavailable(f"MOCK_LLM=1 and no fixture for {purpose}/{key}")
    if not config.ANTHROPIC_API_KEY:
        raise LLMUnavailable(f"ANTHROPIC_API_KEY not set and no fixture for {purpose}/{key}")

    import anthropic

    client = anthropic.Anthropic(api_key=config.ANTHROPIC_API_KEY, timeout=300, max_retries=2)
    output_config: dict = {"format": {"type": "json_schema", "schema": schema}}
    if not model.startswith("claude-haiku"):
        output_config["effort"] = effort
    t0 = time.monotonic()
    try:
        with client.messages.stream(
            model=model,
            max_tokens=16000,
            system=system,
            messages=[{"role": "user", "content": user}],
            output_config=output_config,
        ) as stream:
            msg = stream.get_final_message()
    except anthropic.APIConnectionError as e:
        raise LLMUnavailable(f"connection error: {e}") from e
    except anthropic.APIStatusError as e:
        raise LLMUnavailable(f"API error {e.status_code}: {e.message}") from e
    ms = int((time.monotonic() - t0) * 1000)
    if msg.stop_reason == "refusal":
        raise LLMUnavailable(f"model refused ({purpose})")
    text = next((b.text for b in msg.content if b.type == "text"), "")
    try:
        data = json.loads(text)
    except json.JSONDecodeError as e:
        raise LLMUnavailable(f"non-JSON output ({msg.stop_reason}): {text[:200]}") from e

    rec = {
        "purpose": purpose,
        "model": model,
        "data": data,
        "input_tokens": msg.usage.input_tokens,
        "output_tokens": msg.usage.output_tokens,
        "ms": ms,
        "prompt_preview": user[:400],
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(rec, indent=2, ensure_ascii=False))
    return LLMResult(data, model, msg.usage.input_tokens, msg.usage.output_tokens, ms, False)
