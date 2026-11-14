"""Batch bench: effort config, fixture-key compatibility, collect/replay modes, Message Batches
submission with a fake client, cost estimate, and the collect-batch round loop."""

import dataclasses
import hashlib
import json
from types import SimpleNamespace

import pytest

from receipts import bench, config, estimate, llm
from receipts.lenses import LENSES, readers, run_all_lenses
from receipts.lenses.base import run_lens
from receipts.models import Hunk, RouteResult
from receipts.routers import combine

SCHEMA = {"type": "object", "properties": {"ok": {"type": "boolean"}}, "required": ["ok"]}


@pytest.fixture
def iso(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "FIXTURES_LLM", tmp_path / "fixtures")
    monkeypatch.setattr(config, "BATCH_DIR", tmp_path / "batch")
    monkeypatch.setattr(config, "ANTHROPIC_API_KEY", "")
    monkeypatch.setattr(config, "MOCK_LLM", True)
    monkeypatch.setattr(config, "LLM_MODE", "")
    return tmp_path


def write_fixture(purpose, key, data, inp=10, out=20):
    path = config.FIXTURES_LLM / purpose / f"{key}.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({"data": data, "input_tokens": inp, "output_tokens": out, "ms": 1}))


# ---------------------------------------------------------------- effort config


def test_effort_override_parsing():
    assert config.parse_lens_effort("") == {}
    assert config.parse_lens_effort("requirement-tests=medium, logic-edge=low") == {
        "requirement-tests": "medium", "logic-edge": "low"}
    for bad in ("requirement-tests", "nope=medium", "logic-edge=extreme", "logic-edge=medium=x"):
        with pytest.raises(ValueError):
            config.parse_lens_effort(bad)


def test_default_effort_table_matches_history():
    assert config.LENS_EFFORT_DEFAULT == {
        "requirement-tests": "high", "logic-edge": "high", "compatibility": "medium",
        "security": "medium", "product-context": "medium", "test-quality": "medium"}
    assert {s.name for s in LENSES} == set(config.LENS_EFFORT_DEFAULT)


# ---------------------------------------------------------------- fixture key


def _old_key(model, system, user, schema):
    """The fixture key before effort could be part of it (commit 333a653)."""
    blob = json.dumps([model, system, user, schema], sort_keys=True, ensure_ascii=False)
    return hashlib.sha256(blob.encode()).hexdigest()[:24]


def test_fixture_key_unchanged_at_default_effort():
    old = _old_key("m", "s", "u", SCHEMA)
    assert llm.fixture_key("m", "s", "u", SCHEMA, "high", default_effort="high") == old
    assert llm.fixture_key("m", "s", "u", SCHEMA, "low") == old  # caller without a default
    medium = llm.fixture_key("m", "s", "u", SCHEMA, "medium", default_effort="high")
    assert medium != old
    assert medium != llm.fixture_key("m", "s", "u", SCHEMA, "low", default_effort="high")


H = Hunk(id="src/MessagePack.SourceGenerator/X.cs#1",
         file="src/MessagePack.SourceGenerator/X.cs", kind="source", old_start=1, old_lines=1,
         new_start=1, new_lines=1, text="-a\n+b")
AREAS = {H.file: "source-generator"}
DEEP = {H.id: RouteResult(hunk_id=H.id, risk=9, category="correctness", confidence=1.0,
                          router="rule")}


def test_lower_effort_never_replays_a_default_effort_fixture(iso, monkeypatch):
    spec = next(s for s in LENSES if s.name == "logic-edge")
    queued = []
    monkeypatch.setattr(llm, "_enqueue", lambda p, k, params: queued.append((p, k, params)))
    monkeypatch.setattr(config, "LLM_MODE", "collect")
    run_lens(spec, "R", "req", [H], DEEP, AREAS, [], [], "c")
    (_, default_key, params), = queued
    assert params["output_config"]["effort"] == "high"
    write_fixture("lens-logic-edge", default_key, {"findings": [], "questions": [],
                                                   "generated_tests": []})
    monkeypatch.setattr(config, "LLM_MODE", "")

    run, parsed = run_lens(spec, "R", "req", [H], DEEP, AREAS, [], [], "c")
    assert (run.status, run.effort, run.from_fixture) == ("ok", "high", True)

    medium = dataclasses.replace(spec, effort="medium")
    run, parsed = run_lens(medium, "R", "req", [H], DEEP, AREAS, [], [], "c")
    assert (run.status, run.effort, parsed) == ("unavailable", "medium", None)


# ---------------------------------------------------------------- collect / replay modes


def test_collect_queues_and_raises_pending(iso, monkeypatch):
    monkeypatch.setattr(config, "LLM_MODE", "collect")
    before = llm.pending_count()
    with pytest.raises(llm.PendingLLM):
        llm.call_json("p", "claude-opus-5-5", "sys", "user", SCHEMA, effort="medium",
                      max_tokens=99)
    assert llm.pending_count() == before + 1
    (entry,) = llm.read_jsonl(llm.queue_path())
    assert entry["custom_id"] == llm.fixture_key("claude-opus-5-5", "sys", "user", SCHEMA, "medium")
    assert entry["purpose"] == "p"
    assert entry["params"] == llm.request_params("claude-opus-5-5", "sys", "user", SCHEMA,
                                                 "medium", 99)

    write_fixture("p", entry["custom_id"], {"ok": True})
    res = llm.call_json("p", "claude-opus-5-5", "sys", "user", SCHEMA, max_tokens=99)
    assert res.data == {"ok": True} and res.from_fixture
    assert len(llm.read_jsonl(llm.queue_path())) == 1


def test_pending_is_unavailable_to_existing_callers():
    assert issubclass(llm.PendingLLM, llm.LLMUnavailable)


def test_failed_batch_request_is_reported_not_requeued(iso, monkeypatch):
    key = llm.fixture_key("m", "s", "u", SCHEMA, "medium")
    llm._append_jsonl(llm.failed_path(), {"custom_id": key, "purpose": "p",
                                          "reason": "errored (overloaded_error: busy)"})
    for mode in ("collect", "replay"):
        monkeypatch.setattr(config, "LLM_MODE", mode)
        with pytest.raises(llm.LLMUnavailable) as e:
            llm.call_json("p", "m", "s", "u", SCHEMA)
        assert not isinstance(e.value, llm.PendingLLM)
        assert "overloaded_error" in str(e.value)
    assert not llm.queue_path().exists()


def test_replay_never_calls_or_queues(iso, monkeypatch):
    monkeypatch.setattr(config, "LLM_MODE", "replay")
    monkeypatch.setattr(config, "MOCK_LLM", False)
    monkeypatch.setattr(config, "ANTHROPIC_API_KEY", "sk-would-be-used")
    with pytest.raises(llm.LLMUnavailable, match="replay mode"):
        llm.call_json("p", "m", "s", "u", SCHEMA)
    assert not llm.queue_path().exists()


def test_collect_queues_every_lens_in_one_pass(iso, monkeypatch):
    monkeypatch.setattr(config, "LLM_MODE", "collect")
    out = run_all_lenses("R", "req", [H], list(DEEP.values()), [], [], areas=AREAS)
    purposes = sorted(e["purpose"] for e in llm.read_jsonl(llm.queue_path()))
    ran = [r.lens for r in out.runs if r.status == "unavailable"]
    assert purposes == sorted(f"lens-{n}" for n in ran)
    assert {"lens-requirement-tests", "lens-logic-edge"} <= set(purposes)


ALWAYS = {"summary": "Always add the attribute", "code_difference": "unconditional emission"}
OPT_OUT = {"summary": "Add it with an opt-out setting", "code_difference": "a boolean option"}


def _reader_key(framing, user):
    return llm.fixture_key(readers.MODEL, readers.SYSTEM.format(framing=framing), user,
                           readers.SCHEMA, readers.EFFORT)


def test_orientation_waits_for_every_reader(iso, monkeypatch):
    monkeypatch.setattr(config, "LLM_MODE", "collect")
    user = readers.build_reader_prompt("R", "req text", [])
    flagged = {"readings": [ALWAYS, OPT_OUT], "materially_different": True, "quote": ""}
    for name, framing in readers.READERS[:2]:
        write_fixture("readers", _reader_key(framing, user), flagged)

    readers.run_panel("R", "req text", [], [H], AREAS, parallel=False)
    assert [e["purpose"] for e in llm.read_jsonl(llm.queue_path())] == ["readers"]

    llm.queue_path().unlink()
    write_fixture("readers", _reader_key(readers.READERS[2][1], user), flagged)
    readers.run_panel("R", "req text", [], [H], AREAS, parallel=False)
    assert [e["purpose"] for e in llm.read_jsonl(llm.queue_path())] == ["readers-orient"]


class QueueingRouter:
    name = "queueing"
    calls = 0

    def route(self, hunk, requirement_text, context):
        QueueingRouter.calls += 1
        raise llm.PendingLLM("queued")


def test_router_keeps_queueing_every_hunk(monkeypatch):
    monkeypatch.setitem(combine._extra, "queueing", QueueingRouter)
    monkeypatch.setattr(QueueingRouter, "calls", 0)
    hunks = [H, H.model_copy(update={"id": H.id[:-1] + "2"})]
    combine.route_all(hunks, "req", {}, "queueing", files=[])
    assert QueueingRouter.calls == 2


# ---------------------------------------------------------------- run_batch


def _msg(text, inp=100, out=50, stop="end_turn"):
    return SimpleNamespace(stop_reason=stop, content=[SimpleNamespace(type="text", text=text)],
                           usage=SimpleNamespace(input_tokens=inp, output_tokens=out))


def _ok(cid, text, **kw):
    return SimpleNamespace(custom_id=cid, result=SimpleNamespace(type="succeeded",
                                                                  message=_msg(text, **kw)))


def _err(cid, kind="errored", etype="overloaded_error", message="busy"):
    error = SimpleNamespace(type="error", error=SimpleNamespace(type=etype, message=message))
    return SimpleNamespace(custom_id=cid, result=SimpleNamespace(type=kind, error=error))


class FakeBatches:
    def __init__(self, results, statuses=("in_progress", "ended")):
        self.results_list, self.statuses = results, list(statuses)
        self.created = []

    def create(self, requests):
        self.created.append(requests)
        return SimpleNamespace(id="msgbatch_1", processing_status="in_progress")

    def retrieve(self, batch_id):
        assert batch_id == "msgbatch_1"
        return SimpleNamespace(processing_status=self.statuses.pop(0))

    def results(self, batch_id):
        return iter(self.results_list)


def fake_client(results, **kw):
    return SimpleNamespace(messages=SimpleNamespace(batches=FakeBatches(results, **kw)))


def _queue(*specs):
    """specs: (purpose, user, model) -> {custom_id: entry} exactly as collect mode writes them."""
    out = {}
    for purpose, user, model in specs:
        key = llm.fixture_key(model, "sys", user, SCHEMA, "high")
        out[key] = {"custom_id": key, "purpose": purpose,
                    "params": llm.request_params(model, "sys", user, SCHEMA, "high", 1000)}
    return out


def test_run_batch_maps_results_by_custom_id(iso, monkeypatch):
    reqs = _queue(("lens-a", "ua", "claude-opus-5-5"), ("lens-b", "ub", "claude-sonnet-5-5"),
                  ("lens-c", "uc", "claude-opus-5-5"), ("lens-d", "ud", "claude-opus-5-5"),
                  ("lens-e", "ue", "claude-opus-5-5"))
    ka, kb, kc, kd, ke = (next(k for k, e in reqs.items() if e["purpose"] == f"lens-{x}")
                          for x in "abcde")
    client = fake_client([  # out of order, one unknown id, one duplicate, one missing (kd)
        _ok(kc, "not json"), _ok("someone-else", '{"ok": true}'), _err(ka),
        _ok(kb, '{"ok": true}', inp=1000, out=2000), _ok(kb, '{"ok": false}'),
        _err(ke, kind="expired", etype="", message=""),
    ])
    sleeps = []
    rep = llm.run_batch(reqs, client=client, poll_s=7, sleep=sleeps.append, log=lambda s: None)

    (created,) = client.messages.batches.created
    assert sorted(r["custom_id"] for r in created) == sorted(reqs)
    assert all(r["params"] == reqs[r["custom_id"]]["params"] for r in created)
    assert sleeps == [7]
    assert (rep.batch_id, rep.submitted, rep.succeeded) == ("msgbatch_1", 5, 1)
    fixture = json.loads((config.FIXTURES_LLM / "lens-b" / f"{kb}.json").read_text())
    assert fixture["data"] == {"ok": True}
    assert (fixture["input_tokens"], fixture["output_tokens"]) == (1000, 2000)
    assert fixture["prompt_preview"] == "ub" and fixture["batch_id"] == "msgbatch_1"
    assert rep.cost_usd == round((1000 * 2 + 2000 * 10) / 1e6 * 0.5, 6)
    for k in (ka, kc, kd, ke):
        assert not (config.FIXTURES_LLM / reqs[k]["purpose"] / f"{k}.json").exists()
    reasons = {e["custom_id"]: e["reason"] for e in rep.errored}
    assert reasons[ka] == "errored (overloaded_error: busy)"
    assert reasons[kc].startswith("non-JSON output")
    assert reasons[kd] == "no result returned by the batch"
    assert reasons[ke] == "expired"
    assert {e["custom_id"] for e in llm.read_jsonl(llm.failed_path())} == {ka, kc, kd, ke}
    assert not (config.BATCH_DIR / "inflight.json").exists()

    monkeypatch.setattr(config, "LLM_MODE", "replay")
    assert llm.call_json("lens-b", "claude-sonnet-5-5", "sys", "ub", SCHEMA, effort="high",
                         max_tokens=1000).data == {"ok": True}
    with pytest.raises(llm.LLMUnavailable, match="overloaded_error"):
        llm.call_json("lens-a", "claude-opus-5-5", "sys", "ua", SCHEMA, effort="high",
                      max_tokens=1000)


def test_refusal_in_batch_is_a_failure(iso):
    reqs = _queue(("p", "u", "claude-opus-5-5"))
    (k,) = reqs
    rep = llm.run_batch(reqs, client=fake_client([_ok(k, "{}", stop="refusal")], statuses=["ended"]),
                        log=lambda s: None)
    assert rep.succeeded == 0 and "refused" in rep.errored[0]["reason"]


def test_run_batch_resumes_inflight_batch(iso):
    reqs = _queue(("p", "u1", "claude-opus-5-5"), ("p", "u2", "claude-opus-5-5"))
    k1, k2 = sorted(reqs)
    config.BATCH_DIR.mkdir(parents=True)
    (config.BATCH_DIR / "inflight.json").write_text(
        json.dumps({"batch_id": "msgbatch_1", "custom_ids": [k1, k2]}))
    del reqs[k1]  # its fixture was written before the crash
    client = fake_client([_ok(k1, '{"ok": true}'), _ok(k2, '{"ok": true}')], statuses=["ended"])
    rep = llm.run_batch(reqs, client=client, log=lambda s: None)
    assert client.messages.batches.created == []
    assert rep.succeeded == 1 and not rep.errored


def test_run_batch_refuses_foreign_inflight(iso):
    config.BATCH_DIR.mkdir(parents=True)
    (config.BATCH_DIR / "inflight.json").write_text(
        json.dumps({"batch_id": "msgbatch_0", "custom_ids": ["other"]}))
    with pytest.raises(RuntimeError, match="msgbatch_0"):
        llm.run_batch(_queue(("p", "u", "m")), client=fake_client([]), log=lambda s: None)


def test_unique_requests_dedupes_and_skips_answered(iso):
    reqs = _queue(("p", "u1", "m"), ("p", "u2", "m"))
    k1, k2 = sorted(reqs)
    write_fixture("p", k2, {"ok": True})
    lines = [reqs[k1], reqs[k1], reqs[k2]]
    assert list(llm.unique_requests(lines)) == [k1]


# ---------------------------------------------------------------- estimate


def test_estimate_uses_history_and_halves_for_batch(tmp_path):
    run = tmp_path / "runs" / "mock-1" / "t1"
    run.mkdir(parents=True)
    (run / "lenses.json").write_text(json.dumps({"lenses": [
        {"lens": "logic-edge", "output_tokens": 10000, "from_fixture": False},
        {"lens": "logic-edge", "output_tokens": 8000, "from_fixture": False},
        {"lens": "logic-edge", "output_tokens": 999999, "from_fixture": True}]}))
    (run / "readers.json").write_text(json.dumps({
        "readers": [{"output_tokens": 600}], "orientation": {"output_tokens": 100}}))
    hist = estimate.historical_output([tmp_path / "runs"], fixtures=tmp_path / "none")
    assert hist == {"lens-logic-edge": (9000.0, 2), "readers": (600.0, 1),
                    "readers-orient": (100.0, 1)}

    reqs = {"k": {"custom_id": "k", "purpose": "lens-logic-edge",
                  "params": llm.request_params("claude-opus-5-5", "s" * 400, "u" * 3600, {}, "high",
                                               48000)},
            "j": {"custom_id": "j", "purpose": "unknown",
                  "params": llm.request_params("claude-sonnet-5-5", "", "x" * 40, {}, "medium", 500)}}
    lines = {x.purpose: x for x in estimate.estimate(reqs, hist)}
    deep = lines["lens-logic-edge"]
    assert deep.output_tokens == 9000 and deep.effort == "high"
    assert deep.input_tokens >= 1000
    assert lines["unknown"].output_tokens == 500  # default capped at max_tokens
    t = estimate.totals(list(lines.values()))
    assert t["batch_usd"] == pytest.approx(t["standard_usd"] / 2, abs=0.01)
    assert "batch $" in estimate.format_table(list(lines.values()))


# ---------------------------------------------------------------- round loop


def _round_script(monkeypatch, rounds):
    """collect_case stand-in: in round r every case queues the requests listed for it."""
    state = {"round": 0}
    entries = [{"id": "mock-1-clean", "requirement": "REQ-1"}]

    def collect(entry, log):
        if "collect-r" in log.name:
            r = int(log.name.split("collect-r")[1].split(".")[0])
            state["round"] = max(state["round"], r)
            for q in rounds[r - 1] if r <= len(rounds) else []:
                if not (config.FIXTURES_LLM / q["purpose"] / f"{q['custom_id']}.json").exists():
                    llm._append_jsonl(llm.queue_path(), q)
        return 0

    monkeypatch.setattr(bench, "collect_case", collect)
    monkeypatch.setattr(estimate, "historical_output", lambda *a, **k: {})
    return entries, state


def test_rounds_until_nothing_pending(iso, monkeypatch, tmp_path):
    r1 = list(_queue(("lens-logic-edge", "u1", "claude-opus-5-5"),
                     ("readers", "u2", "claude-sonnet-5-5")).values())
    r2 = list(_queue(("readers-orient", "u3", "claude-sonnet-5-5")).values())
    entries, state = _round_script(monkeypatch, [r1, r1 + r2])
    batches = []

    def fake_run_batch(reqs, client=None):
        batches.append(sorted(reqs))
        for k, e in reqs.items():
            write_fixture(e["purpose"], k, {"ok": True})
        return llm.BatchReport(batch_id=f"b{len(batches)}", submitted=len(reqs), succeeded=len(reqs))

    monkeypatch.setattr(llm, "run_batch", fake_run_batch)
    stop, log = bench.batch_rounds(entries, 1, tmp_path, max_usd=None, dry_run=False)
    assert stop is None
    assert [len(b) for b in batches] == [2, 1]
    assert [r["requests"] for r in log] == [2, 1, 0]


def test_cost_cap_and_dry_run_submit_nothing(iso, monkeypatch, tmp_path):
    r1 = list(_queue(("lens-logic-edge", "u" * 40000, "claude-opus-5-5")).values())
    entries, _ = _round_script(monkeypatch, [r1])
    monkeypatch.setattr(llm, "run_batch", lambda *a, **k: pytest.fail("must not submit"))
    stop, log = bench.batch_rounds(entries, 1, tmp_path, max_usd=0.01, dry_run=False)
    assert stop == 2 and log[0]["estimate"]["totals"]["batch_usd"] > 0.01
    stop, log = bench.batch_rounds(entries, 1, tmp_path, max_usd=None, dry_run=True)
    assert stop == 0 and len(log) == 1


def test_round_cap(iso, monkeypatch, tmp_path):
    def endless(entry, log):
        r = log.name.split("collect-r")[1].split(".")[0]
        llm._append_jsonl(llm.queue_path(), next(iter(_queue(("p", f"u{r}", "m")).values())))
        return 0

    monkeypatch.setattr(bench, "collect_case", endless)
    monkeypatch.setattr(estimate, "historical_output", lambda *a, **k: {})
    calls = []
    monkeypatch.setattr(llm, "run_batch", lambda reqs, client=None: calls.append(1)
                        or llm.BatchReport())
    stop, log = bench.batch_rounds([{"id": "x", "requirement": "R"}], 1, tmp_path, None, False)
    assert stop is None and len(calls) == bench.MAX_ROUNDS == len(log)
