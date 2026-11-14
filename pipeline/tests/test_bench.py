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


def test_run_is_a_stub():
    with pytest.raises(NotImplementedError):
        bench.run()
