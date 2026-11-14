import os
import subprocess
from pathlib import Path

import pytest

from receipts import config
from receipts.compat import (
    compose, diff_api, diff_bytes, public_api_listing, roundtrip_failures, run_compat,
)


def test_diff_bytes_reports_changed_and_one_sided_cases():
    base = {"int/1": "01", "int/-128": "d080", "gone": "c0"}
    head = {"int/1": "01", "int/-128": "d1ff80", "new": "c3"}
    assert diff_bytes(base, head) == [
        {"case": "gone", "base_hex": "c0", "head_hex": None},
        {"case": "int/-128", "base_hex": "d080", "head_hex": "d1ff80"},
        {"case": "new", "base_hex": None, "head_hex": "c3"},
    ]
    assert diff_bytes(base, dict(base)) == []


def test_roundtrip_failures_ignore_ok_and_skipped():
    results = {"a": "ok", "b": "skipped: base could not serialize this case", "c": "mismatch: read back as 00",
               "d": "error: MessagePackSerializationException: boom"}
    assert [f["case"] for f in roundtrip_failures(results)] == ["c", "d"]


def test_api_diff_and_composed_result():
    added, removed = diff_api(["A", "B"], ["B", "C"])
    assert (added, removed) == (["C"], ["A"])
    assert compose([], [], ["C"], [], 10)[0] == "pass"  # additions alone are compatible
    assert compose([], [], [], ["A"], 10)[0] == "fail"  # a removed public member breaks callers
    assert compose([], [{"case": "x", "result": "mismatch"}], [], [], 10)[0] == "fail"
    result, title, excerpt = compose([{"case": "int/-128", "base_hex": "d080", "head_hex": "d1ff80"}], [], [], [], 10)
    assert result == "fail" and "1/10 cases differ" in title and "int/-128" in excerpt


# Real harness: builds MessagePack from worktrees of the sample repo (about 20 s warm).
SAMPLE = Path(os.environ.get("RECEIPTS_SAMPLE_REPO") or config.MOCK_REPO)
dotnet = pytest.mark.skipif(
    os.environ.get("RUN_DOTNET_TESTS") != "1" or not SAMPLE.exists(),
    reason="set RUN_DOTNET_TESTS=1 and RECEIPTS_SAMPLE_REPO (or create mock-repo/)",
)


def _git(*args: str) -> str:
    return subprocess.run(["git", "-C", str(SAMPLE), *args], check=True, capture_output=True, text=True).stdout


def _rev(name: str) -> str:
    for ref in (name, f"origin/{name}"):
        r = subprocess.run(["git", "-C", str(SAMPLE), "rev-parse", "--verify", "-q", ref + "^{commit}"],
                           capture_output=True, text=True)
        if r.returncode == 0:
            return r.stdout.strip()
    raise LookupError(name)


@pytest.fixture
def worktree(tmp_path):
    made = []

    def add(name: str, rev: str) -> Path:
        path = tmp_path / name
        _git("worktree", "add", "--detach", str(path), rev)
        made.append(path)
        return path

    yield add
    for path in made:
        _git("worktree", "remove", "--force", str(path))


def _plant(path: Path, old: str, new: str) -> None:
    text = path.read_text()
    assert old in text, f"plant anchor missing in {path}"
    path.write_text(text.replace(old, new, 1))


@dotnet
def test_planted_writer_and_reader_changes_are_caught(worktree, tmp_path):
    base = worktree("base", config.BASE_SHA)
    planted = worktree("planted", config.BASE_SHA)
    src = planted / "src" / "MessagePack"
    # Two-line anchor: the same `case` line also exists in the short and long overloads.
    int_switch = ("case >= sbyte.MinValue: return TryWriteInt8(destination, unchecked((sbyte)value), out bytesWritten);\n"
                  "            case >= short.MinValue: return TryWriteInt16(destination, unchecked((short)value), out bytesWritten);\n"
                  "            default: return TryWriteInt32(")
    _plant(src / "MessagePackPrimitives.Writers.cs", int_switch, int_switch.replace(">= sbyte", "> sbyte", 1))
    _plant(src / "MessagePackPrimitives.Readers.cs", "AddSeconds(uintValue)", "AddSeconds(uintValue + 1)")

    receipts, ev = run_compat(base, planted, tmp_path / "run")

    assert ev.result == "fail", ev.excerpt
    assert {d["case"] for d in ev.details["differing"]} == {"int/-128"}
    assert ev.details["differing"][0]["head_hex"] == "d1ff80"
    assert "object/nested" in {f["case"] for f in ev.details["roundtrip_failures"]}
    assert all(r.exit_code == 0 for r in receipts)
    assert (tmp_path / "run" / receipts[0].log_path).exists()


@dotnet
def test_2129_keeps_bytes_and_adds_public_api(worktree, tmp_path):
    base = worktree("base", config.BASE_SHA)
    head = worktree("h2129", _rev("ju26-issue-2129"))

    _, ev = run_compat(base, head, tmp_path / "run")

    assert ev.result == "pass", ev.excerpt
    assert ev.details["differing"] == [] and ev.details["roundtrip_failures"] == []
    assert ev.details["api_removed"] == []
    assert any("GeneratedMessagePackFormatterAttribute" in a for a in ev.details["api_added"])

    listing = public_api_listing(head, tmp_path / "api")
    assert set(ev.details["api_added"]) <= set(listing)
