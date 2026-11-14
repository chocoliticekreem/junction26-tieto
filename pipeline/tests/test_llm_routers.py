import json

import pytest

from receipts import config, llm
from receipts.models import Hunk
from receipts.routers import jev
from receipts.routers.jev import JevRouter, JevUnavailable, build_state, parse_answers
from receipts.routers.llm_router import LLMRouter, RouterOutputError, parse_route

HUNK = Hunk(id="src/A.cs#1", file="src/A.cs", kind="source", old_start=1, old_lines=1, new_start=1,
            new_lines=1, header="class A", text="-a\n+b")


@pytest.fixture
def isolated(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "FIXTURES_LLM", tmp_path)
    monkeypatch.setattr(config, "ANTHROPIC_API_KEY", "")
    monkeypatch.setattr(config, "JEV_API_KEY", "")
    monkeypatch.setattr(config, "MOCK_LLM", False)
    return tmp_path


# --- LLMRouter schema validation -------------------------------------------------------------


def test_llm_route_accepts_valid_output():
    r = parse_route({"risk": 6, "category": "compatibility", "confidence": 0.8, "reason": "x"}, "h#1", "llm", 10, 5)
    assert (r.risk, r.category, r.router, r.cost_tokens) == (6, "compatibility", "llm", 10)


@pytest.mark.parametrize("bad", [
    {"risk": 11, "category": "correctness", "confidence": 0.5},
    {"risk": 0, "category": "correctness", "confidence": 0.5},
    {"risk": 5, "category": "style", "confidence": 0.5},
    {"risk": 5, "category": "correctness", "confidence": 1.5},
    {"risk": "high", "category": "correctness", "confidence": 0.5},
    {"risk": 5.5, "category": "correctness", "confidence": 0.5},
    {"risk": True, "category": "correctness", "confidence": 0.5},
    {"category": "correctness", "confidence": 0.5},
    ["not", "an", "object"],
])
def test_llm_route_rejects_bad_output(bad):
    with pytest.raises(RouterOutputError):
        parse_route(bad, "h#1", "llm", 0, 0)


def test_llm_router_bad_fixture_output_is_rejected(isolated):
    """A recorded (or live) response outside the schema raises instead of being coerced."""
    router = LLMRouter()
    seen = {}

    def fake_call_json(purpose, model, system, user, schema, **kw):
        seen["purpose"], seen["user"] = purpose, user
        return llm.LLMResult({"risk": 42, "category": "correctness", "confidence": 0.5}, model, 1, 1, 1, True)

    orig = llm.call_json
    llm.call_json = fake_call_json
    try:
        with pytest.raises(RouterOutputError):
            router.route(HUNK, "REQ text", {})
    finally:
        llm.call_json = orig
    assert seen["purpose"] == "router-llm" and "REQ text" in seen["user"] and "+b" in seen["user"]


def test_llm_router_mock_without_fixture_raises_unavailable(isolated, monkeypatch):
    monkeypatch.setattr(config, "MOCK_LLM", True)
    with pytest.raises(llm.LLMUnavailable):
        LLMRouter().route(HUNK, "REQ", {})


# --- JevRouter ---------------------------------------------------------------------------------


def _jev_response(probs=None, score=4.93, choice="correctness", usage=True):
    resp = {
        "model": "jev-1.14.0",
        "answers": {
            "risk": {"type": "score", "score": score, "confidence": 0.56, "legend": {},
                     "probabilities": probs if probs is not None else {"4": 0.2, "6": 0.69, "0": 0.11}},
            "category": {"type": "choice", "choice": choice, "confidence": 0.9, "probabilities": {choice: 0.9}},
        },
    }
    if usage:
        resp["usage"] = {"input_tokens": 2661, "output_tokens": 79}
    return resp


def test_jev_parse_uses_most_likely_level_and_min_confidence():
    r = parse_answers(_jev_response(), "h#1", 200, 999)
    assert r.risk == 7 and r.category == "correctness" and r.confidence == 0.56
    assert r.cost_tokens == 2740 and "estimated" not in r.reason


def test_jev_parse_estimates_tokens_when_usage_missing():
    r = parse_answers(_jev_response(usage=False), "h#1", 200, 123)
    assert r.cost_tokens == 123 and "estimated" in r.reason


@pytest.mark.parametrize("resp", [
    {"model": "x"},
    _jev_response(choice="style"),
    _jev_response(score=12),
    {"model": "x", "answers": {"risk": {"type": "choice"}, "category": {"type": "choice", "choice": "docs"}}},
    _jev_response(probs={"high": 0.9}),
])
def test_jev_parse_rejects_bad_responses(resp):
    with pytest.raises(RouterOutputError):
        parse_answers(resp, "h#1", 0, 0)


def test_jev_state_truncates_with_marker():
    big = HUNK.model_copy(update={"text": "+x\n" * 100_000})
    state, truncated = build_state(big, "REQ")
    assert truncated and "truncated by receipts" in state["diff"]
    assert len(state["diff"]) <= jev.STATE_CHAR_BUDGET


def test_jev_without_key_or_fixture_raises_unavailable(isolated):
    with pytest.raises(JevUnavailable):
        JevRouter().route(HUNK, "REQ", {})


def test_jev_mock_without_fixture_raises_unavailable(isolated, monkeypatch):
    monkeypatch.setattr(config, "MOCK_LLM", True)
    monkeypatch.setattr(config, "JEV_API_KEY", "set-but-unused")
    with pytest.raises(llm.LLMUnavailable):
        JevRouter().route(HUNK, "REQ", {})


def test_jev_records_then_replays(isolated, monkeypatch):
    calls = []

    def fake_post(state):
        calls.append(state)
        return {"response": _jev_response(), "ms": 200, "request_id": "req_x", "attempts": 1}

    monkeypatch.setattr(jev, "_post", fake_post)
    first = JevRouter().route(HUNK, "REQ", {"area": "type-collector"})
    monkeypatch.setattr(config, "MOCK_LLM", True)
    second = JevRouter().route(HUNK, "REQ", {"area": "type-collector"})
    assert len(calls) == 1 and first == second
    files = list((isolated / "router-jev").glob("*.json"))
    assert len(files) == 1 and "Authorization" not in files[0].read_text()
    assert json.loads(files[0].read_text())["response"]["model"] == "jev-1.14.0"


class _Resp:
    def __init__(self, status, body=None, headers=None):
        self.status_code, self._body, self.headers = status, body or {}, headers or {}
        self.text = json.dumps(self._body)

    def json(self):
        return self._body


class _Client:
    def __init__(self, responses):
        self.responses, self.calls = list(responses), 0

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False

    def post(self, *a, **kw):
        self.calls += 1
        return self.responses.pop(0)


def test_jev_post_retries_529_then_succeeds(monkeypatch):
    client = _Client([_Resp(529), _Resp(429, headers={"retry-after": "0"}), _Resp(200, _jev_response())])
    monkeypatch.setattr(config, "JEV_API_KEY", "k")
    monkeypatch.setattr(jev.httpx, "Client", lambda **kw: client)
    monkeypatch.setattr(jev.time, "sleep", lambda s: None)
    rec = jev._post({"task": "t", "diff": "d"})
    assert client.calls == 3 and rec["attempts"] == 3


def test_jev_post_does_not_retry_401(monkeypatch):
    client = _Client([_Resp(401, {"detail": "bad key"})])
    monkeypatch.setattr(config, "JEV_API_KEY", "k")
    monkeypatch.setattr(jev.httpx, "Client", lambda **kw: client)
    with pytest.raises(JevUnavailable, match="HTTP 401"):
        jev._post({"task": "t", "diff": "d"})
    assert client.calls == 1


def test_jev_post_gives_up_after_two_retries(monkeypatch):
    client = _Client([_Resp(503), _Resp(503), _Resp(503)])
    monkeypatch.setattr(config, "JEV_API_KEY", "k")
    monkeypatch.setattr(jev.httpx, "Client", lambda **kw: client)
    monkeypatch.setattr(jev.time, "sleep", lambda s: None)
    with pytest.raises(JevUnavailable, match="HTTP 503"):
        jev._post({"task": "t", "diff": "d"})
    assert client.calls == 3
