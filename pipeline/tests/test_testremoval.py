import subprocess
from pathlib import Path
from types import SimpleNamespace

import pytest

from receipts import testremoval as tr
from receipts.models import Hunk
from receipts.models import TestCase as Case
from receipts.models import TestRun as Run

FOO = '''using Xunit;

public class FooTests
{
    [Fact]
    public async Task Drops()
    {
        string src = """
            namespace N
            {
                public class C { }
            }
            """;
        Assert.Equal(1, Run(src));
        Assert.True(Ok());
    }

    [Theory]
    [InlineData(1)]
    public void Keeps(int x)
    {
        string s = @"{ ""}"" }";
        Assert.Equal(x, x);
    }
}
'''


def _parse(text: str, path: str = "tests/P/FooTests.cs") -> dict[str, tr.TestMethod]:
    return {t.key: t for t in tr.parse_tests(path, text)}


def test_parse_finds_fact_and_theory_and_skips_braces_in_strings():
    tests = _parse(FOO)
    assert set(tests) == {"FooTests.Drops", "FooTests.Keeps"}
    assert tests["FooTests.Drops"].line == 5
    assert tests["FooTests.Keeps"].line == 18
    assert tests["FooTests.Drops"].body.rstrip().endswith("}")
    assert "Assert.True" in tests["FooTests.Drops"].body
    assert tr.assertion_count(tests["FooTests.Drops"]) == 2


def _classify(before: str | None, head: str | None, base_sha: str = "base",
              revisions: list[tuple[str, str | None]] | None = None):
    revs = revisions or [(base_sha, before)]
    parsed = [(sha, tr.parse_tests("tests/P/FooTests.cs", text) if text else [])
              for sha, text in revs]
    head_tests = tr.parse_tests("tests/P/FooTests.cs", head) if head else []
    return tr.classify_removals(parsed, head_tests, base_sha)


def _without(text: str, start: str, end: str) -> str:
    a, b = text.index(start), text.index(end)
    return text[:a] + text[b:]


def test_removed_method_is_reported_from_base():
    head = _without(FOO, "    [Fact]", "    [Theory]")
    removed, weakened = _classify(FOO, head)
    assert [r.test.key for r in removed] == ["FooTests.Drops"]
    assert removed[0].on_base and removed[0].before_sha == "base"
    assert not weakened


def test_removed_file_reports_every_test():
    removed, _ = _classify(FOO, None)
    assert sorted(r.test.key for r in removed) == ["FooTests.Drops", "FooTests.Keeps"]


def test_test_added_then_removed_inside_the_change_is_reported_from_that_commit():
    head = _without(FOO, "    [Fact]", "    [Theory]")
    removed, _ = _classify(None, head, revisions=[("base", head), ("c1", FOO)])
    assert [(r.test.key, r.before_sha, r.on_base) for r in removed] == [
        ("FooTests.Drops", "c1", False)]


def test_renamed_test_with_same_body_is_not_removed():
    removed, _ = _classify(FOO, FOO.replace("Task Drops()", "Task DropsRenamed()"))
    assert removed == []


def test_test_moved_to_another_class_is_not_removed():
    removed, _ = _classify(FOO, FOO.replace("class FooTests", "class BarTests"))
    assert removed == []


def test_removed_assertion_is_weakened_not_removed():
    head = FOO.replace("        Assert.True(Ok());\n", "")
    removed, weakened = _classify(FOO, head)
    assert removed == []
    assert [(w.test.key, w.before_count, w.head_count) for w in weakened] == [
        ("FooTests.Drops", 2, 1)]


def test_unchanged_tests_report_nothing():
    assert _classify(FOO, FOO) == ([], [])


# History over a real git repo ----------------------------------------------------------------


def _git(repo: Path, *args: str) -> str:
    return subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True,
                          text=True).stdout.strip()


def _commit(repo: Path, files: dict[str, str | None], msg: str) -> str:
    for rel, text in files.items():
        p = repo / rel
        if text is None:
            p.unlink()
        else:
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(text)
    _git(repo, "add", "-A")
    _git(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", msg)
    return _git(repo, "rev-parse", "HEAD")


def test_detect_walks_the_change_history(tmp_path):
    repo = tmp_path / "r"
    repo.mkdir()
    _git(repo, "init", "-q")
    path = "tests/P/FooTests.cs"
    only_keeps = _without(FOO, "    [Fact]", "    [Theory]")
    base = _commit(repo, {path: only_keeps, "src/A.cs": "class A {}\n"}, "base")
    _commit(repo, {path: FOO, "src/A.cs": "class A { int x; }\n"}, "agent adds a test")
    head = _commit(repo, {path: only_keeps}, "drop it")
    removed, weakened = tr.detect(repo, base, head)
    assert [(r.test.key, r.on_base) for r in removed] == [("FooTests.Drops", False)]
    assert weakened == []
    removed, _ = tr.detect(repo, base, base)
    assert removed == []


# Receipt ---------------------------------------------------------------------------------------


def test_orphaned_lines_are_covered_before_and_uncovered_on_head():
    changed = {"src/A.cs": {10, 11, 12, 13}}
    before = {"src/A.cs": {10: 1, 11: 2, 12: 0}}
    head = {"src/A.cs": {10: 5, 11: 0, 12: 0, 13: 0}}
    assert tr.orphaned_lines(changed, before, head) == {"src/A.cs": [11]}
    assert tr.orphaned_lines(changed, before, {"src/A.cs": {10: 1, 11: 1}}) == {}


def _ctx(tmp_path, head_build_ok=True, requirement_text="Report a diagnostic for X."):
    hunk = Hunk(id="src/A.cs#1", file="src/A.cs", kind="source", old_start=1, old_lines=1,
                new_start=1, new_lines=3, header="", text=" a\n+b\n+c")
    return SimpleNamespace(
        base_sha="base", head_sha="head", run_dir=tmp_path, commands=[], tests=[],
        head_build_ok=head_build_ok, intake=SimpleNamespace(hunks=[hunk]),
        requirement_text=requirement_text,
        impact={"members": [{"file": "src/A.cs", "head_span": [1, 3], "name": "A.M",
                             "member": "M", "type_name": "A"}]})


def _removed(on_base: bool) -> tr.RemovedTest:
    t = tr.TestMethod(cls="FooTests", name="Drops", file="tests/P/FooTests.cs", line=5, body="{}")
    return tr.RemovedTest(test=t, before_sha="c1" + "0" * 38, on_base=on_base)


def _fake(monkeypatch, removed, before_hits, head_hits, passed=True, before_br=None,
          head_br=None):
    monkeypatch.setattr(tr, "detect", lambda repo, b, h: (removed, []))
    monkeypatch.setattr(tr, "_same_file", lambda repo, a, b, p: True)

    def cov(sha, rid, flt, run_dir, commands):
        run = Run(receipt_id=rid, side="head", compiled=True, total=1, passed=int(passed),
                  failed=int(not passed),
                  cases=[Case(name="FooTests.Drops", outcome="passed" if passed else "failed")])
        if flt:
            return before_hits, before_br or {}, run, ""
        return head_hits, head_br or {}, run, ""

    monkeypatch.setattr(tr, "_coverage_run", cov)


@pytest.mark.parametrize("on_base", [True, False])
def test_deleted_passing_test_with_orphaned_lines_is_high(tmp_path, monkeypatch, on_base):
    _fake(monkeypatch, [_removed(on_base)], {"src/A.cs": {2: 1, 3: 1}}, {"src/A.cs": {2: 4, 3: 0}})
    ev, findings, gaps, ran = tr.run(_ctx(tmp_path))
    assert ran and not gaps
    assert [(e.kind, e.result) for e in ev] == [("test-removal", "fail")]
    assert ev[0].details["orphaned_lines"] == {"src/A.cs": [3]}
    [f] = findings
    assert f.verified and f.evidence_ids == [ev[0].id] and f.severity == "high"
    assert f.lens == "test-quality" and f.location == "tests/P/FooTests.cs:5"
    assert "A.M" in f.claim and "deletes the passing test" in f.claim


def test_deleted_passing_test_still_covered_elsewhere_is_medium(tmp_path, monkeypatch):
    _fake(monkeypatch, [_removed(True)], {"src/A.cs": {2: 1}}, {"src/A.cs": {2: 7}})
    ev, findings, _, _ = tr.run(_ctx(tmp_path))
    assert ev[0].result == "fail" and ev[0].details["orphaned_lines"] == {}
    [f] = findings
    assert f.verified and f.severity == "medium" and f.evidence_ids == [ev[0].id]
    assert "deletes a passing test" in f.claim and "nothing asserts what it asserted" in f.claim


def test_deleted_passing_test_without_coverage_report_is_still_medium(tmp_path, monkeypatch):
    _fake(monkeypatch, [_removed(True)], None, None)
    ev, findings, _, _ = tr.run(_ctx(tmp_path))
    assert [(f.verified, f.severity) for f in findings] == [(True, "medium")]
    assert "coverage not compared" in ev[0].title


def test_removal_the_ticket_asks_for_stays_a_hunch(tmp_path, monkeypatch):
    _fake(monkeypatch, [_removed(True)], {"src/A.cs": {2: 1}}, {"src/A.cs": {2: 0}})
    ctx = _ctx(tmp_path, requirement_text="Fix the parser. Remove the obsolete tests for v1.")
    ev, findings, _, _ = tr.run(ctx)
    assert ev[0].result == "partial" and ev[0].details["ticket_asks_for_test_removal"]
    assert [f.verified for f in findings] == [False]


@pytest.mark.parametrize("text,asks", [
    ("Remove the old tests for the v1 parser.", True),
    ("Replace the snapshot test with a unit test.", True),
    ("These tests are obsolete now.", True),
    ("Remove the null check. Add a test for empty input.", False),
    ("Delete the cache entry when the key expires.", False),
    ("", False),
])
def test_ticket_asks_for_test_removal_needs_both_words_in_one_sentence(text, asks):
    assert tr.ticket_asks_for_test_removal(text) is asks


def test_weakened_test_is_only_a_hunch(tmp_path, monkeypatch):
    head = tr.TestMethod(cls="FooTests", name="Keeps", file="tests/P/FooTests.cs", line=9,
                         body="{ Assert.True(x); }")
    weak = tr.WeakenedTest(test=head, before_sha="b" * 40, before_count=2, head_count=1)
    monkeypatch.setattr(tr, "detect", lambda repo, b, h: ([], [weak]))
    ev, findings, _, _ = tr.run(_ctx(tmp_path))
    assert ev == [] and [(f.verified, f.id) for f in findings] == [(False, "weakened-test-1")]


def test_deleted_test_failing_before_has_no_receipt(tmp_path, monkeypatch):
    _fake(monkeypatch, [_removed(True)], {"src/A.cs": {2: 1}}, {"src/A.cs": {2: 0}}, passed=False)
    ev, findings, _, _ = tr.run(_ctx(tmp_path))
    assert ev[0].result == "error"
    assert [f.verified for f in findings] == [False]


def test_head_not_building_leaves_hunches_and_a_gap(tmp_path, monkeypatch):
    _fake(monkeypatch, [_removed(True)], {}, {})
    ev, findings, gaps, _ = tr.run(_ctx(tmp_path, head_build_ok=False))
    assert ev == [] and [f.verified for f in findings] == [False]
    assert gaps and "head does not build" in gaps[0].why


def test_no_removed_test_runs_nothing(tmp_path, monkeypatch):
    monkeypatch.setattr(tr, "detect", lambda repo, b, h: ([], []))
    monkeypatch.setattr(tr, "_coverage_run", lambda *a: pytest.fail("no coverage run expected"))
    assert tr.run(_ctx(tmp_path)) == ([], [], [], True)


def test_branch_only_the_deleted_test_took_verifies_even_when_lines_stay_covered(tmp_path,
                                                                                 monkeypatch):
    k_taken, k_other = (2, 10, 12, 1, 3), (2, 10, 14, 0, 2)
    _fake(monkeypatch, [_removed(False)], {"src/A.cs": {2: 1}}, {"src/A.cs": {2: 9}},
          before_br={"src/A.cs": {k_taken: 1, k_other: 0}},
          head_br={"src/A.cs": {k_taken: 0, k_other: 9}})
    ev, findings, _, _ = tr.run(_ctx(tmp_path))
    assert ev[0].result == "fail" and ev[0].details["orphaned_lines"] == {}
    assert ev[0].details["orphaned_branches"]["src/A.cs"][0]["ordinal"] == 3
    [f] = findings
    assert f.verified and f.severity == "high" and "branch outcome" in f.claim


def test_orphaned_branches_ignore_unchanged_lines_and_outcomes_head_does_not_list():
    changed = {"src/A.cs": {2}}
    before = {"src/A.cs": {(2, 1, 2, 0, 0): 1, (5, 1, 2, 0, 0): 1, (2, 3, 4, 0, 1): 1}}
    head = {"src/A.cs": {(2, 1, 2, 0, 0): 0, (5, 1, 2, 0, 0): 0}}
    assert tr.orphaned_branches(changed, before, head) == {"src/A.cs": [(2, 1, 2, 0, 0)]}


def test_parse_coverlet_json_relativizes_paths(tmp_path):
    import json

    root = tmp_path / "wt"
    (root / "src").mkdir(parents=True)
    report = tmp_path / "coverage.json"
    report.write_text(json.dumps({"M.dll": {str(root / "src" / "A.cs"): {"A": {"m()": {
        "Lines": {"2": 1}, "Branches": [{"Line": 2, "Offset": 1, "EndOffset": 2, "Path": 0,
                                         "Ordinal": 0, "Hits": 3}]}}}}}))
    assert tr.parse_coverlet_json(report, root) == {"src/A.cs": {(2, 1, 2, 0, 0): 3}}


def test_class_declared_inside_a_fixture_string_is_not_the_test_class():
    text = FOO.replace('string src = """', 'string src = """\n            partial class Decoy { }')
    text = 'const string S = """\n    partial class Decoy2 { }\n    """;\n' + text
    assert set(_parse(text)) == {"FooTests.Drops", "FooTests.Keeps"}
