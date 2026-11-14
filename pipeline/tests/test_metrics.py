import json

import pytest

from receipts import config, metrics

F = "src/X/Collector.cs"
RT = "src/MessagePack/Writers.cs"

MANIFEST = [
    {"id": "mock-1-clean", "issue": "1", "variant": "clean", "category": "none", "files": [],
     "lines": [], "expected_lens": []},
    {"id": "mock-1-logic", "issue": "1", "variant": "logic", "category": "correctness",
     "files": [F], "lines": [f"{F}:100-105"], "expected_lens": ["logic-edge", "requirement-tests"]},
    {"id": "mock-1-edge", "issue": "1", "variant": "edge", "category": "correctness",
     "files": [F], "lines": [f"{F}:200-202"], "expected_lens": ["logic-edge"]},
    {"id": "mock-1-compat", "issue": "1", "variant": "compat", "category": "compatibility",
     "files": [RT], "lines": [], "expected_lens": ["compatibility"]},
    {"id": "mock-1-control", "issue": "1", "variant": "control", "category": "none",
     "files": [F], "lines": [f"{F}:10-12"], "expected_lens": []},
    {"id": "mock-1-bait", "issue": "1", "variant": "bait", "category": "convention",
     "files": [F], "lines": [f"{F}:300-310"], "expected_lens": ["product-context"]},
]
CASE = {c["id"]: c for c in MANIFEST}


def finding(lens, location, evidence=("ev",), fid=None):
    return {"id": fid or f"{lens}-1", "lens": lens, "claim": "c", "location": location,
            "severity": "high", "receipt_plan": {"kind": "test", "detail": ""},
            "verified": True, "evidence_ids": list(evidence)}


def write_run(root, cid, mode, seq, verdict, findings=(), evidence=(), questions=(), reason="",
              areas=("type-collector",), ts=None):
    d = root / cid / (ts or f"2026111{seq % 10}T00000{0 if mode == 'cold' else 1}Z")
    d.mkdir(parents=True)
    (d / "receipt.json").write_text(json.dumps({
        "change_id": cid, "verdict": verdict, "verdict_reason": reason, "findings": list(findings),
        "hunches": [], "areas": list(areas), "routing": {}}))
    (d / "evidence.json").write_text(json.dumps(list(evidence)))
    (d / "findings.json").write_text(json.dumps(list(findings)))
    (d / "questions.json").write_text(json.dumps(list(questions)))
    (d / "bench.json").write_text(json.dumps({"mode": mode, "seq": seq, "manifest_id": cid}))
    return d


MUT_EV = {"id": "mutation", "kind": "mutation", "result": "partial", "command_ids": ["mutant-1"]}
BYTES_EV = {"id": "compat-bytes", "kind": "byte-diff", "result": "fail", "command_ids": ["c1"]}
TEST_EV = {"id": "gentest-1", "kind": "fail-before-pass-after", "result": "fail",
           "command_ids": ["g1"]}


@pytest.fixture
def bench_runs(tmp_path):
    root = tmp_path / "runs"
    baseline = finding("test-quality", f"{F}:50", ("mutation",), "mutation-survivor-m1")
    write_run(root, "mock-1-clean", "cold", 1, "needs-sign-off", [baseline], [MUT_EV])
    # logic: caught by an edge test at the planted line (±10) and by an existing-test regression
    write_run(root, "mock-1-logic", "cold", 2, "blocked",
              [finding("logic-edge", f"{F}:110", ("gentest-1",)), baseline], [TEST_EV, MUT_EV],
              reason="1 existing test(s) pass on base but fail on head: T.X")
    # edge: missed (finding far from the planted line is off-target, not a catch)
    write_run(root, "mock-1-edge", "cold", 3, "needs-sign-off",
              [finding("security", f"{F}:900")], [])
    # compat: caught by the byte diff (file match, no lines in the manifest)
    write_run(root, "mock-1-compat", "cold", 4, "blocked",
              [finding("compatibility", RT, ("compat-bytes",), "compat-break")], [BYTES_EV])
    # control: one baseline finding and one false alarm
    write_run(root, "mock-1-control", "cold", 5, "needs-sign-off",
              [baseline, finding("logic-edge", f"{F}:11", ("gentest-1",))], [TEST_EV, MUT_EV])
    # bait: flags the bait file -> bait false alarm (and a false alarm)
    write_run(root, "mock-1-bait", "cold", 6, "blocked",
              [finding("product-context", f"{F}:305", ("gentest-1",))], [TEST_EV],
              questions=[{"id": "q1", "area": "analyzer", "text": "Is it deliberate?"}])
    write_run(root, "mock-1-bait", "warm", 6, "needs-sign-off", [], [], ts="20261120T000000Z")
    return root


def test_load_takes_newest_run_per_mode(bench_runs):
    runs = metrics.load_bench_runs(MANIFEST, bench_runs)
    assert ("mock-1-bait", "warm") in runs and ("mock-1-bait", "cold") in runs
    assert runs[("mock-1-bait", "warm")].verdict == "needs-sign-off"
    assert len([k for k in runs if k[1] == "cold"]) == 6


def test_classification_caught_missed_false_alarm_baseline(bench_runs):
    runs = metrics.load_bench_runs(MANIFEST, bench_runs)
    res = {r.run.change_id: r for r in metrics.classify_all(runs, "cold")}

    logic = res["mock-1-logic"]
    assert logic.caught and logic.rows == {"logic-edge", "fail-before-pass-after",
                                           metrics.BUILD_TESTS}
    assert [f["lens"] for f in logic.baseline] == ["test-quality"]

    edge = res["mock-1-edge"]
    assert not edge.caught and edge.rows == set() and len(edge.off_target) == 1

    compat = res["mock-1-compat"]
    assert compat.caught and compat.rows == {"compatibility", "byte-diff"}

    control = res["mock-1-control"]
    assert len(control.baseline) == 1 and len(control.false_alarms) == 1
    assert not control.caught

    bait = res["mock-1-bait"]
    assert len(bait.bait_false_alarms) == 1 and len(bait.false_alarms) == 1

    assert res["mock-1-clean"].baseline and not res["mock-1-clean"].false_alarms


def survivor(location, detail):
    f = finding("test-quality", location, ("mutation",), f"mutation-survivor-{detail}")
    f["receipt_plan"] = {"kind": "mutation", "detail": detail}
    return f


def _run(cid, verdict, findings):
    return metrics.Run(change_id=cid, dir=None, case=CASE.get(cid) or {
        "id": cid, "issue": "1", "variant": "missing-test", "category": "tests",
        "files": ["tests/T.cs"], "lines": [], "expected_lens": ["test-quality"]},
        receipt={"verdict": verdict, "verdict_reason": "", "findings": findings})


def test_mutation_baseline_uses_mutant_text_not_just_file():
    clean = _run("mock-1-clean", "needs-sign-off", [survivor(f"{F}:101", "a -> b")])
    # same mutant as the clean run, even at the planted line: baseline, not a catch
    same = metrics.classify(_run("mock-1-logic", "needs-sign-off",
                                 [survivor(f"{F}:103", "a -> b")]), clean)
    assert not same.caught and len(same.baseline) == 1
    # a new mutant in the same file at the planted line: a catch
    new = metrics.classify(_run("mock-1-logic", "needs-sign-off",
                                [survivor(f"{F}:103", "x -> y")]), clean)
    assert new.caught and new.rows == {"test-quality"}
    # a new mutant in the same file away from the plant: about the agent's change, baseline
    away = metrics.classify(_run("mock-1-logic", "needs-sign-off",
                                 [survivor(f"{F}:700", "x -> y")]), clean)
    assert not away.caught and len(away.baseline) == 1


def test_missing_test_plant_caught_by_any_new_survivor():
    clean = _run("mock-1-clean", "needs-sign-off", [survivor(f"{F}:5", "a -> b")])
    r = metrics.classify(_run("mock-1-missing-test", "needs-sign-off",
                              [survivor(f"{F}:5", "a -> b"), survivor(f"{F}:900", "p -> q")]),
                         clean)
    assert r.caught and len(r.matching) == 1 and len(r.baseline) == 1


def test_verdict_only_catch_gets_pseudo_lens():
    run = metrics.Run(change_id="mock-1-edge", dir=None, case=CASE["mock-1-edge"],
                      receipt={"verdict": "blocked", "verdict_reason": "verified high finding(s)",
                               "findings": [finding("security", f"{F}:900")]})
    r = metrics.classify(run, None)
    assert r.caught and r.rows == {metrics.VERDICT_ONLY}


def test_t1_rows(bench_runs):
    runs = metrics.load_bench_runs(MANIFEST, bench_runs)
    t1 = metrics.table_t1(metrics.classify_all(runs, "cold"), "cold")
    rows = {r[0]: dict(zip(t1["columns"], r)) for r in t1["rows"]}
    le = rows["logic-edge"]
    assert (le["planted_in_scope"], le["caught"], le["missed"], le["false_alarms"]) == (2, 1, 1, 1)
    assert le["recall"] == 0.5 and le["precision"] == 0.5
    assert rows["compatibility"]["recall"] == 1.0 and rows["compatibility"]["precision"] == 1.0
    assert rows["byte-diff"]["caught"] == 1
    bt = rows[metrics.BUILD_TESTS]
    assert bt["planted_in_scope"] == 3 and bt["caught"] == 1
    assert rows["requirement-tests"]["caught"] == 0 and rows["requirement-tests"]["recall"] == 0.0
    assert rows["security"]["planted_in_scope"] == 0 and rows["security"]["recall"] is None
    assert {"lens", "recall"} <= set(t1["columns"]) and "note" in t1


def test_t5_shape_and_repeats(bench_runs):
    from receipts import oracle

    runs = metrics.load_bench_runs(MANIFEST, bench_runs)
    by_mode = {m: metrics.classify_all(runs, m) for m in ("cold", "warm")}
    ledger = [{"area": "analyzer", "question": "Is it deliberate?", "answered_by": oracle.ORACLE}]
    # the warm bait run asked the same question again: a repeat
    (runs[("mock-1-bait", "warm")].dir / "questions.json").write_text(json.dumps(
        [{"id": "q1", "area": "analyzer", "text": "Is it deliberate?"}]))
    by_mode["warm"] = metrics.classify_all(metrics.load_bench_runs(MANIFEST, bench_runs), "warm")
    t5 = metrics.table_t5(by_mode, ledger)
    assert t5["columns"][:4] == ["seq", "change_id", "mode", "questions"]
    rows = [dict(zip(t5["columns"], r)) for r in t5["rows"]]
    cold_bait = next(r for r in rows if r["mode"] == "cold" and r["change_id"] == "mock-1-bait")
    assert cold_bait["questions"] == 1 and cold_bait["bait_false_alarms"] == 1
    warm = [r for r in rows if r["mode"] == "warm"]
    assert len(warm) == 1 and warm[0]["repeat_questions"] == 1
    assert oracle.ORACLE in t5["note"]


def test_trust_increments_after_five_and_resets_on_escape():
    def res(seq, variant, verdict, caught):
        case = {"id": f"c{seq}", "variant": variant, "category": "x", "files": [], "lines": []}
        run = metrics.Run(change_id=f"c{seq}", dir=None, seq=seq, case=case,
                          receipt={"verdict": verdict, "areas": ["a"]})
        return metrics.CaseResult(run=run, planted=variant not in metrics.NON_PLANTED,
                                  caught=caught)

    seqs = [res(i, "control", "needs-sign-off", False) for i in range(1, 6)]
    levels, hist = metrics.trust_history(seqs)
    assert levels == {"a": 2} and hist[-1]["level"] == 2 and "1 -> 2" in hist[-1]["event"]

    seqs.append(res(6, "logic", "auto-ready", False))
    levels, hist = metrics.trust_history(seqs)
    assert levels == {"a": 0} and hist[-1]["event"].startswith("escape")

    # a wrong verdict (planted, missed, not auto-ready) resets the streak but not the level
    seqs = [res(i, "control", "needs-sign-off", False) for i in range(1, 5)]
    seqs.append(res(5, "edge", "needs-sign-off", False))
    seqs += [res(i, "control", "needs-sign-off", False) for i in range(6, 10)]
    levels, _ = metrics.trust_history(seqs)
    assert levels == {"a": 1}


def test_planted_hunks_overlap_with_tolerance():
    from receipts.models import Hunk

    def hunk(n, start, lines, file=F, kind="source"):
        return Hunk(id=f"{file}#{n}", file=file, kind=kind, old_start=start, old_lines=lines,
                    new_start=start, new_lines=lines, text="+x")

    hs = [hunk(1, 90, 3), hunk(2, 150, 5), hunk(3, 1, 2, file="other.cs")]
    ids = [h.id for h in metrics.planted_hunks(hs, CASE["mock-1-logic"])]
    assert ids == [f"{F}#1"]  # 90..92 is within 10 lines of 100..105; 150 is not


def test_run_metrics_writes_tables(bench_runs, tmp_path, monkeypatch):
    monkeypatch.setattr(config, "RUNS", bench_runs)
    monkeypatch.setattr(config, "METRICS", tmp_path / "metrics")
    monkeypatch.setattr(config, "CONTEXT", tmp_path / "context")
    monkeypatch.setattr("receipts.bench.load_manifest", lambda: MANIFEST)
    monkeypatch.setattr(metrics, "ROUTERS", [])  # no intake in the fake runs; routing is separate
    assert metrics.run_metrics() == 0
    out = tmp_path / "metrics"
    for name in ("T1", "T2", "T3", "T4", "T5", "T6", "summary", "trust", "trust-history"):
        assert (out / f"{name}.json").exists(), name
    summary = json.loads((out / "summary.json").read_text())
    assert summary["cold"]["planted"] == 3 and summary["cold"]["caught"] == 2
    assert summary["cold"]["false_alarm_cases"] == 2 and summary["cold"]["bait_false_alarms"] == 1
    t2 = json.loads((out / "T2.json").read_text())
    assert all(len(r) == len(t2["columns"]) for r in t2["rows"])
