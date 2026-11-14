"""Blast radius: tools/impact parsing, rule floors, characterization classification, impact.json."""

from pathlib import Path

import pytest

from receipts import config, impact, verdict
from receipts.characterize import classify, outcomes, select_targets
from receipts.lenses import regression
from receipts.models import Hunk
from receipts.models import TestCase as Case
from receipts.models import TestRun as Run
from receipts.routers.rule import RuleRouter

LIB_BASE = """namespace A
{
    public class Lib
    {
        public int Used(int x)
        {
            return x + 1;
        }

        public int Gone() => 0;

        public int Same() => 2;
    }
}
"""

LIB_HEAD = """namespace A
{
    public class Lib
    {
        public int Used(int x)
        {
            return x + 2;
        }

        public int Fresh() => 1;

        public int Same() => 2;
    }
}
"""

CALLER = """namespace A
{
    public class Caller
    {
        public int Run(Lib l) => l.Used(2);

        public int Unrelated() => new Other().Used(1);
    }

    public class Other
    {
        public int Used(int x) => x;
    }
}
"""

FEATURE = """namespace A
{
    public class Feature
    {
        public int Go() => new Lib().Used(3);
    }
}
"""

TEST = """public class LibTests
{
    public void T() { var l = new A.Lib(); l.Used(1); }
}
"""


def _write(root: Path, files: dict[str, str]) -> None:
    for rel, text in files.items():
        p = root / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text)


@pytest.fixture(scope="module")
def tool_output(tmp_path_factory):
    if not Path(config.DOTNET).exists():
        pytest.skip("dotnet SDK not installed")
    tmp = tmp_path_factory.mktemp("impact")
    base, head = tmp / "base", tmp / "head"
    _write(base, {"src/A/Lib.cs": LIB_BASE, "src/A/Caller.cs": CALLER})
    _write(head, {"src/A/Lib.cs": LIB_HEAD, "src/A/Caller.cs": CALLER,
                  "src/A/Feature.cs": FEATURE, "tests/T/LibTests.cs": TEST})
    run_dir = tmp / "run"
    (run_dir / "logs").mkdir(parents=True)
    raw, why = impact.analyze(["src/A/Lib.cs", "src/A/Feature.cs"], base, head, run_dir, [])
    assert raw is not None, why
    return raw


def _member(raw, name):
    return next(m for m in raw["members"] if m["member"] == name and m["type_name"] in ("Lib", "Feature"))


def test_tool_classifies_new_modified_deleted(tool_output):
    kinds = {(m["type_name"], m["member"]): m["kind"] for m in tool_output["members"]}
    assert kinds[("Lib", "Used")] == "modified"
    assert kinds[("Lib", "Fresh")] == "new"
    assert kinds[("Lib", "Gone")] == "deleted"
    assert kinds[("Feature", "Go")] == "new"
    assert ("Lib", "Same") not in kinds  # unchanged members are omitted
    assert not _member(tool_output, "Used")["signature_changed"]


def test_tool_finds_callers_inside_and_outside_the_change(tool_output):
    used = _member(tool_output, "Used")
    by_file = {c["file"]: c for c in used["callers"]}
    assert set(by_file) == {"src/A/Caller.cs", "src/A/Feature.cs"}
    assert by_file["src/A/Caller.cs"]["in_change"] is False
    assert by_file["src/A/Caller.cs"]["line"] == 5
    assert by_file["src/A/Caller.cs"]["member"] == "Caller.Run"
    assert by_file["src/A/Feature.cs"]["in_change"] is True
    # `new Other().Used(1)`: the receiver is visibly another type, so it is not a caller.
    assert all(c["member"] != "Caller.Unrelated" for c in used["callers"])
    assert [c["file"] for c in used["test_callers"]] == ["tests/T/LibTests.cs"]
    assert used["same_name_declarations"] == 1  # Other.Used
    assert used["ambiguous"] is False  # every receiver was resolved


def _hunk(file: str, old_start: int, new_start: int, text: str, kind: str = "source") -> Hunk:
    n_old = sum(not ln.startswith("+") for ln in text.split("\n"))
    n_new = sum(not ln.startswith("-") for ln in text.split("\n"))
    return Hunk(id=f"{file}#1", file=file, kind=kind, old_start=old_start, old_lines=n_old,
                new_start=new_start, new_lines=n_new, text=text)


RAW = {"members": [
    {"name": "Lib.Used(int)", "member": "Used", "type_name": "Lib", "file": "src/A/Lib.cs",
     "kind": "modified", "signature_changed": False, "head_span": [5, 8], "base_span": [5, 8],
     "callers": [{"file": "src/A/Caller.cs", "line": 5, "statement_start": 5, "member": "Caller.Run",
                  "in_change": False}]},
    {"name": "Lib.Fresh()", "member": "Fresh", "type_name": "Lib", "file": "src/A/Lib.cs",
     "kind": "new", "signature_changed": False, "head_span": [10, 10], "base_span": None,
     "callers": []},
    {"name": "Lib.Gone()", "member": "Gone", "type_name": "Lib", "file": "src/A/Lib.cs",
     "kind": "deleted", "signature_changed": False, "head_span": None, "base_span": [20, 20],
     "callers": [{"file": "src/B.cs", "line": 3, "statement_start": 3, "member": "B.M",
                  "in_change": False}]},
    {"name": "Lib.Local()", "member": "Local", "type_name": "Lib", "file": "src/A/Lib.cs",
     "kind": "modified", "signature_changed": False, "head_span": [30, 32], "base_span": [30, 32],
     "callers": [{"file": "src/A/Lib.cs", "line": 10, "statement_start": 10, "member": "Lib.Fresh",
                  "in_change": True}]},
]}


def _route(h: Hunk, area: str = "type-collector"):
    return RuleRouter().route(h, "req", {"area": area, "impact": RAW})


def test_floor_8_for_modified_member_with_outside_caller():
    r = _route(_hunk("src/A/Lib.cs", 6, 6, " {\n-  return x + 1;\n+  return x + 2;\n }"))
    assert (r.risk, r.category, r.floor_applied) == (8, "compatibility", True)
    assert r.reason == "modifies existing Lib.Used used in 1 other places"


def test_no_extra_floor_for_new_member_or_in_change_callers_only():
    new = _route(_hunk("src/A/Lib.cs", 10, 10, "+public int Fresh() => 1;"), area="other")
    assert new.risk == 4 and not new.floor_applied
    local = _route(_hunk("src/A/Lib.cs", 31, 31, "-a\n+b"), area="other")
    assert local.risk == 4


def test_floor_9_for_deleted_member_with_callers():
    r = _route(_hunk("src/A/Lib.cs", 20, 20, "-public int Gone() => 0;"))
    assert (r.risk, r.floor_applied) == (9, True)
    assert r.reason.startswith("deletes existing Lib.Gone")


def test_context_lines_alone_do_not_trigger_the_floor():
    # Only line 9 changes; lines 6-8 of Used are context.
    r = _route(_hunk("src/A/Lib.cs", 6, 6, " a\n b\n c\n-old\n+new"), area="other")
    assert r.risk == 4


def test_floor_does_not_lower_a_higher_rule_risk():
    r = RuleRouter().route(_hunk("README.md", 6, 6, "-a\n+b", kind="docs"), "req",
                           {"area": "docs", "impact": RAW})
    assert r.risk == 1  # docs are never in a member


def _run(cases: dict[str, str] | None) -> Run:
    if cases is None:
        return Run(receipt_id="x", side="base", compiled=False)
    return Run(receipt_id="x", side="base", compiled=True, total=len(cases),
                   cases=[Case(name=f"Receipts.GeneratedTests.{n}", outcome=o)
                          for n, o in cases.items()])


def test_characterization_classification():
    names = ["C.A", "C.B"]
    base_ce = outcomes(_run(None), names)
    assert classify(base_ce, None, False) == ("compile-error", "not-run", "invalid", [])
    base_fail = outcomes(_run({"C.A": "failed", "C.B": "failed"}), names)
    assert classify(base_fail, None, False)[2] == "invalid"
    base_ok = outcomes(_run({"C.A": "passed", "C.B": "failed"}), names)
    head_fail = outcomes(_run({"C.A": "failed"}), ["C.A"])
    assert classify(base_ok, head_fail, True) == ("pass", "fail", "changed", ["C.A"])
    head_pass = outcomes(_run({"C.A": "passed"}), ["C.A"])
    assert classify(base_ok, head_pass, True) == ("pass", "pass", "preserved", ["C.A"])
    assert classify(base_ok, outcomes(_run(None), ["C.A"]), True)[1:3] == ("compile-error", "invalid")


def test_select_targets_caps_and_orders_by_outside_callers():
    def m(name, n):
        return {"name": name, "kind": "modified", "base_span": [1, 2],
                "callers": [{"in_change": False}] * n + [{"in_change": True}]}
    raw = {"members": [m("a", 1), m("b", 5), m("c", 0), m("d", 2), m("e", 3)]}
    assert [x["name"] for x in select_targets(raw)] == ["b", "e", "d"]


def test_regression_output_caps_tests_per_member():
    data = {"tests": [{"member": "M1", "path": "tests/MessagePack.SourceGenerator.Tests/Receipts/X.cs",
                       "content": "class X {}", "test_names": ["a", "b", "c", "d"], "pins": "p"},
                      {"member": "M9", "path": "tests/MessagePack.SourceGenerator.Tests/Receipts/Y.cs",
                       "content": "class Y {}", "test_names": ["a"], "pins": "p"}]}
    out = regression.parse_output(data, ["M1", "M2"])
    assert len(out) == 1 and out[0]["test_names"] == ["a", "b", "c"]
    assert out[0]["path"] == "Receipts/X.cs"
    with pytest.raises(regression.RegressionOutputError):
        regression.parse_output({"tests": [{**data["tests"][0], "path": "src/Evil.cs"}]}, ["M1"])


def test_impact_json_shape():
    raw = {"members": [dict(m, callers=[dict(c, tested=False) for c in m["callers"]])
                       for m in RAW["members"]]}
    char = [{"member": "Lib.Used(int)", "test_names": ["C.A"], "base": "pass", "head": "fail",
             "result": "changed", "evidence_id": "characterize-1"}]
    rep = impact.report(raw, char, coverage_available=True)
    assert list(rep) == ["method", "members", "characterization", "coverage_available", "summary"]
    assert rep["method"] == "name-based call sites (Roslyn syntax, not semantic)"
    m = rep["members"][0]
    assert list(m) == ["name", "file", "kind", "signature_changed", "callers",
                       "callers_outside_change", "untested_callers", "ambiguous"]
    assert list(m["callers"][0]) == ["file", "line", "member", "in_change", "tested"]
    assert rep["summary"] == {"modified_existing": 2, "new": 1, "deleted": 1,
                              "callers_outside_change": 2, "untested_callers": 2,
                              "behaviour_changes": 1}
    gaps = [n.what for n in impact.not_checked(raw, "", "")]
    assert "1 callers of Lib.Used are not exercised by any test (legacy coverage gap): " \
           "src/A/Caller.cs:5" in gaps


def test_mark_tested_uses_statement_lines_and_none_without_coverage():
    raw = {"members": [{"callers": [{"file": "f.cs", "line": 12, "statement_start": 10}]}]}
    impact.mark_tested(raw, {"f.cs": {10: 3}})
    assert raw["members"][0]["callers"][0]["tested"] is True
    impact.mark_tested(raw, {"f.cs": {13: 3}})
    assert raw["members"][0]["callers"][0]["tested"] is False
    impact.mark_tested(raw, None)
    assert raw["members"][0]["callers"][0]["tested"] is None


def test_verdict_never_auto_ready_with_callers_outside_the_change(tmp_path, monkeypatch):
    from test_verdict import _inputs  # noqa: PLC0415 - reuse the all-green fixture
    monkeypatch.setattr(config, "METRICS", tmp_path)
    (tmp_path / "trust.json").write_text('{"type-collector": 3}')
    inp = _inputs("type-collector")
    assert verdict.compose(inp).verdict == "auto-ready"
    inp.impact = impact.report(RAW, [], coverage_available=False)
    r = verdict.compose(inp)
    assert r.verdict == "needs-sign-off"
    assert "blast radius" in r.verdict_reason
