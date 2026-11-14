from types import SimpleNamespace

import pytest

from receipts import checks
from receipts.models import Evidence, FileChange, Finding, Mutant, ReceiptPlan, RouteResult
from receipts.models import TestCase as Case
from receipts.models import TestRun as Run
from receipts.mutants import added_lines, candidates, generate

# Mutant generation ---------------------------------------------------------------------------

FILE = """class C
{
    void M(string s, int[] a, int i)
    {
        if (s is null)
        {
            return;
        }

        if (i < a.Length && a[0] == 1)
        {
            Report(s);
        }

        var ok = true;
        Report(s);
        int j = i + 1;
    }
}""".split("\n")


def _cands(line_no: int) -> dict[str, str]:
    return dict(candidates(FILE, line_no - 1))


def test_exit_guard_is_removed_to_false_and_negated():
    c = _cands(5)
    assert c["negate-condition"] == "        if (!(s is null))"
    assert c["remove-guard"] == "        if (false)"


def test_wrapping_guard_is_removed_to_true_and_bounds_mutated():
    c = _cands(10)
    assert c["remove-guard"] == "        if (true)"
    assert c["off-by-one"] == "        if (i <= a.Length && a[0] == 1)"
    assert c["negate-condition"].startswith("        if (!(i < a.Length")


def test_swap_boolean_and_delete_statement():
    assert _cands(15) == {"swap-boolean": "        var ok = false;"}  # a declaration is not deleted
    assert _cands(16) == {"delete-statement": ""}
    assert _cands(17) == {"off-by-one": "        int j = i - 1;"}


def test_non_code_lines_have_no_mutants():
    assert _cands(6) == {} and _cands(1) == {}
    assert candidates(["    /// <summary>if (x) return;</summary>"], 0) == []


def test_multiline_statement_continuation_is_not_deleted():
    lines = ["    var d = cond", "        ? A()", "        : B();"]
    assert "delete-statement" not in dict(candidates(lines, 2))


def test_added_lines_track_head_line_numbers():
    hunk = " ctx\n-old\n+new1\n ctx2\n+new2"
    assert added_lines(hunk, 10) == [(11, "new1"), (13, "new2")]


def test_generate_is_deterministic_capped_and_diverse():
    hunk = "\n".join(f"+{ln}" for ln in FILE)
    a = generate("C.cs", FILE, hunk, 1, cap=5)
    b = generate("C.cs", FILE, hunk, 1, cap=5)
    assert a == b and len(a) == 5
    assert {m.operator for m in a} == {"negate-condition", "remove-guard", "off-by-one",
                                       "swap-boolean", "delete-statement"}
    assert [m.line for m in a] == sorted(m.line for m in a)


def test_generate_skips_stale_lines_and_repeated_text():
    lines = ["        x: true,", "        x: true,"]
    hunk = "+        x: true,\n+        x: true,"
    assert len(generate("D.cs", lines, hunk, 1, cap=5)) == 1
    assert generate("D.cs", ["different"], "+        x: true,", 1, cap=5) == []


# Fail-before / pass-after classification ------------------------------------------------------

NAMES = ["Req.A", "Req.B"]


def _run(side, compiled=True, **outcomes):
    cases = [Case(name=f"Req.{k}", outcome=v) for k, v in outcomes.items()]
    return Run(receipt_id=f"r-{side}", side=side, compiled=compiled, total=len(cases),
                   passed=sum(c.outcome == "passed" for c in cases),
                   failed=sum(c.outcome == "failed" for c in cases), cases=cases)


@pytest.mark.parametrize("base,head,expects,kind,result,fragment", [
    (_run("base", A="failed", B="passed"), _run("head", A="passed", B="passed"),
     "fail-on-base-pass-on-head", "fail-before-pass-after", "pass", "fails on base (1/2)"),
    (_run("base", compiled=False), _run("head", A="passed", B="passed"),
     "new-api", "new-api", "pass", "new-API evidence (weaker)"),
    (_run("base", A="failed", B="failed"), _run("head", A="failed", B="passed"),
     "fail-on-base-pass-on-head", "fail-before-pass-after", "fail", "fails on head"),
    (_run("base", A="passed", B="passed"), _run("head", A="passed", B="passed"),
     "fail-on-base-pass-on-head", "fail-before-pass-after", "partial", "does not discriminate"),
    (None, _run("head", compiled=False), "fail-on-base-pass-on-head",
     "fail-before-pass-after", "error", "does not compile on head"),
    (None, _run("head", A="failed", B="passed"), "fail-on-head",
     "fail-before-pass-after", "fail", "edge-case test fails on head"),
    (None, _run("head", A="passed", B="passed"), "fail-on-head",
     "fail-before-pass-after", "pass", "no defect shown"),
])
def test_classify_generated_test(base, head, expects, kind, result, fragment):
    k, r, title = checks.classify_generated_test(base, head, NAMES, expects)
    assert (k, r) == (kind, result) and fragment in title


def test_fixture_tests_load_req_2283():
    [gt] = checks.fixture_tests("REQ-2283")
    assert gt["expects"] == "fail-on-base-pass-on-head" and gt["lens"] == "requirement-tests"
    assert gt["path"].endswith("REQ-2283.cs")
    assert len(gt["test_names"]) == 3
    assert all(n.startswith("Req2283ClosedGenericFromReferencedAssemblyTests.")
               for n in gt["test_names"])
    assert checks.name_filter(["X.a", "X.b"]) == "FullyQualifiedName~X.a|FullyQualifiedName~X.b"
    assert checks.fixture_tests("REQ-0000") == []


# Mutant status ---------------------------------------------------------------------------------


def test_mutant_status():
    killed = _run("head", A="failed", B="passed")
    assert checks.mutant_status(killed, 1, "") == ("killed", ["Req.A"])
    assert checks.mutant_status(_run("head", A="passed"), 0, "")[0] == "survived"
    assert checks.mutant_status(_run("head", compiled=False), 1, "x.cs(1,1): error CS1002")[0] \
        == "compile-error"
    assert checks.mutant_status(_run("head", compiled=False), 1, "host crashed")[0] == "killed"
    assert checks.mutant_status(_run("head", compiled=False), 124, "")[0] == "timeout"


# Finding verification mapping --------------------------------------------------------------------


def _finding(fid, lens, kind, location="src/X.cs:10", detail=""):
    return Finding(id=fid, lens=lens, claim="claim", location=location, severity="high",
                   receipt_plan=ReceiptPlan(kind=kind, detail=detail))


def _ev(eid, kind, result, **details):
    return Evidence(id=eid, kind=kind, title=eid, result=result, details=details)


def test_failing_head_test_verifies_the_named_finding_only():
    ev = [_ev("gentest-1", "fail-before-pass-after", "fail", lens="logic-edge",
              failed_on_head=["Edge.EmptyInputThrows"])]
    fs = [_finding("f1", "logic-edge", "test", detail="EmptyInputThrows fails on head"),
          _finding("f2", "logic-edge", "test", detail="some other test")]
    out = {f.id: f for f in checks.verify_findings(fs, ev, [])}
    assert out["f1"].verified and out["f1"].evidence_ids == ["gentest-1"]
    assert not out["f2"].verified


def test_single_unnamed_test_finding_maps_to_single_failing_evidence():
    ev = [_ev("gentest-1", "fail-before-pass-after", "fail", lens="requirement-tests",
              failed_on_head=["Req.A"])]
    [f] = checks.verify_findings([_finding("f", "requirement-tests", "test")], ev, [])
    assert f.verified and f.evidence_ids == ["gentest-1"]


def test_passing_evidence_never_verifies():
    ev = [_ev("gentest-1", "fail-before-pass-after", "pass", lens="requirement-tests",
              failed_on_head=[]),
          _ev("compat-bytes", "byte-diff", "pass"), _ev("regeneration", "regeneration", "pass")]
    fs = [_finding("t", "requirement-tests", "test"), _finding("b", "compatibility", "byte-diff"),
          _finding("r", "test-quality", "regeneration"), _finding("c", "security", "code-citation")]
    assert not any(f.verified for f in checks.verify_findings(fs, ev, []))


def test_mutation_and_byte_diff_findings_map_to_their_evidence():
    survivor = Mutant(id="m1", file="src/X.cs", line=10, operator="negate-condition",
                      original="a", mutated="b", status="survived")
    killed = Mutant(id="m2", file="src/Y.cs", line=3, operator="off-by-one", original="a",
                    mutated="b", status="killed")
    ev = [_ev("mutation", "mutation", "partial"), _ev("compat-bytes", "byte-diff", "fail")]
    fs = [_finding("m", "test-quality", "mutation", location="src/X.cs:12"),
          _finding("k", "test-quality", "mutation", location="src/Y.cs:3"),
          _finding("b", "compatibility", "byte-diff")]
    out = {f.id: f for f in checks.verify_findings(fs, ev, [survivor, killed])}
    assert out["m"].evidence_ids == ["mutation"] and out["m"].verified
    assert not out["k"].verified  # every mutant in that file was killed: nothing backs the claim
    assert out["b"].evidence_ids == ["compat-bytes"]


# Regeneration mapping and compat trigger ---------------------------------------------------------

RES = "tests/MessagePack.SourceGenerator.Tests/Resources"


def test_map_generated_matches_theory_arguments():
    head = Run(receipt_id="h", side="head", compiled=True, cases=[
        Case(name="GenerationTests.EnumFormatter(container: Namespace, usesMapMode: False)",
                 outcome="passed"),
        Case(name="GenerationTests.EnumFormatter(container: None, usesMapMode: True)",
                 outcome="passed"),
        Case(name="GenericsFormatterTests.GenericType", outcome="passed"),
    ])
    m = checks.map_generated([f"{RES}/EnumFormatter(Namespace, False)/a.g.cs",
                              f"{RES}/GenericType/b.g.cs", f"{RES}/Unknown/c.g.cs",
                              "src/Elsewhere.g.cs"], head)
    assert [c.name for c in m[f"{RES}/EnumFormatter(Namespace, False)/a.g.cs"]] == [
        "GenerationTests.EnumFormatter(container: Namespace, usesMapMode: False)"]
    assert len(m[f"{RES}/GenericType/b.g.cs"]) == 1
    assert m[f"{RES}/Unknown/c.g.cs"] == [] and m["src/Elsewhere.g.cs"] == []


def _ctx(paths_kinds, categories=()):
    files = [FileChange(path=p, kind=k, status="modified", additions=1, deletions=0, area="x")
             for p, k in paths_kinds]
    routes = [RouteResult(hunk_id=f"h{i}", risk=3, category=c, confidence=1, router="rule")
              for i, c in enumerate(categories)]
    return SimpleNamespace(intake=SimpleNamespace(files=files),
                           routing=SimpleNamespace(routes=routes))


def test_compat_needed():
    assert checks.compat_needed(_ctx([("src/MessagePack.Annotations/Attributes.cs", "source")]))
    assert checks.compat_needed(_ctx([("src/MessagePack/Formatters/X.cs", "source")]))
    assert not checks.compat_needed(_ctx(
        [("src/MessagePack.SourceGenerator/CodeAnalysis/TypeCollector.cs", "source")],
        categories=["correctness"]))
    assert checks.compat_needed(_ctx([("README.md", "docs")], categories=["compatibility"]))
