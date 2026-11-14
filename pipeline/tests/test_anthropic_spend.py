"""Anthropic spend cap: live calls and batches reserve their worst case before anything is sent,
settle to reported usage, and are refused past ANTHROPIC_MAX_USD. Fake clients only."""

import json
from types import SimpleNamespace

import pytest
from test_batch import SCHEMA, _ok, _queue, fake_client

from receipts import anthropic_spend, config, llm


@pytest.fixture
def iso(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "FIXTURES_LLM", tmp_path / "fixtures")
    monkeypatch.setattr(config, "BATCH_DIR", tmp_path / "batch")
    monkeypatch.setattr(config, "ANTHROPIC_API_KEY", "test-key")
    monkeypatch.setattr(config, "LLM_PROVIDER", "anthropic")
    monkeypatch.setattr(config, "MOCK_LLM", False)
    monkeypatch.setattr(config, "LLM_MODE", "")
    return tmp_path


def ledger() -> dict:
    return json.loads(config.ANTHROPIC_SPEND_FILE.read_text())


class FakeStream:
    def __init__(self, msg):
        self.msg = msg

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False

    def get_final_message(self):
        return self.msg


def fake_anthropic(monkeypatch, stream):
    import anthropic

    monkeypatch.setattr(anthropic, "Anthropic", lambda **kw: SimpleNamespace(
        messages=SimpleNamespace(stream=stream)))


def _msg(text='{"ok": true}', inp=1000, out=2000):
    return SimpleNamespace(stop_reason="end_turn", content=[SimpleNamespace(type="text", text=text)],
                           usage=SimpleNamespace(input_tokens=inp, output_tokens=out))


def test_worst_case_counts_max_tokens_and_batch_halves():
    params = llm.request_params("claude-opus-5-5", "s" * 230, "u" * 2300, SCHEMA, "high", 10000)
    tokens_in = round(anthropic_spend.request_chars(params) / anthropic_spend.CHARS_PER_TOKEN)
    live = anthropic_spend.worst_usd(params)
    assert live == llm.cost_usd("claude-opus-5-5", tokens_in, 10000)
    assert anthropic_spend.worst_usd(params, batch=True) == round(live / 2, 6)


def test_live_call_settles_reported_usage(iso, monkeypatch):
    sent: list = []
    fake_anthropic(monkeypatch, lambda **p: sent.append(p) or FakeStream(_msg()))
    res = llm.call_json("p", "claude-sonnet-5-5", "s", "u", SCHEMA, max_tokens=4000)
    assert res.data == {"ok": True} and len(sent) == 1
    led = ledger()
    assert led["total_usd"] == llm.cost_usd("claude-sonnet-5-5", 1000, 2000)
    assert led["reservations"] == {} and led["calls"][0]["output_tokens"] == 2000


def test_live_call_refused_before_sending(iso, monkeypatch):
    sent: list = []
    fake_anthropic(monkeypatch, lambda **p: sent.append(p) or FakeStream(_msg()))
    monkeypatch.setattr(config, "ANTHROPIC_MAX_USD", 0.01)  # below 4000 Sonnet output tokens
    with pytest.raises(llm.LLMUnavailable, match="budget"):
        llm.call_json("p", "claude-sonnet-5-5", "s", "u", SCHEMA, max_tokens=4000)
    assert sent == []


def test_failed_live_call_releases_reservation(iso, monkeypatch):
    import anthropic
    import httpx

    def boom(**params):
        raise anthropic.APIConnectionError(request=httpx.Request("POST", "https://x"))

    fake_anthropic(monkeypatch, boom)
    with pytest.raises(llm.LLMUnavailable, match="connection"):
        llm.call_json("p", "claude-sonnet-5-5", "s", "u", SCHEMA, max_tokens=4000)
    led = ledger()
    assert led["reservations"] == {} and led["total_usd"] == 0.0


def test_batch_reserves_then_settles_at_batch_price(iso):
    reqs = _queue(("p", "u1", "claude-opus-5-5"), ("p", "u2", "claude-sonnet-5-5"))
    k1 = next(k for k, e in reqs.items() if e["params"]["messages"][0]["content"] == "u1")
    k2 = next(k for k in reqs if k != k1)
    client = fake_client([_ok(k1, '{"ok": true}', inp=1000, out=2000),
                          _ok(k2, "not json", inp=500, out=100)])
    real_retrieve = client.messages.batches.retrieve
    held_while_running = []

    def retrieve(batch_id):
        held_while_running.append(ledger()["reservations"])
        return real_retrieve(batch_id)

    client.messages.batches.retrieve = retrieve
    rep = llm.run_batch(reqs, client=client, poll_s=0, sleep=lambda s: None, log=lambda s: None)
    (held,) = held_while_running[0].values()
    assert held["batch_id"] == "msgbatch_1"
    assert held["usd"] == round(sum(anthropic_spend.worst_usd(e["params"], batch=True)
                                    for e in reqs.values()), 6)
    billed = (llm.cost_usd("claude-opus-5-5", 1000, 2000)
              + llm.cost_usd("claude-sonnet-5-5", 500, 100)) * 0.5
    led = ledger()
    assert led["reservations"] == {} and led["total_usd"] == pytest.approx(billed)
    assert rep.succeeded == 1  # the non-JSON reply is billed but is not a fixture


def test_batch_refused_before_submission(iso, monkeypatch):
    monkeypatch.setattr(config, "ANTHROPIC_MAX_USD", 0.0001)
    client = fake_client([])
    with pytest.raises(llm.LLMUnavailable, match="budget"):
        llm.run_batch(_queue(("p", "u", "claude-opus-5-5")), client=client, log=lambda s: None)
    assert client.messages.batches.created == []


def test_batch_reservation_survives_a_dead_process(iso):
    config.ANTHROPIC_SPEND_FILE.write_text(json.dumps({"total_usd": 0.0, "calls": [],
                                                       "reservations": {
        "live": {"pid": 2 ** 22 + 7, "usd": 15.0},
        "batch": {"pid": 2 ** 22 + 7, "usd": 15.0, "batch_id": "msgbatch_9"}}}))
    assert anthropic_spend.available_usd() == pytest.approx(5.0)
    assert set(ledger()["reservations"]) == {"batch"}
    assert anthropic_spend.batch_reservation("msgbatch_9") == "batch"


def test_run_batches_chunks_to_fit_budget(iso, monkeypatch):
    reqs = _queue(*[("p", f"u{i}", "claude-opus-5-5") for i in range(4)])
    one = anthropic_spend.worst_usd(next(iter(reqs.values()))["params"], batch=True)
    monkeypatch.setattr(config, "ANTHROPIC_MAX_USD", one * 2.5)  # two fit at a time
    chunks = []

    def fake_run_batch(chunk, client=None, log=None):
        chunks.append(sorted(chunk))
        rid = anthropic_spend.reserve(sum(anthropic_spend.worst_usd(e["params"], batch=True)
                                          for e in chunk.values()), "t")
        anthropic_spend.settle(rid, "t", one * 0.1 * len(chunk))
        return llm.BatchReport(batch_id=f"b{len(chunks)}", submitted=len(chunk),
                               succeeded=len(chunk))

    monkeypatch.setattr(llm, "run_batch", fake_run_batch)
    rep = llm.run_batches(reqs, log=lambda s: None)
    assert [len(c) for c in chunks] == [2, 2]
    assert rep.submitted == rep.succeeded == 4 and rep.batch_id == "b1,b2"


def test_run_batches_reports_what_never_fits(iso, monkeypatch):
    reqs = _queue(("p", "u", "claude-opus-5-5"))
    monkeypatch.setattr(config, "ANTHROPIC_MAX_USD", 0.0001)
    monkeypatch.setattr(llm, "run_batch", lambda *a, **k: pytest.fail("must not submit"))
    rep = llm.run_batches(reqs, log=lambda s: None)
    assert rep.submitted == 0 and "budget" in rep.errored[0]["reason"]
    assert llm.failed_reason(next(iter(reqs))).startswith("budget")
