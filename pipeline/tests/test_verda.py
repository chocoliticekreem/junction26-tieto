"""Verda provider: fixture-key composition, provider selection, JSON validation with one repair,
the spend guard, run ids, and the batch fallback. HTTP goes to a fake transport; nothing is sent."""

import hashlib
import json

import httpx
import pytest

from receipts import bench, config, estimate, llm, metrics, verda

SCHEMA = {"type": "object", "properties": {"ok": {"type": "boolean"},
                                           "kind": {"type": "string", "enum": ["a", "b"]}},
          "required": ["ok", "kind"], "additionalProperties": False}
MODEL = "deepseek-4-1-flash-1017"


@pytest.fixture
def iso(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "FIXTURES_LLM", tmp_path / "fixtures")
    monkeypatch.setattr(config, "VERDA_SPEND_FILE", tmp_path / "work" / "verda-spend.json")
    monkeypatch.setattr(config, "VERDA_API_KEY", "test-key")
    monkeypatch.setattr(config, "VERDA_MAX_EUR", 40.0)
    monkeypatch.setattr(config, "VERDA_EUR_PER_USD", 1.0)
    monkeypatch.setattr(config, "LLM_PROVIDER", "verda")
    monkeypatch.setattr(config, "MOCK_LLM", False)
    monkeypatch.setattr(config, "LLM_MODE", "")
    return tmp_path


def sse(content: str, prompt_tokens=100, completion_tokens=50, finish="stop") -> bytes:
    chunks = [{"choices": [{"delta": {"content": content[:5]}}]},
              {"choices": [{"delta": {"content": content[5:]}, "finish_reason": finish}]},
              {"choices": [], "usage": {"prompt_tokens": prompt_tokens,
                                        "completion_tokens": completion_tokens}}]
    return b"".join(f"data: {json.dumps(c)}\n\n".encode() for c in chunks) + b"data: [DONE]\n\n"


def fake_client(monkeypatch, replies: list, seen: list):
    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(json.loads(request.content))
        status, body = replies.pop(0)
        return httpx.Response(status, content=body,
                              headers={"content-type": "text/event-stream"})

    monkeypatch.setattr(verda, "make_client",
                        lambda: httpx.Client(transport=httpx.MockTransport(handler)))


# ---------------------------------------------------------------- key composition


def test_anthropic_key_unchanged_and_verda_key_distinct():
    old = hashlib.sha256(json.dumps(["claude-opus-5-5", "s", "u", SCHEMA], sort_keys=True,
                                    ensure_ascii=False).encode()).hexdigest()[:24]
    assert llm.fixture_key("claude-opus-5-5", "s", "u", SCHEMA, "high", "high",
                           provider="anthropic") == old
    v = llm.fixture_key("claude-opus-5-5", "s", "u", SCHEMA, "high", "high", provider="verda")
    assert v != old
    assert llm.fixture_key(MODEL, "s", "u", SCHEMA, "high", "high", provider="verda") != v


def test_anthropic_fixture_never_replays_under_verda(iso, monkeypatch):
    monkeypatch.setattr(config, "MOCK_LLM", True)
    key = llm.fixture_key(MODEL, "s", "u", SCHEMA, "medium", provider="anthropic")
    path = config.FIXTURES_LLM / "p" / f"{key}.json"
    path.parent.mkdir(parents=True)
    path.write_text(json.dumps({"data": {"ok": True, "kind": "a"}}))
    with pytest.raises(llm.LLMUnavailable, match="MOCK_LLM"):
        llm.call_json("p", MODEL, "s", "u", SCHEMA)
    monkeypatch.setattr(config, "LLM_PROVIDER", "anthropic")
    assert llm.call_json("p", MODEL, "s", "u", SCHEMA).data == {"ok": True, "kind": "a"}


# ---------------------------------------------------------------- provider selection


def test_call_json_dispatches_to_verda_and_replays_its_fixture(iso, monkeypatch):
    seen: list = []
    fake_client(monkeypatch, [(200, sse('{"ok": true, "kind": "b"}'))], seen)
    res = llm.call_json("lens-x", MODEL, "sys", "user", SCHEMA, effort="medium")
    assert (res.data, res.provider, res.from_fixture) == ({"ok": True, "kind": "b"}, "verda", False)
    assert (res.input_tokens, res.output_tokens) == (100, 50)
    body = seen[0]
    assert body["model"] == MODEL and body["stream"] is True
    assert body["response_format"] == {"type": "json_object"}
    assert body["reasoning_effort"] == "low" and body["max_tokens"] == 32000
    assert '"enum": ["a", "b"]' in body["messages"][0]["content"]
    again = llm.call_json("lens-x", MODEL, "sys", "user", SCHEMA, effort="medium")
    assert again.from_fixture and again.provider == "verda" and len(seen) == 1


def test_router_runs_without_reasoning():
    assert verda.reasoning_effort("router-llm", "medium") == "none"
    assert verda.reasoning_effort("lens-logic-edge", "high") == "high"
    assert verda.reasoning_effort("readers", "medium") == "low"


def test_missing_key_is_declared(iso, monkeypatch):
    monkeypatch.setattr(config, "VERDA_API_KEY", "")
    with pytest.raises(llm.LLMUnavailable, match="VERDA_API_KEY"):
        llm.call_json("p", MODEL, "s", "u", SCHEMA)


def test_api_error_is_declared(iso, monkeypatch):
    seen: list = []
    err = json.dumps({"error": {"code": "subscription_required", "message": "not subscribed"}})
    fake_client(monkeypatch, [(403, err.encode())], seen)
    with pytest.raises(llm.LLMUnavailable, match="403: subscription_required"):
        llm.call_json("p", MODEL, "s", "u", SCHEMA)
    assert verda.spent_eur() == 0.0


# ---------------------------------------------------------------- validation and repair


def test_schema_errors():
    assert verda.schema_errors({"ok": True, "kind": "a"}, SCHEMA) == []
    errs = verda.schema_errors({"ok": "yes", "kind": "c", "x": 1}, SCHEMA)
    assert any("expected boolean" in e for e in errs)
    assert any("not one of" in e for e in errs) and any("unexpected key 'x'" in e for e in errs)
    assert verda.schema_errors({"ok": True}, SCHEMA) == ["$: missing key 'kind'"]
    assert verda.parse_reply('```json\n{"ok": false, "kind": "a"}\n```', SCHEMA)[0] == {
        "ok": False, "kind": "a"}


def test_invalid_reply_is_repaired_once(iso, monkeypatch):
    seen: list = []
    fake_client(monkeypatch, [(200, sse('{"ok": true}')), (200, sse('{"ok": true, "kind": "a"}'))],
                seen)
    res = llm.call_json("p", MODEL, "s", "u", SCHEMA)
    assert res.data == {"ok": True, "kind": "a"} and len(seen) == 2
    assert (res.input_tokens, res.output_tokens) == (200, 100)
    repair = seen[1]["messages"]
    assert repair[2] == {"role": "assistant", "content": '{"ok": true}'}
    assert "missing key 'kind'" in repair[3]["content"]
    fx = next((config.FIXTURES_LLM / "p").glob("*.json"))
    assert json.loads(fx.read_text())["repaired"] is True
    assert len(json.loads(config.VERDA_SPEND_FILE.read_text())["calls"]) == 2


def test_still_invalid_after_repair_raises(iso, monkeypatch):
    seen: list = []
    fake_client(monkeypatch, [(200, sse("not json")), (200, sse('{"ok": 1, "kind": "a"}'))], seen)
    with pytest.raises(llm.LLMUnavailable, match="invalid after one repair"):
        llm.call_json("p", MODEL, "s", "u", SCHEMA)
    assert not (config.FIXTURES_LLM / "p").exists()


def test_cut_off_reply_is_not_repaired(iso, monkeypatch):
    seen: list = []
    fake_client(monkeypatch, [(200, sse('{"ok": tr', finish="length"))], seen)
    with pytest.raises(llm.LLMUnavailable, match="cut off"):
        llm.call_json("p", MODEL, "s", "u", SCHEMA)
    assert len(seen) == 1


# ---------------------------------------------------------------- spend guard


def test_spend_is_recorded_and_budget_refuses(iso, monkeypatch):
    monkeypatch.setattr(config, "VERDA_MAX_EUR", 0.05)
    seen: list = []
    fake_client(monkeypatch, [(200, sse('{"ok": true, "kind": "a"}', 1000, 2000))], seen)
    llm.call_json("p", MODEL, "s", "u1", SCHEMA)
    led = json.loads(config.VERDA_SPEND_FILE.read_text())
    expected = llm.cost_eur(MODEL, 1000, 2000)
    assert led["total_eur"] == expected and led["reservations"] == {}
    monkeypatch.setattr(config, "VERDA_MAX_EUR", expected + 0.001)  # less than a worst case
    with pytest.raises(llm.LLMUnavailable, match="budget"):
        llm.call_json("p", MODEL, "s", "u2", SCHEMA)
    assert len(seen) == 1  # refused before any request


def test_dead_reservations_are_released(iso):
    config.VERDA_SPEND_FILE.parent.mkdir(parents=True)
    config.VERDA_SPEND_FILE.write_text(json.dumps(
        {"total_eur": 1.0, "calls": [], "reservations": {"x": {"pid": 2 ** 22 + 7, "eur": 39.0}}}))
    rid = verda.reserve(5.0)
    led = json.loads(config.VERDA_SPEND_FILE.read_text())
    assert list(led["reservations"]) == [rid]


# ---------------------------------------------------------------- run ids, batch, estimate


def test_run_id(monkeypatch):
    monkeypatch.setattr(config, "RUN_SUFFIX", "@verda")
    assert config.run_id("ju26-issue-2283") == "ju26-issue-2283@verda"
    assert config.run_id("ju26-issue-2297-r2__A") == "ju26-issue-2297-r2@verda__A"
    assert config.run_id("ju26-issue-2283@verda") == "ju26-issue-2283@verda"
    monkeypatch.setattr(config, "RUN_SUFFIX", "")
    assert config.run_id("ju26-issue-2283") == "ju26-issue-2283"


def test_bench_dry_run_refused_on_verda(monkeypatch, capsys):
    monkeypatch.setattr(config, "LLM_PROVIDER", "verda")
    assert bench.run(dry_run=True) == 2
    assert "no batch API" in capsys.readouterr().out


def test_projection_reprices_recorded_tokens(tmp_path):
    run = tmp_path / "c" / "20261114T000000Z"
    run.mkdir(parents=True)
    (run / "receipt.json").write_text("{}")
    (run / "lenses.json").write_text(json.dumps({"lenses": [
        {"lens": "logic-edge", "input_tokens": 1000, "output_tokens": 500},
        {"lens": "security", "input_tokens": 2000, "output_tokens": 100}]}))
    models = {"deep": "kimi-k3-1-pro", "lens": MODEL, "router": MODEL}
    p = estimate.project_verda([estimate.latest_dir("c", tmp_path)], models, {"deep": 2.0})
    assert p["roles"]["deep"]["output_tokens"] == 1000
    assert p["roles"]["deep"]["usd"] == llm.cost_usd("kimi-k3-1-pro", 1000, 1000)
    assert p["roles"]["lens"]["usd"] == llm.cost_usd(MODEL, 2000, 100)


def test_comparison_shape(tmp_path):
    table = metrics.provider_comparison([], tmp_path)
    assert table["columns"][:4] == ["change_id", "kind", "verdict_anthropic", "verdict_verda"]
    assert [r[0] for r in table["rows"]] == ["overall: Tieto changes (0 with both)",
                                             "overall: bench cold (0 with both)"]
    assert "final pipeline" in table["note"] and "compare models only" in table["note"]
