import json
import os
import subprocess
from pathlib import Path

import pytest

from receipts import bench, config

SAMPLE_CLONE = Path(os.environ.get(
    "RECEIPTS_SAMPLE_CLONE",
    "/private/tmp/claude-501/-Users-sonionring-orca-projects-junction/"
    "aae424c1-cec2-4b34-862a-bf3153e7d7ee/scratchpad/tieto/repo",
))
needs_clone = pytest.mark.skipif(not (SAMPLE_CLONE / ".git").exists(), reason="sample clone not present")

MANIFEST = bench.load_manifest()
FIELDS = {"id", "issue", "variant", "branch", "category", "files", "lines", "description",
          "expected_outcome", "expected_lens", "compiles", "own_tests_pass"}


def test_manifest_has_every_issue_and_variant():
    pairs = {(e["issue"], e["variant"]) for e in MANIFEST}
    issues = {b.removeprefix("ju26-issue-") for b in config.SAMPLE_BRANCHES}
    assert pairs == {(i, v) for i in issues for v in bench.VARIANTS}
    assert len(MANIFEST) >= 24


def test_manifest_entries_are_complete_and_consistent():
    ids = [e["id"] for e in MANIFEST]
    assert len(ids) == len(set(ids))
    for e in MANIFEST:
        assert FIELDS <= e.keys(), e["id"]
        assert e["id"] == f"mock-{e['issue']}-{e['variant']}"
        assert e["branch"] == f"mock/{e['issue']}/{e['variant']}"
        assert e["category"] in bench.CATEGORIES
        assert e["variant"] in bench.VARIANTS
        assert e["compiles"] is True
        assert isinstance(e["own_tests_pass"], bool)
        assert e["description"].strip()


def test_every_plant_has_a_patch_and_clean_has_none():
    for e in MANIFEST:
        patch = bench.patch_path(e)
        if e["variant"] == "clean":
            assert patch is None and e["files"] == []
        else:
            assert patch is not None and patch.is_file(), e["id"]
            assert e["files"], e["id"]
            text = patch.read_text(encoding="utf-8", errors="replace")
            for f in e["files"]:
                assert f"b/{f}" in text, (e["id"], f)


def test_no_patch_without_manifest_entry():
    listed = {bench.patch_path(e) for e in MANIFEST if e["variant"] != "clean"}
    on_disk = set(bench.PLANTS.glob("*/*.patch"))
    assert on_disk == listed


@needs_clone
@pytest.mark.parametrize("entry", [e for e in MANIFEST if e["variant"] != "clean"], ids=lambda e: e["id"])
def test_plant_applies_to_agent_head_in_sample_clone(entry):
    head = bench.resolve_commit(SAMPLE_CLONE, f"refs/remotes/origin/{bench.agent_branch(entry['issue'])}")
    assert head, "agent branch missing in sample clone"
    bench.apply_to_tree(SAMPLE_CLONE, head, bench.patch_path(entry), check_only=True)


def test_generate_reports_missing_mock_repo(tmp_path, monkeypatch, capsys):
    monkeypatch.setenv("RECEIPTS_MOCK_REPO", str(tmp_path / "absent.git"))
    assert bench.generate() == 1
    assert "mock repo not found" in capsys.readouterr().out


@needs_clone
def test_generate_creates_branches_and_is_idempotent(tmp_path, monkeypatch, capsys):
    mock = tmp_path / "mock.git"
    subprocess.run(["git", "clone", "--quiet", "--mirror", str(SAMPLE_CLONE), str(mock)], check=True)
    for branch in config.SAMPLE_BRANCHES:
        subprocess.run(["git", "-C", str(mock), "update-ref", f"refs/heads/{branch}",
                        f"refs/remotes/origin/{branch}"], check=True)
    monkeypatch.setenv("RECEIPTS_MOCK_REPO", str(mock))

    assert bench.generate() == 0
    first = capsys.readouterr().out
    assert first.count(": created mock/") == len(MANIFEST)

    for e in MANIFEST:
        head = bench.resolve_commit(mock, f"refs/heads/{bench.agent_branch(e['issue'])}")
        tip = bench.resolve_commit(mock, f"refs/heads/{e['branch']}")
        if e["variant"] == "clean":
            assert tip == head
            continue
        parent = subprocess.run(["git", "-C", str(mock), "rev-parse", f"{tip}^1"],
                                capture_output=True, text=True, check=True).stdout.strip()
        assert parent == head
        message = subprocess.run(["git", "-C", str(mock), "log", "-1", "--format=%B", tip],
                                 capture_output=True, text=True, check=True).stdout
        assert message.strip() == f"Change for REQ-{e['issue']}"
        assert e["variant"] not in message

    assert bench.generate() == 0
    assert capsys.readouterr().out.count(": skip mock/") == len(MANIFEST)


def test_select_entries_filters_on_manifest_id():
    picked = bench.select_entries(MANIFEST, "mock-2283")
    assert len(picked) == 9 and all(e["issue"] == "2283" for e in picked)
    assert [e["id"] for e in bench.select_entries(MANIFEST, "2297-bait")] == ["mock-2297-bait"]
    assert bench.select_entries(MANIFEST, None) == MANIFEST
    assert bench.select_entries(MANIFEST, "nope") == []


def test_run_with_no_match_does_nothing(capsys):
    assert bench.run(only="no-such-case") == 1
    assert "no manifest entry" in capsys.readouterr().out


def _entry(answered_by, question="q?"):
    return json.dumps({"question": question, "answer": "a", "area": "x",
                       "answered_by": answered_by, "change_id": "c"})


def test_ledger_scope_cold_empties_and_restores(tmp_path, monkeypatch):
    from receipts import oracle

    monkeypatch.setattr(config, "CONTEXT", tmp_path)
    real, old_oracle = _entry("sonion"), _entry(oracle.ORACLE, "old")
    (tmp_path / "ledger.jsonl").write_text(real + "\n" + old_oracle + "\n")
    with bench.LedgerScope("cold"):
        assert (tmp_path / "ledger.jsonl").read_text() == ""
        with (tmp_path / "ledger.jsonl").open("a") as f:
            f.write(_entry(oracle.ORACLE, "new") + "\n")
    lines = (tmp_path / "ledger.jsonl").read_text().splitlines()
    assert lines == [real, _entry(oracle.ORACLE, "new")]
    assert (tmp_path / "ledger.cold-backup.jsonl").read_text().splitlines() == [real]


def test_ledger_scope_warm_keeps_only_oracle_and_restores_on_crash(tmp_path, monkeypatch):
    from receipts import oracle

    monkeypatch.setattr(config, "CONTEXT", tmp_path)
    real, ans = _entry("sonion"), _entry(oracle.ORACLE)
    (tmp_path / "ledger.jsonl").write_text(real + "\n" + ans + "\n")
    with pytest.raises(RuntimeError):
        with bench.LedgerScope("warm"):
            assert (tmp_path / "ledger.jsonl").read_text().splitlines() == [ans]
            raise RuntimeError("case crashed")
    assert (tmp_path / "ledger.jsonl").read_text().splitlines() == [real, ans]


def test_summary_merges_partial_reruns(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "RUNS", tmp_path)
    bench._write_summary("cold", [{"id": "mock-2283-clean", "wall_s": 10, "verdict": "blocked"},
                                  {"id": "mock-2283-logic", "wall_s": 20, "verdict": "blocked"}], {})
    bench._write_summary("cold", [{"id": "mock-2283-clean", "wall_s": 5,
                                   "verdict": "needs-sign-off"}], {"only": "clean"})
    s = json.loads((tmp_path / "bench-cold-summary.json").read_text())
    assert [c["id"] for c in s["cases"]] == ["mock-2283-clean", "mock-2283-logic"]
    assert s["totals"]["wall_s_sum"] == 25
    assert s["totals"]["verdicts"] == {"blocked": 1, "needs-sign-off": 1}


def test_leak_scan_flags_labels(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "RUNS", tmp_path)
    monkeypatch.setattr(config, "FIXTURES_LLM", tmp_path / "fx")
    (tmp_path / "fx").mkdir()
    d = tmp_path / "mock-2283-bait" / "ts"
    d.mkdir(parents=True)
    (d / "intake.json").write_text(json.dumps({"hunks": [{"file": "a.cs", "text": "+int x = 1;"}]}))
    assert bench.leak_scan(0, [d]) == []
    (d / "intake.json").write_text(json.dumps({"hunks": [{"file": "a.cs",
                                                          "text": "+// see mock/2283/bait"}]}))
    hits = bench.leak_scan(0, [d])
    assert {h["pattern"] for h in hits} >= {r"mock[/-]\d{4}", r"\bbait\b"}
